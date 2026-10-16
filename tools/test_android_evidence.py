import hashlib
import json
import unittest
from pathlib import Path
import tempfile
from unittest.mock import Mock, patch

from android_evidence import EvidenceRun, button_center, effective_restrictions, lock_state, resumed_package


class EvidenceWaitTests(unittest.TestCase):
    def test_waits_past_splash_for_rendered_version(self):
        run = object.__new__(EvidenceRun)
        run.ui = Mock(side_effect=['<hierarchy><node text="" /></hierarchy>',
                                   '<hierarchy><node text="Fleet Sample&#10;Version 0.2.0 (2)" /></hierarchy>'])
        with patch("android_evidence.time.sleep"):
            run.wait_text("Version 0.2.0 (2)")
        self.assertEqual(run.ui.call_count, 2)

    def test_wrong_version_is_not_accepted(self):
        run = object.__new__(EvidenceRun)
        run.ui = Mock(return_value='<hierarchy><node text="Version 0.1.0 (1)" /></hierarchy>')
        with self.assertRaisesRegex(AssertionError, "UI did not render expected text"):
            run.wait_text("Version 0.2.0 (2)", timeout=0)

    def test_non_text_attribute_is_not_accepted(self):
        run = object.__new__(EvidenceRun)
        run.ui = Mock(return_value='<hierarchy><node content-desc="Version 0.2.0 (2)" /></hierarchy>')
        with self.assertRaisesRegex(AssertionError, "UI did not render expected text"):
            run.wait_text("Version 0.2.0 (2)", timeout=0)


class OperatorPairingTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        self.addCleanup(patch.stopall)
        patch("android_evidence.ROOT", root).start()
        operator = root / ".lab-private" / "lab-device"
        operator.mkdir(parents=True)
        (operator / "operator.pem").write_bytes(b"test-private-placeholder")
        (operator / "operator-public.der").write_bytes(b"expected-public-key")
        self.run = object.__new__(EvidenceRun)
        self.run.serial = "lab-device"
        self.run.command = Mock()
        self.run.activity = Mock(return_value="mLockTaskModeState=NONE")

    def test_missing_key_file_pairs_without_reading_error_as_key(self):
        self.run.device = Mock(return_value="")
        self.run.pair_operator()
        self.assertFalse(any(call.args[:1] == ("exec-out",)
                             for call in self.run.device.call_args_list))
        self.assertEqual(self.run.device.call_args_list[-1].kwargs["input_data"],
                         b"expected-public-key")

    def test_different_paired_key_is_never_replaced(self):
        self.run.device = Mock(side_effect=["present\n", b"different-public-key"])
        with self.assertRaisesRegex(AssertionError, "refusing automatic key replacement"):
            self.run.pair_operator()
        self.assertEqual(self.run.device.call_count, 2)

    def test_matching_key_does_not_require_repairing_while_locked(self):
        self.run.device = Mock(side_effect=["present\n", b"expected-public-key"])
        self.run.activity.return_value = "mLockTaskModeState=LOCKED"
        self.run.pair_operator()
        self.run.activity.assert_not_called()
        self.assertEqual(self.run.device.call_count, 2)

    def test_missing_key_is_not_paired_while_locked(self):
        self.run.device = Mock(return_value="")
        self.run.activity.return_value = "mLockTaskModeState=LOCKED"
        with self.assertRaisesRegex(AssertionError, "before entering kiosk"):
            self.run.pair_operator()
        self.assertEqual(self.run.device.call_count, 1)


class BootEvidenceTests(unittest.TestCase):
    def test_exit_waits_for_resumed_activity_after_unlock(self):
        run = object.__new__(EvidenceRun)
        run.activity = Mock(side_effect=["mLockTaskModeState=NONE",
                                       "mLockTaskModeState=NONE\n topResumedActivity=ActivityRecord{abc u0 dev.fleet.agent/.Main t1}"])
        with patch("android_evidence.time.sleep"):
            run.wait_kiosk_exit()
        self.assertEqual(run.activity.call_count, 2)

    def test_exit_accepts_launcher_only_after_unlock(self):
        run = object.__new__(EvidenceRun)
        run.activity = Mock(side_effect=["mLockTaskModeState=LOCKED",
                                       "mLockTaskModeState=NONE\n topResumedActivity=ActivityRecord{abc u0 com.android.launcher3/.Launcher t1}"])
        run.device = Mock()
        run.wait_activity = Mock()
        with patch("android_evidence.time.sleep"):
            run.wait_kiosk_exit()
        run.device.assert_called_once_with("shell", "am", "start", "-W", "-n", "dev.fleet.agent/.MainActivity")
        run.wait_activity.assert_called_once_with("NONE", "dev.fleet.agent")

    def test_exit_rejects_unknown_foreground(self):
        run = object.__new__(EvidenceRun)
        run.activity = Mock(return_value="mLockTaskModeState=NONE\n topResumedActivity=ActivityRecord{abc u0 unknown.app/.Main t1}")
        with self.assertRaisesRegex(AssertionError, "Unexpected unlocked"):
            run.wait_kiosk_exit()

    def test_exit_rejects_active_lock(self):
        run = object.__new__(EvidenceRun)
        run.activity = Mock(return_value="mLockTaskModeState=LOCKED")
        with self.assertRaisesRegex(AssertionError, "did not clear"):
            run.wait_kiosk_exit(timeout=0)

    def boot_state(self, enabled="true", boot="7", status="Kiosk launch requested after boot."):
        return (f'<map><boolean name="enabled" value="{enabled}" />'
                f'<int name="bootCount" value="{boot}" />'
                f'<string name="bootStatus">{status}</string></map>')

    def test_accepts_current_boot_receiver_result(self):
        run = object.__new__(EvidenceRun)
        run.device = Mock(side_effect=["7", self.boot_state()])
        with tempfile.TemporaryDirectory() as directory:
            run.output = Path(directory)
            run.wait_boot_policy(True)
            self.assertTrue((run.output / "boot-policy-7.xml").exists())

    def test_rejects_previous_boot_receiver_result(self):
        run = object.__new__(EvidenceRun)
        run.device = Mock(side_effect=["8", self.boot_state()])
        with self.assertRaisesRegex(AssertionError, "current boot"):
            run.wait_boot_policy(True, timeout=0)

    def test_rejects_wrong_persisted_intent(self):
        run = object.__new__(EvidenceRun)
        run.device = Mock(side_effect=["7", self.boot_state(enabled="false")])
        with self.assertRaisesRegex(AssertionError, "incorrect persisted"):
            run.wait_boot_policy(True)

    def test_rejects_receiver_failure(self):
        run = object.__new__(EvidenceRun)
        run.device = Mock(side_effect=["7", self.boot_state(status="Kiosk boot restoration failed: IllegalStateException")])
        with self.assertRaisesRegex(AssertionError, "Boot receiver failed"):
            run.wait_boot_policy(True)

    def test_accepts_disabled_boot_without_launch(self):
        run = object.__new__(EvidenceRun)
        run.device = Mock(side_effect=["7", self.boot_state(enabled="false", status="Kiosk disabled; no boot launch requested.")])
        with tempfile.TemporaryDirectory() as directory:
            run.output = Path(directory)
            run.wait_boot_policy(False)
            self.assertTrue((run.output / "boot-policy-7.xml").exists())

    def test_reboot_requires_changed_boot_identity(self):
        run = object.__new__(EvidenceRun)
        run.stop_video = Mock()
        run.device = Mock(side_effect=["old-boot", "", "1", "old-boot", "1", "new-boot"])
        with patch("android_evidence.time.sleep"):
            run.reboot_device()
        run.stop_video.assert_called_once()
        self.assertEqual(run.device.call_count, 6)


class PreparedArtifactTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.source = Path(directory.name) / "source"
        self.source.mkdir()
        self.run = object.__new__(EvidenceRun)
        self.run.output = Path(directory.name) / "output"
        self.run.output.mkdir()
        self.names = ("agent-debug.apk", "sample-v1.apk", "sample-v2.apk",
                      "sample-v2-wrong-signer.apk", "sample-invalid.apk")
        hashes = {}
        for name in self.names:
            content = name.encode()
            (self.source / name).write_bytes(content)
            hashes[name] = hashlib.sha256(content).hexdigest()
        (self.source / "apk-sha256.json").write_text(json.dumps(hashes))

    def test_copies_verified_artifacts_and_records_provenance(self):
        artifacts = self.run.prepare_apks(self.source)
        self.assertEqual(tuple(artifact.name for artifact in artifacts), self.names)
        for artifact in artifacts:
            self.assertEqual(artifact.read_bytes(), (self.source / artifact.name).read_bytes())
        provenance = json.loads((self.run.output / "prepared-source.json").read_text())
        self.assertEqual(provenance["source"], str(self.source.resolve()))
        self.assertFalse(provenance["buildAndLintRerun"])

    def test_altered_artifact_is_rejected(self):
        (self.source / "sample-v2.apk").write_bytes(b"altered")
        with self.assertRaisesRegex(AssertionError, "hash mismatch: sample-v2.apk"):
            self.run.prepare_apks(self.source)
        self.assertFalse((self.run.output / "prepared-source.json").exists())

    def test_missing_hash_is_rejected(self):
        (self.source / "apk-sha256.json").write_text("{}")
        with self.assertRaisesRegex(AssertionError, "hash mismatch: agent-debug.apk"):
            self.run.prepare_apks(self.source)


class NegativeFixtureTests(unittest.TestCase):
    def test_same_signer_fixture_is_rejected(self):
        run = object.__new__(EvidenceRun)
        run.adb = "/sdk/platform-tools/adb"
        certificate = "Signer #1 certificate SHA-256 digest: aabbcc"
        run.command = Mock(side_effect=["", "", certificate, certificate])
        with tempfile.TemporaryDirectory() as directory:
            run.output = Path(directory)
            with self.assertRaisesRegex(AssertionError, "different APK signer"):
                run.negative_apks(run.output / "sample-v2.apk")
            self.assertFalse((run.output / "sample-invalid.apk").exists())

    def test_missing_signer_certificate_is_rejected(self):
        run = object.__new__(EvidenceRun)
        run.adb = "/sdk/platform-tools/adb"
        run.command = Mock(side_effect=["", "", "verification without certificate"])
        with tempfile.TemporaryDirectory() as directory:
            run.output = Path(directory)
            with self.assertRaisesRegex(AssertionError, "did not report a signer certificate"):
                run.negative_apks(run.output / "sample-v2.apk")


class EvidenceParserTests(unittest.TestCase):
    def test_effective_restrictions_exclude_guest_defaults(self):
        state = "UserInfo{0:Owner:13}\n    Effective restrictions:\n      no_control_apps\n    UserProperties:\nDevice properties:\n  Guest restrictions:\n    no_config_wifi\n"
        self.assertEqual(effective_restrictions(state), {"no_control_apps"})

    def test_empty_restrictions_are_not_guest_restrictions(self):
        state = "UserInfo{0:Owner:13}\n    Effective restrictions:\n      none\n    UserProperties:\nDevice properties:\n  Guest restrictions:\n    no_config_wifi\n"
        self.assertEqual(effective_restrictions(state), set())

    def test_missing_user_restrictions_fail_closed(self):
        with self.assertRaisesRegex(AssertionError, "Missing user 0"):
            effective_restrictions("Guest restrictions:\n    no_config_wifi\n")

    def test_button_matches_label_case_and_calculates_center(self):
        xml = '<hierarchy><node text="START SAMPLE KIOSK" enabled="true" clickable="true" bounds="[63,370][1017,496]" /></hierarchy>'
        self.assertEqual(button_center(xml, "Start sample kiosk"), (540, 433))

    def test_disabled_control_is_rejected(self):
        xml = '<hierarchy><node text="Exit" enabled="false" clickable="true" bounds="[0,0][10,10]" /></hierarchy>'
        with self.assertRaisesRegex(ValueError, "not enabled"):
            button_center(xml, "Exit")

    def test_missing_control_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "not found"):
            button_center("<hierarchy />", "Exit")

    def test_invalid_bounds_are_rejected(self):
        xml = '<hierarchy><node text="Exit" enabled="true" clickable="true" bounds="invalid" /></hierarchy>'
        with self.assertRaisesRegex(ValueError, "Invalid control bounds"):
            button_center(xml, "Exit")

    def test_locked_is_distinct_from_pinned_and_none(self):
        for state in ("LOCKED", "PINNED", "NONE"):
            self.assertEqual(lock_state("    mLockTaskModeState=" + state), state)
        self.assertIsNone(lock_state("incomplete dump"))

    def test_resumed_package_ignores_other_activity_mentions(self):
        activity = "packageName=dev.fleet.agent\n topResumedActivity=ActivityRecord{abc u0 dev.fleet.sample/.MainActivity t9}"
        self.assertEqual(resumed_package(activity), "dev.fleet.sample")
        self.assertIsNone(resumed_package("packageName=dev.fleet.sample"))


if __name__ == "__main__":
    unittest.main()