import argparse
import base64
import datetime
import hashlib
import html
import importlib.metadata
import json
import os
from pathlib import Path
import re
import selectors
import shutil
import subprocess
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET

from check_android_enrollment import COMMAND_CONTRACT_INPUTS, instrumentation_passed


ROOT = Path(__file__).resolve().parents[1]


def public_configuration(configuration):
    bootstrap = configuration["bootstrap"]
    result = {"schemaVersion": 1, "stage": "ready", "namespace": configuration["namespace"],
            "serverRootDer": configuration["serverRootDer"], "deviceRootDer": configuration["deviceRootDer"],
            "bootstrap": {name: bootstrap[name] for name in ("schemaVersion", "endpoint", "pendingEnrollmentId")}}
    if "mqttPort" in configuration:
        result.update(mqttPort=configuration["mqttPort"], mqttProbe=configuration["mqttProbe"])
    return result


def verify_mqtt_proof(device, broker):
    if device.get("mqttAuthenticated") is not True or device.get("mqttDisabledRejected") is not True:
        raise ValueError("Android MQTT proof missing")
    response = json.loads(base64.b64decode(device["responseBase64"], validate=True))
    fingerprint = hashlib.sha256(base64.b64decode(response["certificateDer"], validate=True)).hexdigest()
    if (broker.get("stage") != "mqtt-verified" or broker.get("stopped") is not True
            or broker.get("identities") != {device["deviceId"]: fingerprint}
            or broker.get("probeRoundtrips") != 1):
        raise ValueError("Independent broker identity/roundtrip proof missing")
    if not all(type(broker.get(name)) is int and broker[name] >= 1 for name in
               ("crossDeviceDenied", "wildcardDenied", "disabledSubscribeDenied", "disabledConnectionsRejected")):
        raise ValueError("Independent broker rejection proof missing")
    receipt = device.get("mqttReceipt")
    if (device.get("mqttJournalVerified") is not True or not isinstance(receipt, dict)
            or set(receipt) != {"schemaVersion", "eventId", "commandId", "deviceId", "correlationId", "recordedAt", "state"}
            or type(receipt["schemaVersion"]) is not int or receipt["schemaVersion"] != 1
            or receipt["state"] != "received" or receipt["deviceId"] != device["deviceId"]
            or receipt["commandId"] != broker.get("commandId") or receipt["correlationId"] != broker.get("commandId")
            or type(broker.get("commandDeliveries")) is not int or broker["commandDeliveries"] != 2):
        raise ValueError("Durable MQTT receipt proof missing")
    for name in ("eventId", "commandId"):
        if str(uuid.UUID(receipt[name])) != receipt[name]:
            raise ValueError("Invalid receipt identifier")
    recorded = datetime.datetime.fromisoformat(receipt["recordedAt"].replace("Z", "+00:00"))
    if recorded.tzinfo is None:
        raise ValueError("Invalid receipt timestamp")


def public_tls_diagnostics(stderr):
    return [line for line in stderr.splitlines() if line in
            {"TLS:ACCEPTED", "TLS:PROOF_REJECTED", "TLS:CHAIN_REJECTED"}
            or re.fullmatch(r"TLS:ERROR:(?:AuthenticationException|SslException|IOException|Win32Exception|CryptographicException|AppleCommonCryptoCryptographicException|InvalidOperationException):-?[0-9]{1,11}", line)]


def main():
    parser = argparse.ArgumentParser(description="Record explicit emulator enrollment against private PostgreSQL and real Kestrel TLS.")
    parser.add_argument("--serial", required=True)
    parser.add_argument("--expected-avd", required=True)
    parser.add_argument("--with-mqtt", action="store_true", help="Also verify enrolled Keystore MQTT and test-host membership propagation")
    parser.add_argument("--bin-dir", type=Path, default=ROOT / ".lab-private/postgres-install/17.11/bin")
    args = parser.parse_args()
    if not re.fullmatch(r"emulator-[0-9]+", args.serial):
        parser.error("Only an explicitly named disposable emulator is supported")
    started = datetime.datetime.now(datetime.timezone.utc)
    stamp = started.strftime("%Y%m%dT%H%M%S.%fZ")
    output = ROOT / ("artifacts/android-backend-mqtt" if args.with_mqtt else "artifacts/android-backend") / stamp
    output.mkdir(parents=True)
    inputs = output / "inputs"
    hashes = {}
    sources = [*list((ROOT / "android").rglob("*")), *list((ROOT / "backend").rglob("*")),
               *list((ROOT / "tools/android-enrollment-host").rglob("*")),
               ROOT / "tools/check_android_backend.py", ROOT / "tools/test_android_backend.py",
               ROOT / "tools/check_android_enrollment.py", *(ROOT / name for name in COMMAND_CONTRACT_INPUTS)]
    if args.with_mqtt:
        sources.extend(ROOT / "tools" / name for name in ("mqtt_device_broker.py", "mqtt_device_policy.py",
            "mqtt_android_probe.py", "test_mqtt_device_probe.py", "mqtt-lab-requirements.txt"))
    for source in sorted(sources):
        relative = source.relative_to(ROOT)
        if not source.is_file() or {"bin", "obj", "build", ".gradle", ".kotlin"}.intersection(relative.parts):
            continue
        if source.name == "local.properties":
            continue
        destination = inputs / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        hashes[str(relative)] = hashlib.sha256(destination.read_bytes()).hexdigest()
    private = ROOT / ".lab-private/android-backend" / stamp
    private.mkdir(parents=True, mode=0o700)
    mqtt_directory = private / "mqtt"
    if args.with_mqtt:
        mqtt_directory.mkdir(mode=0o700)
    data = private / "data"
    server_log = private / "postgres.log"
    socket_directory = Path(tempfile.mkdtemp(prefix="fleet-pg-"))
    sdk = Path(os.environ.get("ANDROID_HOME", Path.home() / "Library/Android/sdk"))
    environment = dict(os.environ, ANDROID_HOME=str(sdk))
    adb = [str(sdk / "platform-tools/adb"), "-s", args.serial]
    records = []
    host = None
    broker = None
    passed = False
    stopped = False
    error = None
    jvm = None
    apks = {}
    staged = False
    host_stderr = ""

    def execute(arguments, cwd=inputs / "backend", timeout=600, input_text=None):
        process = subprocess.run(list(map(str, arguments)), cwd=cwd, env=environment, input=input_text,
                                 capture_output=True, text=True, timeout=timeout)
        entry = {"argv": list(map(str, arguments)), "returncode": process.returncode,
                 "stdout": process.stdout, "stderr": process.stderr}
        records.append(entry)
        (output / f"command-{len(records):02d}.json").write_text(json.dumps(entry, indent=2) + "\n")
        if process.returncode:
            raise RuntimeError("Recorded command failed")
        return process.stdout

    try:
        if execute([*adb, "emu", "avd", "name"], timeout=30).splitlines()[0].strip() != args.expected_avd:
            raise RuntimeError("Explicit AVD target mismatch")
        if execute([*adb, "shell", "getprop", "sys.boot_completed"], timeout=30).strip() != "1":
            raise RuntimeError("Android boot is incomplete")
        execute([*adb, "shell", "ls", "/sdcard"], timeout=30)
        execute([*adb, "shell", "getprop", "ro.build.fingerprint"], timeout=30)
        execute([*adb, "shell", "dpm", "list-owners"], timeout=30)
        execute(["dotnet", "--info"])
        project = inputs / "tools/android-enrollment-host/FleetManagement.AndroidEnrollmentHost.csproj"
        execute(["dotnet", "build", project, "--nologo", "--verbosity", "minimal"])
        execute(["dotnet", "list", project, "package", "--include-transitive"])
        android = inputs / "android"
        execute(["./gradlew", "--version"], cwd=android)
        execute(["./gradlew", ":agent:dependencies", "--configuration", "debugRuntimeClasspath", "--console=plain"], cwd=android)
        execute(["./gradlew", ":agent:testDebugUnitTest", ":agent:assembleDebug", ":agent:lintDebug",
                 ":agent:assembleDebugAndroidTest", "--console=plain"], cwd=android)
        jvm = {"tests": 0, "failures": 0, "errors": 0, "skipped": 0}
        results = android / "agent/build/test-results/testDebugUnitTest"
        for filename in results.glob("TEST-*.xml"):
            suite = ET.parse(filename).getroot()
            for name in jvm:
                jvm[name] += int(suite.get(name, "0"))
        if jvm["tests"] < 37 or any(jvm[name] for name in ("failures", "errors", "skipped")):
            raise RuntimeError("Missing or unsuccessful JVM tests")
        shutil.copytree(results, output / "jvm-results")
        shutil.copytree(android / "agent/build/reports", output / "reports")
        for relative in ("debug/agent-debug.apk", "androidTest/debug/agent-debug-androidTest.apk"):
            apk = android / "agent/build/outputs/apk" / relative
            apks[str(apk.relative_to(output))] = hashlib.sha256(apk.read_bytes()).hexdigest()
            execute([*adb, "install", "-r", "-t", apk], timeout=120)
        for executable in ("initdb", "pg_ctl", "psql", "postgres"):
            if not (args.bin_dir / executable).is_file():
                raise RuntimeError("Private PostgreSQL installation is required")
        execute([args.bin_dir / "postgres", "--version"])
        execute([args.bin_dir / "initdb", "-D", data, "--username=fleet_lab", "--encoding=UTF8",
                 "--locale=C", "--auth-local=trust", "--auth-host=reject"])
        execute([args.bin_dir / "pg_ctl", "-D", data, "-l", server_log, "-w", "-t", "60", "-o",
                 f"-c listen_addresses='' -c unix_socket_directories='{socket_directory}' -c unix_socket_permissions=0700", "start"])
        psql = [args.bin_dir / "psql", "-h", socket_directory, "-U", "fleet_lab", "-d", "postgres", "-X", "-v", "ON_ERROR_STOP=1"]
        execute([*psql, "-c", "SHOW listen_addresses; SHOW unix_socket_permissions; SHOW fsync; SHOW synchronous_commit;"])
        host_environment = dict(environment, FLEET_TEST_POSTGRES=
            f"Host={socket_directory};Database=postgres;Username=fleet_lab;Timeout=10;Command Timeout=30")
        if args.with_mqtt:
            host_environment["FLEET_TEST_MQTT_DIRECTORY"] = str(mqtt_directory)
        executable = inputs / "tools/android-enrollment-host/bin/Debug/net10.0/FleetManagement.AndroidEnrollmentHost.dll"
        host = subprocess.Popen(["dotnet", str(executable)], cwd=inputs / "backend", env=host_environment,
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        with selectors.DefaultSelector() as selector:
            selector.register(host.stdout, selectors.EVENT_READ)
            if not selector.select(90):
                raise RuntimeError("Integration host did not become ready")
            configuration = json.loads(host.stdout.readline())
        if configuration.get("stage") != "ready":
            raise RuntimeError("Integration host readiness was not explicit")
        configuration["namespace"] = "integration-" + str(uuid.uuid4())
        if args.with_mqtt:
            configuration["mqttProbe"] = "probe-" + str(uuid.uuid4())
            broker = subprocess.Popen([sys.executable, str(inputs / "tools/mqtt_android_probe.py")], cwd=inputs,
                                      stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            broker.stdin.write(json.dumps({"directory": str(mqtt_directory), "probe": configuration["mqttProbe"]}) + "\n")
            broker.stdin.flush()
            with selectors.DefaultSelector() as selector:
                selector.register(broker.stdout, selectors.EVENT_READ)
                if not selector.select(30):
                    raise RuntimeError("MQTT probe did not become ready")
                readiness = json.loads(broker.stdout.readline())
            if readiness.get("stage") != "mqtt-ready" or type(readiness.get("port")) is not int or not 1 <= readiness["port"] <= 65535:
                raise RuntimeError("MQTT readiness was not explicit")
            configuration["mqttPort"] = readiness["port"]
        (output / "public-configuration.json").write_text(json.dumps(public_configuration(configuration), indent=2) + "\n")
        for filename in ("enrollment-integration-input.json", "enrollment-integration-result.json"):
            existing = subprocess.run([*adb, "shell", "run-as", "dev.fleet.agent", "test", "-e", "files/" + filename],
                                      capture_output=True, text=True, timeout=30)
            if existing.returncode != 1:
                raise RuntimeError("Integration staging files already exist or app-private access failed")
        staged = True
        execute([*adb, "shell", "run-as", "dev.fleet.agent", "sh", "-c",
                 "'umask 077; mkdir -p files; cat > files/enrollment-integration-input.json'"], timeout=30,
                input_text=json.dumps(configuration))
        del configuration
        result = execute([*adb, "shell", "am", "instrument", "-w", "-r", "-e", "class",
                          "dev.fleet.agent.EnrollmentBackendInstrumentedTest",
                          "dev.fleet.agent.test/androidx.test.runner.AndroidJUnitRunner"], timeout=120)
        if not instrumentation_passed(result):
            raise RuntimeError("Real-backend instrumentation did not explicitly pass")
        proof = execute([*adb, "exec-out", "run-as", "dev.fleet.agent", "cat", "files/enrollment-integration-result.json"], timeout=30)
        json.loads(proof)
        (output / "device-public-proof.json").write_text(proof + "\n")
        stdout, stderr = host.communicate(input=proof + "\n", timeout=60)
        host_stderr = stderr
        if host.returncode or any(line not in public_tls_diagnostics(stderr) for line in stderr.splitlines()):
            raise RuntimeError("Integration host verification failed")
        verification = json.loads(stdout)
        if verification.get("stage") != "verified" or verification.get("issuedCertificates") != 1 or not verification.get("publicKeyProof"):
            raise RuntimeError("Backend proof was not explicit")
        if not all(verification.get(name) is True for name in ("mtlsAuthenticated", "disabledRejected",
                   "alternateCertificateRejected", "forwardedCertificateRejected")):
            raise RuntimeError("mTLS access-control proof was not explicit")
        (output / "backend-verification.json").write_text(json.dumps(verification, indent=2) + "\n")
        if args.with_mqtt:
            broker_stdout, broker_stderr = broker.communicate(input="stop\n", timeout=30)
            if broker.returncode or broker_stderr:
                raise RuntimeError("MQTT broker verification failed")
            broker_verification = json.loads(broker_stdout)
            verify_mqtt_proof(json.loads(proof), broker_verification)
            (output / "mqtt-verification.json").write_text(json.dumps(broker_verification, indent=2) + "\n")
        remaining = execute([*psql, "--tuples-only", "--no-align", "-c",
                             "SELECT count(*) FROM pg_namespace WHERE nspname ~ '^fleet_test_[0-9a-f]{32}$';"])
        if remaining.strip() != "0":
            raise RuntimeError("Integration schema was not cleaned up")
        passed = True
    except Exception:
        error = "Integration check failed; inspect recorded public command results. Private host/staging inputs are not retained."
    finally:
        if host is not None and host.poll() is None:
            try:
                _, host_stderr = host.communicate(timeout=20)
            except subprocess.TimeoutExpired:
                host.kill()
                _, host_stderr = host.communicate()
                passed = False
        if broker is not None and broker.poll() is None:
            try:
                broker.communicate(input="stop\n", timeout=20)
            except subprocess.TimeoutExpired:
                broker.kill()
                broker.communicate()
                passed = False
        if args.with_mqtt and (broker is None or broker.poll() is not None) and (host is None or host.poll() is not None):
            try:
                shutil.rmtree(mqtt_directory)
            except OSError:
                passed = False
        if staged:
            try:
                execute([*adb, "shell", "run-as", "dev.fleet.agent", "rm", "-f",
                         "files/enrollment-integration-input.json", "files/enrollment-integration-result.json"], timeout=30)
            except Exception:
                passed = False
        if (data / "postmaster.pid").exists():
            try:
                execute([args.bin_dir / "pg_ctl", "-D", data, "-w", "-t", "60", "-m", "fast", "stop"])
            except Exception:
                passed = False
        stopped = not (data / "postmaster.pid").exists()
        if stopped:
            shutil.rmtree(socket_directory)
    (output / "tls-diagnostics.json").write_text(json.dumps(public_tls_diagnostics(host_stderr), indent=2) + "\n")
    manifest = {"schemaVersion": 1, "startedAt": started.isoformat(),
                "finishedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "result": "passed" if passed and stopped else "failed", "error": error,
                "serial": args.serial, "expectedAvd": args.expected_avd, "serverStopped": stopped,
                "inputSha256": hashes, "apkSha256": apks, "jvmCounters": jvm,
                "commands": records,
                "mqttRequested": args.with_mqtt,
                "mqttStopped": broker is not None and broker.poll() is not None if args.with_mqtt else None,
                "pythonDependencies": dict(sorted((distribution.metadata["Name"], distribution.version)
                    for distribution in importlib.metadata.distributions())) if args.with_mqtt else {},
                "scope": ("Real emulator enrolled Keystore MQTT mTLS, roundtrip, device-topic/wildcard denial and committed database disable propagated by a test-only local-file bridge, alongside HTTPS enrollment/identity and PostgreSQL audit/completion. No production membership service, forced session termination, durable event processing, UI, live IdP, reboot, power loss or production provisioning proof." if args.with_mqtt else
                    "Real emulator Keystore enrollment and HTTPS mTLS identity request, authorized admin API provisioning, PostgreSQL audit/completion/access-disable and lab signer; exact response retry, invalid token/conflicting CSR/alternate certificate/header spoof rejection. No UI workflow, live IdP, MQTT mTLS, reboot, power loss or production provisioning proof."),
                "fileSha256": {str(filename.relative_to(output)): hashlib.sha256(filename.read_bytes()).hexdigest()
                               for filename in sorted(output.rglob("*")) if filename.is_file()
                               and "inputs" not in filename.relative_to(output).parts}}
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    (output / "replay.html").write_text("<!doctype html><meta charset='utf-8'><title>Android/backend enrollment</title>"
        f"<h1>Android/backend enrollment</h1><p>{manifest['result']}</p><p>{html.escape(manifest['scope'])}</p>"
        "<p><a href='manifest.json'>Manifest, commands and hashes</a></p>"
        f"<pre>{html.escape(error or '')}</pre>")
    print(f"{manifest['result'].upper()}: {output / 'replay.html'}")
    return 0 if manifest["result"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())