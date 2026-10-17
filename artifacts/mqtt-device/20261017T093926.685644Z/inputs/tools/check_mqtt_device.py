import argparse
import datetime
import hashlib
import html
import importlib.metadata
import json
import logging
from pathlib import Path
import shutil
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
SOURCES = (
    "tools/mqtt_device_policy.py", "tools/mqtt_device_broker.py", "tools/mqtt-lab-requirements.txt",
    "tools/test_mqtt_device_policy.py", "tools/test_mqtt_device_broker.py", "tools/test_mqtt_device_wire.py",
    "tools/check_mqtt_device.py",
    "tools/mqtt_android_probe.py", "tools/test_mqtt_device_probe.py",
)


def worker(filename):
    logging.basicConfig(level=logging.CRITICAL)
    suite = unittest.defaultTestLoader.discover("tools", pattern="test_mqtt_device*.py")
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    counters = {"tests": result.testsRun, "failures": len(result.failures), "errors": len(result.errors),
                "skipped": len(result.skipped), "unexpectedSuccesses": len(result.unexpectedSuccesses),
                "expectedFailures": len(result.expectedFailures)}
    filename.write_text(json.dumps(counters, indent=2) + "\n")
    return 0 if result.wasSuccessful() and result.testsRun >= 14 and not result.skipped and not result.expectedFailures else 1


def main():
    parser = argparse.ArgumentParser(description="Snapshot and verify isolated real MQTT mTLS/ACL lab")
    parser.add_argument("--worker-result", type=Path, help=argparse.SUPPRESS)
    arguments = parser.parse_args()
    if arguments.worker_result is not None:
        return worker(arguments.worker_result)
    started = datetime.datetime.now(datetime.timezone.utc)
    output = ROOT / "artifacts/mqtt-device" / started.strftime("%Y%m%dT%H%M%S.%fZ")
    output.mkdir(parents=True, exist_ok=False)
    inputs = {}
    for relative in SOURCES:
        destination = output / "inputs" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, destination)
        inputs[relative] = hashlib.sha256(destination.read_bytes()).hexdigest()
    command = [sys.executable, "tools/check_mqtt_device.py", "--worker-result", str(output / "counters.json")]
    try:
        process = subprocess.run(command, cwd=output / "inputs", capture_output=True, text=True, timeout=120)
        returncode, stdout, stderr = process.returncode, process.stdout, process.stderr
    except subprocess.TimeoutExpired as error:
        returncode = 124
        stdout = (error.stdout or b"").decode(errors="replace")
        stderr = (error.stderr or b"").decode(errors="replace") + "\nMQTT lab timed out after 120 seconds"
    counters_file = output / "counters.json"
    counters = json.loads(counters_file.read_text()) if counters_file.exists() else None
    passed = (returncode == 0 and counters is not None and counters["tests"] >= 14
              and all(value == 0 for key, value in counters.items() if key != "tests"))
    (output / "command.json").write_text(json.dumps({"argv": command, "returncode": returncode,
                                                   "stdout": stdout, "stderr": stderr}, indent=2) + "\n")
    report = {
        "schemaVersion": 1, "startedAt": started.isoformat(),
        "finishedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "result": "passed" if passed else "failed", "testCounters": counters,
        "inputSha256": inputs, "pythonVersion": sys.version,
        "dependencies": dict(sorted((distribution.metadata["Name"], distribution.version)
                                    for distribution in importlib.metadata.distributions())),
        "resultSha256": {filename.name: hashlib.sha256(filename.read_bytes()).hexdigest()
                         for filename in (output / "command.json", counters_file) if filename.exists()},
        "scope": "Real loopback MQTT TLS with ephemeral software keys, explicit lab certificate membership and directional ACLs; no Android Keystore, enrollment/PostgreSQL membership, session termination, payload validation, durable delivery or production infrastructure",
    }
    (output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n")
    (output / "replay.html").write_text(
        "<!doctype html><html lang='en'><meta charset='utf-8'><title>Device MQTT lab</title>"
        "<h1>Device MQTT mTLS and topic authorization</h1>"
        f"<p>{html.escape(report['result'])}</p><p>{html.escape(report['scope'])}</p>"
        "<p><a href='manifest.json'>Exact inputs, dependencies, counters and result hashes</a></p>"
        f"<pre>{html.escape(stdout + stderr)}</pre></html>"
    )
    print(f"{report['result'].upper()}: {output / 'replay.html'}")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())