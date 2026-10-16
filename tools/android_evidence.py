#!/usr/bin/env python3
import argparse
import datetime
import hashlib
import html
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
AGENT = "dev.fleet.agent"
SAMPLE = "dev.fleet.sample"


def button_center(xml, label):
    for node in ET.fromstring(xml).iter("node"):
        if node.get("text", "").casefold() == label.casefold():
            if node.get("enabled") != "true" or node.get("clickable") != "true":
                raise ValueError(f"Control is not enabled and clickable: {label}")
            coordinates = re.fullmatch(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", node.get("bounds", ""))
            if not coordinates:
                raise ValueError(f"Invalid control bounds: {label}")
            left, top, right, bottom = map(int, coordinates.groups())
            return (left + right) // 2, (top + bottom) // 2
    raise ValueError(f"Control not found: {label}")


def lock_state(activity):
    match = re.search(r"mLockTaskModeState=(\w+)", activity)
    return match.group(1) if match else None


def resumed_package(activity):
    match = re.search(r"topResumedActivity=.*?\bu\d+ ([\w.]+)/", activity)
    return match.group(1) if match else None


def effective_restrictions(state):
    user = re.search(r"UserInfo\{0:.*?(?=\n  UserInfo\{|\nDevice properties:|\Z)", state, re.S)
    effective = re.search(r"Effective restrictions:\s*\n((?:[ \t]{6,}[^\n]*\n)*)", user.group() if user else "")
    if effective is None:
        raise AssertionError("Missing user 0 effective restriction section")
    return set(re.findall(r"^\s+(no_[a-z_]+)(?:=true)?\s*$", effective.group(1), re.M))


class EvidenceRun:
    def __init__(self, adb, serial, output):
        self.adb = str(adb)
        self.serial = serial
        self.output = output
        self.output.mkdir(parents=True, exist_ok=False)
        self.commands = []
        self.steps = []
        self.result = "running"
        self.error = None
        self.video_process = None
        self.video_pid = None
        self.videos = []
        self.video_name = None
        self.video_remote = "/sdcard/fleet-runtime-evidence.mp4"
        self.started = datetime.datetime.now(datetime.timezone.utc).isoformat()

    def command(self, arguments, timeout=90, binary=False, check=True, input_data=None, extra_environment=None):
        started = time.monotonic()
        environment = os.environ.copy()
        environment.setdefault("ANDROID_HOME", str(Path(self.adb).resolve().parents[1]))
        environment.update(extra_environment or {})
        try:
            process = subprocess.run(arguments, capture_output=True, input=input_data, timeout=timeout,
                                     cwd=ROOT, env=environment)
        except subprocess.TimeoutExpired as exception:
            process = subprocess.CompletedProcess(arguments, 124, exception.stdout or b"",
                                                  (exception.stderr or b"") + f"\nTimed out after {timeout} seconds".encode())
        entry = {"argv": list(map(str, arguments)), "returncode": process.returncode,
                 "seconds": round(time.monotonic() - started, 3),
                 "stderr": process.stderr.decode(errors="replace")}
        if binary:
            entry["stdoutSha256"] = hashlib.sha256(process.stdout).hexdigest()
        else:
            entry["stdout"] = process.stdout.decode(errors="replace")
        self.commands.append(entry)
        self.output.joinpath(f"command-{len(self.commands):03d}.log").write_text(
            json.dumps(entry, indent=2) + "\n")
        self.save()
        if check and process.returncode:
            raise RuntimeError(f"Command failed ({process.returncode}): {' '.join(map(str, arguments))}\n{entry['stderr']}\n{entry.get('stdout', '')}")
        return process.stdout if binary else process.stdout.decode(errors="replace")

    def device(self, *arguments, **kwargs):
        return self.command([self.adb, "-s", self.serial, *arguments], **kwargs)

    def activity(self):
        return self.device("shell", "dumpsys", "activity", "activities")

    def wait_activity(self, state, package, timeout=90):
        deadline = time.monotonic() + timeout
        while True:
            activity = self.activity()
            if lock_state(activity) == state and resumed_package(activity) == package:
                return activity
            if time.monotonic() >= deadline:
                raise AssertionError(f"Expected {state}/{package}; observed {lock_state(activity)}/{resumed_package(activity)}")
            time.sleep(0.4)

    def wait_kiosk_exit(self, timeout=90):
        deadline = time.monotonic() + timeout
        while True:
            activity = self.activity()
            if lock_state(activity) == "NONE" and resumed_package(activity) is not None:
                package = resumed_package(activity)
                if package not in (AGENT, "com.android.launcher3"):
                    raise AssertionError(f"Unexpected unlocked foreground package: {package}")
                if package != AGENT:
                    self.device("shell", "am", "start", "-W", "-n", AGENT + "/.MainActivity")
                    self.wait_activity("NONE", AGENT)
                return
            if time.monotonic() >= deadline:
                raise AssertionError("Kiosk exit did not clear lock task mode")
            time.sleep(0.4)

    def ui(self):
        self.device("shell", "uiautomator", "dump", "/sdcard/fleet-evidence-window.xml", timeout=90)
        return self.device("exec-out", "cat", "/sdcard/fleet-evidence-window.xml")

    def click(self, label):
        horizontal, vertical = button_center(self.ui(), label)
        self.device("shell", "input", "tap", str(horizontal), str(vertical))

    def pair_operator(self):
        directory = ROOT / ".lab-private" / self.serial
        directory.mkdir(parents=True, exist_ok=True, mode=0o700)
        private = directory / "operator.pem"
        public = directory / "operator-public.der"
        if not private.exists():
            self.command(["openssl", "genpkey", "-algorithm", "EC", "-pkeyopt",
                          "ec_paramgen_curve:P-256", "-out", str(private)])
            private.chmod(0o600)
        self.command(["openssl", "pkey", "-in", str(private), "-pubout", "-outform", "DER", "-out", str(public)])
        present = self.device("shell", "run-as", AGENT, "sh", "-c",
                      "'test -f files/maintenance-public.der && echo present'", check=False).strip()
        existing = (self.device("exec-out", "run-as", AGENT, "cat", "files/maintenance-public.der",
                    binary=True) if present == "present" else b"")
        if existing and existing != public.read_bytes():
            raise AssertionError("Paired operator key differs; refusing automatic key replacement")
        if not existing:
            if lock_state(self.activity()) != "NONE":
                raise AssertionError("Pair the operator key before entering kiosk")
            self.device("shell", "run-as", AGENT, "mkdir", "-p", "files")
            self.device("shell", "-T", "run-as", AGENT, "tee", "files/maintenance-public.der",
                        binary=True, input_data=public.read_bytes())

    def maintenance_exit(self):
        self.pair_operator()
        self.click("Request maintenance challenge")
        self.wait_text("Maintenance challenge ready")
        challenge = self.device("exec-out", "run-as", AGENT, "cat", "files/maintenance-challenge.txt", binary=True)
        directory = ROOT / ".lab-private" / self.serial
        signature = self.command(["openssl", "dgst", "-sha256", "-sign", str(directory / "operator.pem")],
                                 binary=True, input_data=challenge)
        self.device("shell", "-T", "run-as", AGENT, "tee", "files/maintenance-response.sig",
                    binary=True, input_data=signature)
        self.click("Exit lab kiosk")
        self.wait_kiosk_exit()
        self.wait_text("Maintenance exit authenticated.")

    def assert_restrictions(self, enabled):
        state = effective_restrictions(self.device("shell", "dumpsys", "user"))
        for name in ("no_config_wifi", "no_config_mobile_networks", "no_control_apps"):
            if (name in state) != enabled:
                raise AssertionError(f"Restriction {name} did not match enabled={enabled}")

    def phase_one(self):
        self.command([sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_android_evidence.py", "-v"])
        self.command([str(ROOT / "android/gradlew"), "-p", str(ROOT / "android"), ":agent:testDebugUnitTest",
                      ":agent:assembleDebug", ":agent:lintDebug", "--console", "plain"], timeout=600)
        artifact = self.output / "agent-debug.apk"
        shutil.copy2(ROOT / "android/agent/build/outputs/apk/debug/agent-debug.apk", artifact)
        self.device("install", "-r", "-t", str(artifact), timeout=180)
        self.pair_operator()
        self.device("shell", "am", "start", "-W", "-n", AGENT + "/.MainActivity")
        self.wait_activity("NONE", AGENT)
        self.start_video("phase-one-maintenance")
        self.click("Start sample kiosk")
        self.wait_activity("LOCKED", SAMPLE)
        self.assert_restrictions(True)
        self.checkpoint("settings-restrictions-applied")
        self.click("Lab management")
        self.wait_activity("LOCKED", AGENT)
        self.click("Exit lab kiosk")
        self.wait_text("Maintenance denied:")
        self.wait_activity("LOCKED", AGENT)
        self.checkpoint("unsigned-exit-denied")
        self.click("Request maintenance challenge")
        self.wait_text("Maintenance challenge ready")
        self.device("shell", "-T", "run-as", AGENT, "tee", "files/maintenance-response.sig",
                    binary=True, input_data=b"invalid-signature")
        self.click("Exit lab kiosk")
        self.wait_text("Maintenance denied:")
        self.wait_activity("LOCKED", AGENT)
        self.checkpoint("tampered-signature-denied")
        self.maintenance_exit()
        self.assert_restrictions(False)
        self.checkpoint("signed-exit-restores-restrictions")
        self.click("Exit lab kiosk")
        self.wait_text("Maintenance denied:")
        self.checkpoint("consumed-challenge-denied")

    def mqtt_probe(self):
        response = json.loads(self.command([str(ROOT / ".lab-private/mqtt-venv/bin/python"),
                                           str(ROOT / "tools/mqtt_lab.py"), "probe"], timeout=45))
        if response.get("result") != "passed" or not response.get("acknowledgedOverTls"):
            raise AssertionError("MQTT probe did not receive a TLS acknowledgement")
        state = self.device("exec-out", "run-as", AGENT, "cat", "shared_prefs/lab-mqtt.xml")
        observed = ET.fromstring(state).find("string[@name='lastProbe']")
        if observed is None or observed.text != response["probe"]:
            raise AssertionError("Agent did not persist the acknowledged probe")
        self.output.joinpath(f"mqtt-receipt-{len(self.steps):02d}.xml").write_text(state)

    def wait_mqtt_status(self, expected, timeout=90):
        deadline = time.monotonic() + timeout
        while True:
            state = self.device("exec-out", "run-as", AGENT, "cat", "shared_prefs/lab-mqtt.xml")
            status = ET.fromstring(state).find("string[@name='status']")
            if status is not None and expected in (status.text or ""):
                return
            if time.monotonic() >= deadline:
                raise AssertionError(f"MQTT status did not reach: {expected}")
            time.sleep(0.5)

    def start_lab_broker(self, segment):
        log = self.output.joinpath(f"mqtt-broker-{segment}.log").open("w")
        process = subprocess.Popen([str(ROOT / ".lab-private/mqtt-venv/bin/python"),
                                    str(ROOT / "tools/mqtt_lab.py"), "serve"], cwd=ROOT,
                                   stdout=log, stderr=subprocess.STDOUT)
        log.close()
        deadline = time.monotonic() + 30
        while True:
            try:
                with socket.create_connection(("127.0.0.1", 8883), timeout=1):
                    return process
            except OSError:
                if process.poll() is not None or time.monotonic() >= deadline:
                    process.terminate()
                    process.wait(timeout=10)
                    raise RuntimeError("Owned lab broker did not start; check broker log")
                time.sleep(0.2)

    def mqtt_lifecycle(self):
        self.lab_broker = self.start_lab_broker("initial")
        try:
            self.mqtt_suite(lifecycle=True)
        finally:
            if self.lab_broker.poll() is None:
                self.lab_broker.terminate()
                self.lab_broker.wait(timeout=15)

    def mqtt_suite(self, lifecycle=False):
        certificate = ROOT / ".lab-private/mqtt/server.pem"
        if not certificate.is_file():
            raise RuntimeError("Prepare and start the loopback MQTT lab first")
        self.pair_operator()
        self.device("reverse", "tcp:8883", "tcp:8883")
        self.device("shell", "-T", "run-as", AGENT, "tee", "files/mqtt-ca.pem",
                    binary=True, input_data=certificate.read_bytes())
        self.device("shell", "pm", "grant", AGENT, "android.permission.POST_NOTIFICATIONS")
        self.device("shell", "am", "start", "-W", "-n", AGENT + "/.MainActivity")
        self.wait_activity("NONE", AGENT)
        self.start_video("mqtt-lifecycle")
        self.click("Start MQTT probe")
        self.mqtt_probe()
        self.checkpoint("mqtt-tls-round-trip")
        self.click("Start sample kiosk")
        self.wait_activity("LOCKED", SAMPLE)
        self.mqtt_probe()
        self.checkpoint("mqtt-received-while-sample-foreground")
        services = self.device("shell", "dumpsys", "activity", "services", AGENT)
        if "isForeground=true" not in services or "LabMqttService" not in services:
            raise AssertionError("MQTT probe is not running as a visible foreground service")
        if lifecycle:
            self.lab_broker.terminate()
            self.lab_broker.wait(timeout=15)
            self.wait_mqtt_status("disconnected")
            self.checkpoint("broker-offline-sample-still-locked")
            self.lab_broker = self.start_lab_broker("restarted")
            self.wait_mqtt_status("connected over lab TLS")
            self.mqtt_probe()
            self.wait_activity("LOCKED", SAMPLE)
            self.checkpoint("broker-reconnected-new-probe-acknowledged")
            process_id = self.device("shell", "pidof", AGENT).strip()
            if not process_id.isdigit():
                raise AssertionError("Expected one agent process before process-death check")
            self.device("shell", "run-as", AGENT, "kill", "-9", process_id)
            deadline = time.monotonic() + 10
            while time.monotonic() < deadline:
                if self.device("shell", "pidof", AGENT, check=False).strip():
                    raise AssertionError("Probe unexpectedly restarted without an explicit user action")
                time.sleep(1)
            self.wait_activity("LOCKED", SAMPLE)
            self.checkpoint("process-death-sample-contained-no-immortal-socket")
        self.click("Lab management")
        self.wait_activity("LOCKED", AGENT)
        if lifecycle:
            self.click("Start MQTT probe")
            self.mqtt_probe()
            self.checkpoint("explicit-probe-restart-after-process-death")
        self.maintenance_exit()
        self.click("Stop MQTT probe")
        self.checkpoint("mqtt-stopped-explicitly")

    def wait_text(self, expected, timeout=90):
        deadline = time.monotonic() + timeout
        while True:
            hierarchy = ET.fromstring(self.ui())
            if any(expected in node.get("text", "") for node in hierarchy.iter("node")):
                return
            if time.monotonic() >= deadline:
                raise AssertionError(f"UI did not render expected text: {expected}")
            time.sleep(0.4)

    def checkpoint(self, name):
        slug = f"{len(self.steps) + 1:02d}-{name}"
        screenshot = slug + ".png"
        xml = self.ui()
        self.output.joinpath(screenshot).write_bytes(self.device("exec-out", "screencap", "-p", binary=True))
        self.output.joinpath(slug + "-activity.txt").write_text(self.activity())
        self.output.joinpath(slug + "-policy.txt").write_text(self.device("shell", "dumpsys", "device_policy"))
        self.output.joinpath(slug + "-ui.xml").write_text(xml)
        self.steps.append({"name": name, "screenshot": screenshot, "prefix": slug,
                           "recordedAt": datetime.datetime.now(datetime.timezone.utc).isoformat()})
        self.save()
        print(f"PASS checkpoint: {name}", flush=True)

    def start_video(self, name="smoke"):
        if self.device("shell", "pidof", "screenrecord", check=False).strip():
            raise RuntimeError("An existing screen recording is active; refusing to interrupt it")
        arguments = [self.adb, "-s", self.serial, "shell", "screenrecord", "--time-limit", "0",
                     "--size", "540x1200", self.video_remote]
        self.video_log = self.output.joinpath(name + "-screenrecord.log").open("wb")
        self.video_name = name + ".mp4"
        self.video_process = subprocess.Popen(arguments, stdout=self.video_log, stderr=subprocess.STDOUT)
        self.commands.append({"argv": arguments, "kind": "video-start", "maximumSeconds": None})
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            recorder = self.device("shell", "pidof", "screenrecord", check=False).strip()
            if recorder:
                if not re.fullmatch(r"\d+", recorder):
                    raise RuntimeError("Ambiguous screen recorder PID")
                self.video_pid = recorder
                return
            if self.video_process.poll() is not None:
                raise RuntimeError("Screen recording failed; see screenrecord.log")
            time.sleep(0.2)
        raise RuntimeError("Screen recorder did not start")

    def stop_video(self):
        if self.video_process is None:
            return
        try:
            if self.video_pid and self.video_process.poll() is None:
                self.device("shell", "kill", "-2", self.video_pid, check=False)
            self.video_process.wait(timeout=20)
            self.video_log.close()
            self.device("pull", self.video_remote, str(self.output / self.video_name))
            self.videos.append(self.video_name)
        finally:
            if self.video_process.poll() is None:
                self.video_process.terminate()
                self.video_process.wait(timeout=10)
            self.video_log.close()
            self.video_process = None
            self.video_pid = None

    def save(self):
        manifest = {"schemaVersion": 1, "startedAt": self.started, "serial": self.serial,
                    "result": self.result, "error": self.error, "steps": self.steps,
                    "commands": self.commands,
                    "videos": self.videos,
                    "captureNote": "New recorded rerun, not screenshots recovered from the original manual test."}
        self.output.joinpath("manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        slides = "".join(f'<figure><img src="{step["screenshot"]}" alt="{html.escape(step["name"])}"><figcaption>{html.escape(step["name"])} | <a href="{step["prefix"]}-activity.txt">activity</a> | <a href="{step["prefix"]}-policy.txt">policy</a> | <a href="{step["prefix"]}-ui.xml">UI</a></figcaption></figure>' for step in self.steps)
        videos = "".join(f'<p><a href="{video}">{html.escape(video)}</a></p><video controls preload="metadata" src="{video}"></video>' for video in self.videos)
        self.output.joinpath("replay.html").write_text(f'''<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Fleet Android test replay</title>
<style>body{{font:16px Georgia,serif;max-width:1100px;margin:24px auto;padding:0 16px;background:#f4f6f7;color:#182322}}video{{width:270px;max-width:100%}}main{{display:flex;flex-wrap:wrap;gap:20px}}figure{{margin:0;width:260px}}img{{width:100%;height:auto}}figcaption{{padding:8px 0}}a{{color:#006655}}</style>
<h1>Fleet Android test replay</h1><p>{html.escape(self.started)} | {html.escape(self.serial)} | <strong>{self.result}</strong></p>
<p>New recorded rerun of the lab checks. <a href="manifest.json">Assertions and command transcript</a></p>
<p>{html.escape(self.error or "")}</p>{videos}<main>{slides}</main></html>''')

    def installed_version(self):
        details = self.device("shell", "dumpsys", "package", SAMPLE)
        match = re.search(r"\bversionCode=(\d+)", details)
        return int(match.group(1)) if match else None

    def recover(self):
        if not self.serial.startswith("emulator-"):
            raise RuntimeError("Recovery is restricted to explicitly selected lab emulators")
        policy = self.device("shell", "dumpsys", "device_policy")
        if not re.search(r"Device Owner:\s+admin=ComponentInfo\{dev\.fleet\.agent/", policy):
            raise RuntimeError("The lab agent must be Device Owner before recovery")
        self.start_video("recovery")
        activity = self.activity()
        if lock_state(activity) == "LOCKED":
            if resumed_package(activity) == SAMPLE:
                self.click("Lab management")
                self.wait_activity("LOCKED", AGENT)
            elif resumed_package(activity) != AGENT:
                raise RuntimeError("Unexpected locked activity; refusing automated recovery")
            self.checkpoint("before-lab-recovery")
            self.maintenance_exit()
        elif lock_state(activity) != "NONE":
            raise RuntimeError("Unexpected lock task state")
        self.checkpoint("lab-recovery-complete")

    def stage(self, artifact):
        self.device("shell", "run-as", AGENT, "mkdir", "-p", "files")
        self.device("shell", "-T", "run-as", AGENT, "tee", "files/sample-update.apk",
                    binary=True, input_data=artifact.read_bytes())

    def wait_update(self, expected, timeout=120):
        deadline = time.monotonic() + timeout
        while True:
            state = self.device("exec-out", "run-as", AGENT, "cat", "shared_prefs/lab-update.xml", check=False)
            if state:
                status = ET.fromstring(state).find("string[@name='status']")
                text = status.text if status is not None else ""
                if expected in text:
                    self.output.joinpath(f"update-state-{len(self.steps) + 1:02d}.xml").write_text(state)
                    return
                if "failed" in text.casefold() or "requires user confirmation" in text.casefold():
                    raise AssertionError(f"Unexpected update result: {text}")
            if time.monotonic() >= deadline:
                raise AssertionError(f"Timed out waiting for installer result: {expected}")
            time.sleep(0.4)

    def negative_apks(self, candidate):
        signer = Path(self.adb).resolve().parents[1] / "build-tools/34.0.0/apksigner"
        wrong_signer = self.output / "sample-v2-wrong-signer.apk"
        invalid = self.output / "sample-invalid.apk"
        with tempfile.TemporaryDirectory(prefix="fleet-negative-signing-") as directory:
            keystore = str(Path(directory) / "negative.p12")
            self.command(["keytool", "-genkeypair", "-keystore", keystore, "-storetype", "PKCS12",
                          "-storepass", "android", "-keypass", "android", "-alias", "negative-lab",
                          "-keyalg", "RSA", "-keysize", "2048", "-validity", "2",
                          "-dname", "CN=Fleet Negative Test Only", "-noprompt"])
            self.command([str(signer), "sign", "--ks", keystore, "--ks-key-alias", "negative-lab",
                          "--ks-pass", "pass:android", "--out", str(wrong_signer), str(candidate)])
        certificates = []
        for artifact in (candidate, wrong_signer):
            verification = self.command([str(signer), "verify", "--print-certs", str(artifact)])
            certificate = re.search(r"Signer #1 certificate SHA-256 digest: ([0-9a-f]+)", verification)
            if not certificate:
                raise AssertionError("APK verification did not report a signer certificate")
            certificates.append(certificate.group(1))
        if certificates[0] == certificates[1]:
            raise AssertionError("Negative fixture must have a different APK signer")
        invalid.write_bytes(b"Fleet lab invalid APK fixture\n")
        return wrong_signer, invalid

    def prepare_apks(self, prepared_run=None):
        if prepared_run is not None:
            source = prepared_run.resolve()
            hashes = json.loads((source / "apk-sha256.json").read_text())
            artifacts = []
            for name in ("agent-debug.apk", "sample-v1.apk", "sample-v2.apk",
                         "sample-v2-wrong-signer.apk", "sample-invalid.apk"):
                content = (source / name).read_bytes()
                if hashlib.sha256(content).hexdigest() != hashes.get(name):
                    raise AssertionError(f"Prepared APK hash mismatch: {name}")
                artifact = self.output / name
                artifact.write_bytes(content)
                artifacts.append(artifact)
            self.output.joinpath("apk-sha256.json").write_text(json.dumps(
                {artifact.name: hashes[artifact.name] for artifact in artifacts}, indent=2) + "\n")
            self.output.joinpath("prepared-source.json").write_text(json.dumps(
                {"source": str(source), "buildAndLintRerun": False}, indent=2) + "\n")
            return tuple(artifacts)
        gradle = [str(ROOT / "android/gradlew"), "-p", str(ROOT / "android"), "--console", "plain"]
        self.command(gradle + [":agent:assembleDebug", ":agent:lintDebug", ":sample-managed-app:assembleDebug",
                               ":sample-managed-app:lintDebug", "-PsampleVersionCode=1"], timeout=600)
        agent = self.output / "agent-debug.apk"
        baseline = self.output / "sample-v1.apk"
        candidate = self.output / "sample-v2.apk"
        shutil.copy2(ROOT / "android/agent/build/outputs/apk/debug/agent-debug.apk", agent)
        shutil.copy2(ROOT / "android/sample-managed-app/build/outputs/apk/debug/sample-managed-app-debug.apk", baseline)
        self.command(gradle + [":sample-managed-app:assembleDebug", ":sample-managed-app:lintDebug", "-PsampleVersionCode=2"], timeout=600)
        shutil.copy2(ROOT / "android/sample-managed-app/build/outputs/apk/debug/sample-managed-app-debug.apk", candidate)
        wrong_signer, invalid = self.negative_apks(candidate)
        self.output.joinpath("apk-sha256.json").write_text(json.dumps({artifact.name: hashlib.sha256(artifact.read_bytes()).hexdigest()
                                         for artifact in (agent, baseline, candidate, wrong_signer, invalid)}, indent=2) + "\n")
        return agent, baseline, candidate, wrong_signer, invalid

    def update(self, reset_sample, prepared_run=None):
        if not self.serial.startswith("emulator-"):
            raise RuntimeError("Updates are restricted to explicitly selected lab emulators")
        policy = self.device("shell", "dumpsys", "device_policy")
        if not re.search(r"Device Owner:\s+admin=ComponentInfo\{dev\.fleet\.agent/", policy):
            raise RuntimeError("The lab agent must already be Device Owner")
        if lock_state(self.activity()) != "NONE":
            raise RuntimeError("Exit the existing kiosk before preparing an update run")
        self.command([sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_android_evidence.py", "-v"])
        agent, baseline, candidate, wrong_signer, invalid = self.prepare_apks(prepared_run)
        version = self.installed_version()
        if version is not None and version > 1:
            if not reset_sample:
                raise RuntimeError("Sample is already newer than v1; --reset-sample permits uninstalling only dev.fleet.sample and clearing its lab data")
            self.device("uninstall", SAMPLE, timeout=180)
        self.device("install", "-r", "-t", str(agent), timeout=180)
        self.device("install", "-r", str(baseline), timeout=180)
        if self.installed_version() != 1:
            raise AssertionError("Expected installed sample version code 1 before agent update")
        self.smoke()
        self.stop_video()
        self.start_video("update")
        self.device("shell", "am", "start", "-W", "-n", AGENT + "/.MainActivity")
        self.wait_activity("NONE", AGENT)
        self.click("Start sample kiosk")
        self.wait_activity("LOCKED", SAMPLE)
        self.checkpoint("update-baseline-v1")
        self.click("Lab management")
        self.wait_activity("LOCKED", AGENT)
        self.stage(baseline)
        self.click("Install staged lab v2")
        self.wait_update("only supports the sample v1-to-v2 transition")
        if self.installed_version() != 1:
            raise AssertionError("Version changed after rejected v1 artifact")
        self.checkpoint("same-version-rejected")
        self.stage(agent)
        self.click("Install staged lab v2")
        self.wait_update("Wrong package identity")
        if self.installed_version() != 1:
            raise AssertionError("Version changed after rejected package identity")
        self.checkpoint("wrong-package-rejected")
        for artifact, expected, checkpoint in (
            (wrong_signer, "APK signer mismatch", "wrong-signer-rejected"),
            (invalid, "Invalid APK", "invalid-apk-rejected"),
        ):
            self.stage(artifact)
            self.click("Install staged lab v2")
            self.wait_update(expected)
            if self.installed_version() != 1:
                raise AssertionError(f"Version changed after {checkpoint}")
            self.wait_activity("LOCKED", AGENT)
            self.checkpoint(checkpoint)
        self.stage(candidate)
        self.checkpoint("v2-staged-private-storage")
        self.click("Install staged lab v2")
        self.wait_update("Lab v2 installed silently; observed version code 2.")
        if self.installed_version() != 2:
            raise AssertionError("Package manager did not observe sample version code 2")
        self.wait_activity("LOCKED", AGENT)
        self.checkpoint("installer-success-no-confirmation")
        self.click("Start sample kiosk")
        self.wait_activity("LOCKED", SAMPLE)
        self.wait_text("Version 0.2.0 (2)")
        self.checkpoint("updated-v2-kiosk")
        self.click("Lab management")
        self.wait_activity("LOCKED", AGENT)
        self.maintenance_exit()
        self.checkpoint("update-kiosk-recovered")

    def reboot_device(self, timeout=360):
        previous = self.device("shell", "cat", "/proc/sys/kernel/random/boot_id").strip()
        if not previous:
            raise AssertionError("Missing pre-reboot boot ID")
        self.stop_video()
        self.device("reboot")
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            completed = self.device("shell", "getprop", "sys.boot_completed", check=False, timeout=15).strip()
            if completed == "1":
                current = self.device("shell", "cat", "/proc/sys/kernel/random/boot_id", check=False, timeout=15).strip()
                if current and current != previous:
                    return
            time.sleep(1)
        raise AssertionError("Device did not complete a new boot within the deadline")

    def wait_boot_policy(self, enabled, timeout=120):
        boot_count = self.device("shell", "settings", "get", "global", "boot_count").strip()
        if not boot_count.isdigit():
            raise AssertionError("Missing current Android boot count")
        expected = "Kiosk launch requested after boot." if enabled else "Kiosk disabled; no boot launch requested."
        deadline = time.monotonic() + timeout
        while True:
            state = self.device("exec-out", "run-as", AGENT, "cat", "shared_prefs/lab-kiosk.xml", check=False)
            if state:
                preferences = ET.fromstring(state)
                actual_enabled = preferences.find("boolean[@name='enabled']")
                actual_boot = preferences.find("int[@name='bootCount']")
                status = preferences.find("string[@name='bootStatus']")
                if actual_boot is not None and actual_boot.get("value") == boot_count:
                    if actual_enabled is None or actual_enabled.get("value") != str(enabled).lower():
                        raise AssertionError("Boot receiver observed incorrect persisted kiosk intent")
                    if status is None or status.text != expected:
                        raise AssertionError(f"Boot receiver failed: {status.text if status is not None else 'missing status'}")
                    self.output.joinpath(f"boot-policy-{boot_count}.xml").write_text(state)
                    return
            if time.monotonic() >= deadline:
                raise AssertionError("Boot receiver did not record a result for the current boot")
            time.sleep(0.4)

    def reboot_suite(self):
        if not self.serial.startswith("emulator-"):
            raise RuntimeError("Reboots are restricted to explicitly selected lab emulators")
        policy = self.device("shell", "dumpsys", "device_policy")
        if not re.search(r"Device Owner:\s+admin=ComponentInfo\{dev\.fleet\.agent/", policy):
            raise RuntimeError("The lab agent must already be Device Owner")
        if lock_state(self.activity()) != "NONE":
            raise RuntimeError("Exit the existing kiosk before the reboot suite")
        version = self.installed_version()
        if version is None:
            raise RuntimeError("Install the sample app before the reboot suite")
        self.command([sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_android_evidence.py", "-v"])
        self.command([str(ROOT / "android/gradlew"), "-p", str(ROOT / "android"),
                      ":agent:assembleDebug", ":agent:lintDebug", "--console", "plain"], timeout=600)
        artifact = self.output / "agent-debug.apk"
        shutil.copy2(ROOT / "android/agent/build/outputs/apk/debug/agent-debug.apk", artifact)
        self.output.joinpath("apk-sha256.json").write_text(json.dumps(
            {artifact.name: hashlib.sha256(artifact.read_bytes()).hexdigest()}, indent=2) + "\n")
        self.device("install", "-r", "-t", str(artifact), timeout=180)
        self.pair_operator()
        self.device("shell", "am", "start", "-W", "-n", AGENT + "/.MainActivity")
        self.wait_activity("NONE", AGENT)
        self.start_video("before-enabled-reboot")
        self.click("Start sample kiosk")
        self.wait_activity("LOCKED", SAMPLE)
        self.checkpoint("kiosk-enabled-before-reboot")
        self.reboot_device()
        self.start_video("after-enabled-reboot")
        self.wait_boot_policy(True)
        self.wait_activity("LOCKED", SAMPLE)
        if self.installed_version() != version:
            raise AssertionError("Sample version changed across reboot")
        self.checkpoint("kiosk-restored-automatically")
        self.device("shell", "input", "keyevent", "KEYCODE_HOME")
        self.wait_activity("LOCKED", SAMPLE)
        self.checkpoint("restored-kiosk-contains-home")
        self.click("Lab management")
        self.wait_activity("LOCKED", AGENT)
        self.maintenance_exit()
        self.checkpoint("kiosk-disabled-before-second-reboot")
        self.reboot_device()
        self.start_video("after-disabled-reboot")
        self.wait_boot_policy(False)
        self.wait_activity("NONE", "com.android.launcher3")
        self.checkpoint("disabled-kiosk-not-restored")
        self.device("shell", "am", "start", "-W", "-n", AGENT + "/.MainActivity")
        self.wait_activity("NONE", AGENT)
        self.wait_text("Kiosk intent enabled: false")
        self.checkpoint("persisted-exit-confirmed")

    def smoke(self):
        if not self.serial.startswith("emulator-"):
            raise RuntimeError("This runner only supports explicitly selected Android emulators")
        if self.device("shell", "getprop", "sys.boot_completed").strip() != "1":
            raise RuntimeError("Android has not completed boot")
        identity = self.device("emu", "avd", "name")
        self.output.joinpath("environment.txt").write_text(identity + self.device("shell", "getprop", "ro.build.fingerprint"))
        policy = self.device("shell", "dumpsys", "device_policy")
        if not re.search(r"Device Owner:\s+admin=ComponentInfo\{dev\.fleet\.agent/", policy):
            raise RuntimeError("Fleet Lab Agent must already be Device Owner; this runner does not wipe or provision devices")
        if lock_state(self.activity()) != "NONE":
            raise RuntimeError("Exit the existing kiosk before starting a new recording")
        self.device("shell", "am", "start", "-W", "-n", AGENT + "/.MainActivity")
        self.wait_activity("NONE", AGENT)
        self.pair_operator()
        self.start_video()
        self.checkpoint("device-owner-ready")
        self.click("Start sample kiosk")
        self.wait_activity("LOCKED", SAMPLE)
        self.assert_restrictions(True)
        self.checkpoint("sample-kiosk-locked")
        self.device("shell", "input", "keyevent", "KEYCODE_HOME")
        self.wait_activity("LOCKED", SAMPLE)
        self.checkpoint("home-contained")
        self.device("shell", "input", "keyevent", "KEYCODE_APP_SWITCH")
        self.wait_activity("LOCKED", SAMPLE)
        self.checkpoint("recents-contained")
        self.click("Lab management")
        self.wait_activity("LOCKED", AGENT)
        self.checkpoint("lab-management-locked")
        self.maintenance_exit()
        self.assert_restrictions(False)
        self.checkpoint("kiosk-exited")
        self.device("shell", "input", "keyevent", "KEYCODE_HOME")
        self.wait_activity("NONE", "com.android.launcher3")
        self.checkpoint("home-restored")


def main():
    parser = argparse.ArgumentParser(description="Record and rerun the already-provisioned disposable Android lab; never wipes or provisions devices.")
    parser.add_argument("--serial", required=True)
    parser.add_argument("--adb", default=str(Path(os.environ.get("ANDROID_HOME", str(Path.home() / "Library/Android/sdk"))) / "platform-tools/adb"))
    parser.add_argument("--output", type=Path)
    parser.add_argument("--suite", choices=("smoke", "update", "recover", "reboot", "phase-one", "mqtt", "mqtt-lifecycle"), default="smoke")
    parser.add_argument("--prepared-run", type=Path, help="Reuse and hash-check APK fixtures from an earlier local update bundle instead of rebuilding")
    parser.add_argument("--reset-sample", action="store_true", help="Allow uninstalling only the sample app to restore v1 for a repeated update test")
    arguments = parser.parse_args()
    if arguments.prepared_run is not None and arguments.suite != "update":
        parser.error("--prepared-run requires --suite update")
    output = arguments.output or ROOT / "artifacts/android" / (datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ-") + arguments.suite)
    run = EvidenceRun(arguments.adb, arguments.serial, output.resolve())
    try:
        if arguments.suite == "update":
            run.update(arguments.reset_sample, arguments.prepared_run)
        elif arguments.suite == "recover":
            run.recover()
        elif arguments.suite == "reboot":
            run.reboot_suite()
        elif arguments.suite == "phase-one":
            run.phase_one()
        elif arguments.suite == "mqtt":
            run.mqtt_suite()
        elif arguments.suite == "mqtt-lifecycle":
            run.mqtt_lifecycle()
        else:
            run.command([sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_android_evidence.py", "-v"])
            run.smoke()
        run.result = "passed"
    except Exception as exception:
        run.result = "failed"
        run.error = str(exception)
        try:
            run.checkpoint("failure-state")
        except Exception as capture_error:
            run.error += f"; failure capture: {capture_error}"
    finally:
        try:
            run.stop_video()
            if run.videos and sys.platform == "darwin" and shutil.which("swift"):
                run.command([shutil.which("swift"), "-suppress-warnings", str(ROOT / "tools/verify-recordings.swift")],
                            timeout=180, extra_environment={"RECORDING_DIRECTORY": str(run.output)})
        except Exception as video_error:
            run.result = "failed"
            run.error = f"{run.error or ''}; recording finalization: {video_error}"
        run.save()
    print(f"{run.result.upper()}: {run.output / 'replay.html'}", flush=True)
    return 0 if run.result == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())