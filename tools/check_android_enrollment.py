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
import time
import uuid
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
COMMAND_CONTRACT_INPUTS = ("contracts/events/command-v1.schema.json", "contracts/fixtures/commands-v1.json",
                           "contracts/events/command-event-v1.schema.json", "contracts/fixtures/command-events-v1.json",
                           "contracts/events/event-ack-v1.schema.json", "contracts/fixtures/event-acks-v1.json")


def instrumentation_passed(text, expected_tests=1):
    if type(expected_tests) is not int or expected_tests < 1:
        return False
    label = "test" if expected_tests == 1 else "tests"
    return (re.search(rf"^OK \({expected_tests} {label}\)$", text, re.MULTILINE) is not None
            and "INSTRUMENTATION_CODE: -1" in text
            and "FAILURES" not in text and "INSTRUMENTATION_FAILED" not in text)


def verify_recovery_proof(prepared, recovered, nonce, reboot=False):
    if (type(prepared.get("schemaVersion")) is not int or prepared["schemaVersion"] != 1
            or type(recovered.get("schemaVersion")) is not int or recovered["schemaVersion"] != 1
            or prepared.get("probe") != nonce or recovered.get("probe") != nonce
            or str(uuid.UUID(prepared["workId"])) != prepared["workId"]
            or recovered.get("workId") != prepared["workId"] or recovered.get("state") != "SUCCEEDED"
            or type(prepared.get("processId")) is not int or prepared["processId"] <= 0
            or type(recovered.get("preparedProcessId")) is not int or recovered["preparedProcessId"] != prepared["processId"]
            or type(recovered.get("recoveredProcessId")) is not int or recovered["recoveredProcessId"] <= 0
            or (not reboot and recovered["recoveredProcessId"] == prepared["processId"])):
        raise ValueError("Persisted work/process recovery proof missing")
    if reboot and (type(prepared.get("bootCount")) is not int or prepared["bootCount"] < 0
                   or type(recovered.get("preparedBootCount")) is not int
                   or recovered["preparedBootCount"] != prepared["bootCount"]
                   or type(recovered.get("recoveredBootCount")) is not int
                   or recovered["recoveredBootCount"] <= prepared["bootCount"]):
        raise ValueError("Persisted work reboot proof missing")
    for attempt, name in enumerate(("first", "second")):
        record = recovered.get(name, {})
        if (type(record.get("attempt")) is not int or record["attempt"] != attempt
                or record.get("workId") != prepared["workId"]
                or type(record.get("processId")) is not int or record["processId"] != recovered["recoveredProcessId"]
                or type(record.get("elapsedMillis")) is not int):
            raise ValueError("Recovered attempt proof missing")
    if recovered["second"]["elapsedMillis"] - recovered["first"]["elapsedMillis"] < 29000:
        raise ValueError("Recovered retry backoff missing")


def verify_boot_transition(before, after):
    if (str(uuid.UUID(before)) != before or str(uuid.UUID(after)) != after or before == after):
        raise ValueError("Kernel boot ID did not change")


def run_process_recovery(command, adb, output, reboot=False):
    nonce = str(uuid.uuid4())
    fixture = f"no_backup/scheduler-probe-{nonce}"

    def phase(method):
        text = command([*adb, "shell", "am", "instrument", "-w", "-r", "-e", "probe", nonce,
                "-e", "reboot", "true" if reboot else "false",
                        "-e", "class", "dev.fleet.agent.EventSchedulerRecoveryInstrumentedTest#" + method,
                        "dev.fleet.agent.test/androidx.test.runner.AndroidJUnitRunner"], 140)
        if not instrumentation_passed(text):
            raise RuntimeError("Recovery phase did not explicitly pass")

    original_failure = None
    try:
        phase("preparePersistedWorkBeforeHostForceStop")
        prepared = json.loads(command([*adb, "exec-out", "run-as", "dev.fleet.agent", "cat", fixture + "/prepared.json"], 30))
        if type(prepared.get("processId")) is not int or prepared["processId"] <= 0:
            raise ValueError("Prepared process ID missing")
        (output / "recovery-prepared.json").write_text(json.dumps(prepared, indent=2) + "\n")
        command([*adb, "shell", "am", "force-stop", "dev.fleet.agent"], 30)
        command([*adb, "shell", "test", "!", "-d", f"/proc/{prepared['processId']}"], 30)
        (output / "process-stop-verification.json").write_text(json.dumps(
            {"schemaVersion": 1, "probe": nonce, "preparedProcessId": prepared["processId"],
             "forceStopIssued": True, "oldProcessAbsent": True, "explicitRelaunch": True}, indent=2) + "\n")
        if reboot:
            before = command([*adb, "shell", "cat", "/proc/sys/kernel/random/boot_id"], 30).strip()
            command([*adb, "reboot"], 30)
            command([*adb, "wait-for-disconnect"], 60)
            command([*adb, "wait-for-device"], 240)
            deadline = time.monotonic() + 240
            while command([*adb, "shell", "getprop", "sys.boot_completed"], 30).strip() != "1":
                if time.monotonic() >= deadline:
                    raise RuntimeError("Android boot did not complete after reboot")
            after = command([*adb, "shell", "cat", "/proc/sys/kernel/random/boot_id"], 30).strip()
            verify_boot_transition(before, after)
            command([*adb, "shell", "ls", "/sdcard"], 30)
            (output / "reboot-verification.json").write_text(json.dumps(
                {"schemaVersion": 1, "probe": nonce, "beforeBootId": before, "afterBootId": after,
                 "explicitRelaunch": True}, indent=2) + "\n")
        phase("resumePersistedWorkAfterHostVerifiedForceStop")
        recovered = json.loads(command([*adb, "exec-out", "run-as", "dev.fleet.agent", "cat", fixture + "/verified.json"], 30))
        verify_recovery_proof(prepared, recovered, nonce, reboot=reboot)
        (output / "recovery-verified.json").write_text(json.dumps(recovered, indent=2) + "\n")
    except Exception as failure:
        original_failure = failure
        raise
    finally:
        try:
            phase("cleanupOnlyThisRecoveryProbe")
            command([*adb, "shell", "run-as", "dev.fleet.agent", "test", "!", "-d", fixture], 30)
        except Exception as cleanup_failure:
            if original_failure is not None:
                raise RuntimeError(f"{original_failure}; recovery probe cleanup also failed") from cleanup_failure
            raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--serial")
    parser.add_argument("--expected-avd")
    parser.add_argument("--command-journal", action="store_true", help="Run six isolated SQLite journal scenarios on the explicit emulator")
    parser.add_argument("--event-acknowledgements", action="store_true", help="Run five isolated SQLite/TLS acknowledgement deletion scenarios")
    parser.add_argument("--event-worker", action="store_true", help="Run six isolated SQLite/TLS WorkManager worker scenarios")
    parser.add_argument("--event-scheduler", action="store_true", help="Run two actual WorkManager dispatch/backoff/cancellation probe scenarios")
    parser.add_argument("--scheduler-recovery", action="store_true", help="Verify persisted probe work across app force-stop and explicit relaunch")
    parser.add_argument("--scheduler-reboot", action="store_true", help="Verify persisted probe work across force-stop, actual reboot and explicit relaunch")
    args = parser.parse_args()
    if bool(args.serial) != bool(args.expected_avd):
        parser.error("--serial and --expected-avd must be supplied together")
    if args.serial and not args.serial.startswith("emulator-"):
        parser.error("Only an explicitly named disposable emulator is supported")
    if (args.command_journal or args.event_acknowledgements or args.event_worker or args.event_scheduler or args.scheduler_recovery or args.scheduler_reboot) and not args.serial:
        parser.error("Journal scenarios require an explicit emulator and expected AVD")
    if sum((args.command_journal, args.event_acknowledgements, args.event_worker, args.event_scheduler, args.scheduler_recovery, args.scheduler_reboot)) > 1:
        parser.error("Choose one isolated journal suite")
    started = datetime.datetime.now(datetime.timezone.utc)
    output = ROOT / ("artifacts/android-scheduler-reboot" if args.scheduler_reboot else
                     "artifacts/android-scheduler-recovery" if args.scheduler_recovery else
                     "artifacts/android-event-scheduler" if args.event_scheduler else
                     "artifacts/android-event-worker" if args.event_worker else
                     "artifacts/android-event-acknowledgements" if args.event_acknowledgements else
                     "artifacts/android-command-journal" if args.command_journal else "artifacts/android-enrollment") / started.strftime("%Y%m%dT%H%M%S.%fZ")
    output.mkdir(parents=True)
    hashes = {}
    sources = [*list((ROOT / "android").rglob("*")), *(ROOT / name for name in COMMAND_CONTRACT_INPUTS)]
    for source in sorted(sources):
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
        if counters["tests"] < 42 or any(counters[name] for name in ("failures", "errors", "skipped")):
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
            test_class = ("EventSchedulerInstrumentedTest" if args.event_scheduler else
                          "EventUploadWorkerInstrumentedTest" if args.event_worker else
                          "EventAcknowledgementInstrumentedTest" if args.event_acknowledgements else
                          "CommandJournalInstrumentedTest" if args.command_journal else "EnrollmentIdentityInstrumentedTest")
            if args.scheduler_recovery or args.scheduler_reboot:
                run_process_recovery(command, adb, output, reboot=args.scheduler_reboot)
            else:
                result = command([*adb, "shell", "am", "instrument", "-w", "-r", "-e", "class",
                                  "dev.fleet.agent." + test_class,
                                  "dev.fleet.agent.test/androidx.test.runner.AndroidJUnitRunner"], 120)
                if not instrumentation_passed(result, 2 if args.event_scheduler else 6 if args.event_worker or args.command_journal else 5 if args.event_acknowledgements else 1):
                    raise RuntimeError("Instrumentation did not explicitly pass")
        passed = True
    except Exception as failure:
        error = str(failure)
    manifest = {"schemaVersion": 1, "startedAt": started.isoformat(),
                "finishedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "result": "passed" if passed else "failed", "error": error, "commands": records,
                "inputSha256": hashes, "apkSha256": apk_hashes, "jvmCounters": counters, "serial": args.serial,
                "expectedAvd": args.expected_avd,
                "commandJournalRequested": args.command_journal,
                "eventAcknowledgementsRequested": args.event_acknowledgements,
                "eventWorkerRequested": args.event_worker,
                "eventSchedulerRequested": args.event_scheduler,
                "schedulerRecoveryRequested": args.scheduler_recovery,
                "schedulerRebootRequested": args.scheduler_reboot,
                "scope": ("API34 debug-probe persisted WorkManager recovery across host force-stop, actual emulator reboot with changed kernel boot ID and increased Android boot count, then explicit instrumentation relaunch. Same work ID, attempts 0/1, real first backoff, scoped cleanup. No enqueue on resume or identity/journal reset. Package force-stopped until explicit launch; foreground GreedyScheduler possible. Not autonomous boot dispatch, JobScheduler-only execution, actual event/Keystore/backend recovery, abrupt power loss or production proof." if args.scheduler_reboot else
                    "API34 persisted debug-probe WorkManager recovery across host-issued app force-stop with verified old PID absence, then explicit instrumentation relaunch/new PID; same work ID, attempts 0/1, real first backoff, terminal cleanup. No enqueue on resume, test clock, production identity/journal reset or forced scheduler run. Force-stop prevents autonomous execution until relaunch; foreground GreedyScheduler possible. Not automatic dead-process/JobScheduler-only delivery, reboot or actual event recovery." if args.scheduler_recovery else
                    "Two actual API34 WorkManager debug-probe scheduling scenarios: real dispatch, persisted same-ID retry after exponential 30-second backoff, unique KEEP deduplication, cancellation of delayed work without dispatch. No forced run/test clock, production identity/journal access or database reset. Foreground instrumentation permits GreedyScheduler; not proof of JobScheduler-only dispatch, constraints, in-flight event-worker OS cancellation, process death or reboot recovery." if args.event_scheduler else
                    "Six API34 SQLite/software-key loopback mTLS worker-body/request-policy scenarios: bounded batch continuation, transient replay, permanent/exhausted failures retain rows, stop callback cancellation, slow-response deadline and WorkManager network/backoff/input configuration. No actual OS-scheduled backoff/cancel/reboot, real Keystore/backend, process kill or power loss." if args.event_worker else
                    "Five real SQLite/software-key loopback mTLS acknowledgement deletion scenarios: exact-row deletion, retained inbox/dedup after reopen, rejected replies preserve rows, delete failure rollback/retry, concurrent duplicate uploads and changed in-flight row preservation. No backend, real Keystore, worker, process kill or power loss." if args.event_acknowledgements else
                    "Android JVM crypto/parser/loopback enrollment/mTLS and command/event encoding, APK build/lint; optional explicit emulator: six isolated Room receipt/outbox scenarios when commandJournalRequested, otherwise one Keystore scenario. No live command intake, execution, acknowledgement deletion, broker/backend runtime, process-kill or power-loss proof."),
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