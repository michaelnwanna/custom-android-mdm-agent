import argparse
import datetime
import hashlib
import html
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def instrumentation_passed(text):
    return (re.search(r"^OK \(1 test\)$", text, re.MULTILINE) is not None
            and "INSTRUMENTATION_CODE: -1" in text
            and "FAILURES" not in text and "INSTRUMENTATION_FAILED" not in text)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--serial")
    parser.add_argument("--expected-avd")
    args = parser.parse_args()
    if bool(args.serial) != bool(args.expected_avd):
        parser.error("--serial and --expected-avd must be supplied together")
    if args.serial and not args.serial.startswith("emulator-"):
        parser.error("Only an explicitly named disposable emulator is supported")
    started = datetime.datetime.now(datetime.timezone.utc)
    output = ROOT / "artifacts/android-enrollment" / started.strftime("%Y%m%dT%H%M%S.%fZ")
    output.mkdir(parents=True)
    hashes = {}
    for source in sorted((ROOT / "android").rglob("*")):
        relative = source.relative_to(ROOT)
        if not source.is_file() or {"build", ".gradle", ".kotlin"}.intersection(relative.parts):
            continue
        if source.name == "local.properties":
            continue
        destination = output / "inputs" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        hashes[str(relative)] = hashlib.sha256(destination.read_bytes()).hexdigest()
    for name in ("check_android_enrollment.py", "test_android_enrollment.py"):
        source = ROOT / "tools" / name
        destination = output / "inputs/tools" / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        hashes[str(source.relative_to(ROOT))] = hashlib.sha256(destination.read_bytes()).hexdigest()
    working = output / "inputs/android"
    sdk = Path(os.environ.get("ANDROID_HOME", Path.home() / "Library/Android/sdk"))
    environment = dict(os.environ, ANDROID_HOME=str(sdk))
    records = []

    def command(arguments, timeout=600):
        try:
            process = subprocess.run(list(map(str, arguments)), cwd=working, env=environment,
                                     capture_output=True, text=True, timeout=timeout)
            entry = {"argv": list(map(str, arguments)), "returncode": process.returncode,
                     "stdout": process.stdout, "stderr": process.stderr}
        except (OSError, subprocess.TimeoutExpired) as error:
            entry = {"argv": list(map(str, arguments)), "returncode": 124, "stdout": "", "stderr": str(error)}
        records.append(entry)
        (output / f"command-{len(records):02d}.json").write_text(json.dumps(entry, indent=2) + "\n")
        if entry["returncode"]:
            raise RuntimeError("Recorded command failed")
        return entry["stdout"]

    passed = False
    error = None
    counters = None
    apk_hashes = {}
    try:
        command(["./gradlew", "--version"])
        command(["./gradlew", ":agent:dependencies", "--configuration", "debugRuntimeClasspath", "--console=plain"])
        command(["./gradlew", ":agent:testDebugUnitTest", ":agent:assembleDebug", ":agent:lintDebug",
                 ":agent:assembleDebugAndroidTest", "--console=plain"])
        reports = working / "agent/build/test-results/testDebugUnitTest"
        counters = {"tests": 0, "failures": 0, "errors": 0, "skipped": 0}
        for filename in reports.glob("TEST-*.xml"):
            suite = ET.parse(filename).getroot()
            for name in counters:
                counters[name] += int(suite.get(name, "0"))
        if counters["tests"] < 22 or any(counters[name] for name in ("failures", "errors", "skipped")):
            raise RuntimeError("Missing or unsuccessful JVM tests")
        for source in (working / "agent/build/reports").rglob("*"):
            if source.is_file():
                destination = output / "reports" / source.relative_to(working / "agent/build/reports")
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, destination)
        shutil.copytree(reports, output / "jvm-results")
        apks = [working / "agent/build/outputs/apk" / relative for relative in
            ("debug/agent-debug.apk", "androidTest/debug/agent-debug-androidTest.apk")]
        for apk in apks:
            apk_hashes[str(apk.relative_to(output))] = hashlib.sha256(apk.read_bytes()).hexdigest()
        if args.serial:
            adb = [sdk / "platform-tools/adb", "-s", args.serial]
            avd = command([*adb, "emu", "avd", "name"], 30).splitlines()[0].strip()
            if avd != args.expected_avd:
                raise RuntimeError("AVD does not match explicit target")
            if command([*adb, "shell", "getprop", "sys.boot_completed"], 30).strip() != "1":
                raise RuntimeError("Android boot is incomplete")
            command([*adb, "shell", "ls", "/sdcard"], 30)
            command([*adb, "shell", "getprop", "ro.build.fingerprint"], 30)
            command([*adb, "shell", "dpm", "list-owners"], 30)
            for apk in apks:
                command([*adb, "install", "-r", "-t", apk], 120)
            result = command([*adb, "shell", "am", "instrument", "-w", "-r", "-e", "class",
                              "dev.fleet.agent.EnrollmentIdentityInstrumentedTest",
                              "dev.fleet.agent.test/androidx.test.runner.AndroidJUnitRunner"], 120)
            if not instrumentation_passed(result):
                raise RuntimeError("Instrumentation did not explicitly pass")
        passed = True
    except Exception as failure:
        error = str(failure)
    manifest = {"schemaVersion": 1, "startedAt": started.isoformat(),
                "finishedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "result": "passed" if passed else "failed", "error": error, "commands": records,
                "inputSha256": hashes, "apkSha256": apk_hashes, "jvmCounters": counters, "serial": args.serial,
                "expectedAvd": args.expected_avd,
                "scope": "Android JVM crypto/parser/loopback enrollment and mTLS, APK build/lint; optional isolated real Keystore instrumentation. No combined ASP.NET/PostgreSQL/device runtime, MQTT mTLS, process restart or power-loss proof.",
                "fileSha256": {str(filename.relative_to(output)): hashlib.sha256(filename.read_bytes()).hexdigest()
                               for filename in sorted(output.rglob("*")) if filename.is_file()
                               and "inputs" not in filename.relative_to(output).parts}}
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    (output / "replay.html").write_text("<!doctype html><meta charset='utf-8'><title>Android enrollment checks</title>"
        f"<h1>Android enrollment checks</h1><p>{manifest['result']}</p><p>{html.escape(manifest['scope'])}</p>"
        "<p><a href='manifest.json'>Manifest, source hashes and commands</a></p>"
        f"<pre>{html.escape(json.dumps(counters))}\n{html.escape(error or '')}</pre>")
    print(f"{manifest['result'].upper()}: {output / 'replay.html'}")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())