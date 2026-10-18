import json
import base64
import hashlib
import unittest
import tempfile
from pathlib import Path

from check_android_backend import public_configuration, public_tls_diagnostics, verify_event_proof, verify_mqtt_proof, verify_event_process_proof, run_event_process_phases


class AndroidBackendEvidenceTests(unittest.TestCase):
    def test_event_reboot_proof_requires_increased_boot_count_not_distinct_pid(self):
        prepared = {"schemaVersion": 1, "preparedProcessId": 123, "preparedBootCount": 10,
                    "deviceId": "7512f600-bb5b-4353-b67a-3a56126308d7",
                    "eventId": "374823b7-0e54-4893-b661-e638cc03310c", "wireSha256": "a" * 64}
        recovered = {"eventProcessRecoveryVerified": True, "eventRebootRecoveryVerified": True,
                     "preparedProcessId": 123, "recoveredProcessId": 123,
                     "preparedBootCount": 10, "recoveredBootCount": 11,
                     "deviceId": prepared["deviceId"], "retainedEventId": prepared["eventId"],
                     "recoveredWireSha256": "a" * 64}
        verify_event_process_proof(prepared, recovered, reboot=True)
        for name, value in (("eventRebootRecoveryVerified", False), ("preparedBootCount", 9),
                            ("preparedBootCount", 10.0), ("recoveredBootCount", 10), ("recoveredBootCount", True),
                            ("recoveredWireSha256", "b" * 64)):
            with self.subTest(name=name), self.assertRaises(ValueError):
                verify_event_process_proof(prepared, dict(recovered, **{name: value}), reboot=True)

    def test_event_reboot_rejects_unchanged_kernel_boot_before_resume(self):
        calls = []

        def execute(arguments, timeout):
            calls.append(arguments)
            if "instrument" in arguments:
                return "OK (1 test)\nINSTRUMENTATION_CODE: -1\n"
            if "sys.boot_completed" in arguments:
                return "1"
            if "/proc/sys/kernel/random/boot_id" in arguments:
                return "7512f600-bb5b-4353-b67a-3a56126308d7"
            return json.dumps({"preparedProcessId": 123})

        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "Kernel boot ID did not change"):
                run_event_process_phases(execute, ["adb"], Path(directory), reboot=True)
            self.assertFalse((Path(directory) / "event-reboot-verification.json").exists())
        self.assertTrue(any("reboot" in call for call in calls))
        self.assertTrue(any("wait-for-disconnect" in call for call in calls))
        self.assertFalse(any("resume" in call for call in calls))

    def test_event_recovery_requires_new_process_same_identity_event_and_wire(self):
        prepared = {"schemaVersion": 1, "preparedProcessId": 123,
                    "deviceId": "7512f600-bb5b-4353-b67a-3a56126308d7",
                    "eventId": "374823b7-0e54-4893-b661-e638cc03310c", "wireSha256": "a" * 64}
        recovered = {"eventProcessRecoveryVerified": True, "preparedProcessId": 123, "recoveredProcessId": 456,
                     "deviceId": prepared["deviceId"], "retainedEventId": prepared["eventId"], "recoveredWireSha256": "a" * 64}
        verify_event_process_proof(prepared, recovered)
        for name, value in (("eventProcessRecoveryVerified", False), ("preparedProcessId", 124),
                            ("recoveredProcessId", 123), ("recoveredProcessId", True), ("preparedProcessId", 123.0),
                            ("deviceId", prepared["eventId"]), ("retainedEventId", prepared["deviceId"]),
                            ("recoveredWireSha256", "b" * 64)):
            with self.subTest(name=name), self.assertRaises(ValueError):
                verify_event_process_proof(prepared, dict(recovered, **{name: value}))

    def test_event_recovery_does_not_resume_when_old_process_is_present(self):
        calls = []

        def execute(arguments, timeout):
            calls.append(arguments)
            if "instrument" in arguments:
                return "OK (1 test)\nINSTRUMENTATION_CODE: -1\n"
            if "/proc/123" in arguments:
                raise RuntimeError("old process exists")
            return json.dumps({"preparedProcessId": 123})

        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(RuntimeError, "old process exists"):
                run_event_process_phases(execute, ["adb"], Path(directory))
        self.assertTrue(any("force-stop" in call for call in calls))
        self.assertFalse(any("resume" in call for call in calls))

    def test_worker_proof_requires_worker_retry_and_disabled_retention(self):
        device_id = "7512f600-bb5b-4353-b67a-3a56126308d7"
        event_id = "374823b7-0e54-4893-b661-e638cc03310c"
        device = {"deviceId": device_id, "eventUploadVerified": True, "retainedEventId": event_id,
                  "eventAcknowledgement": {"schemaVersion": 1, "deviceId": device_id, "eventId": event_id,
                                           "receivedAt": "2026-10-17T12:00:00Z"},
                  "eventDeletionVerified": True, "eventDeleteRollbackVerified": True, "eventInboxRetained": True,
                  "disabledEventRetained": True, "eventWorkerVerified": True, "eventWorkerRetryVerified": True,
                  "eventWorkerDisabledRetained": True}
        backend = {"eventUploadVerified": True, "suppressedAcknowledgements": 1, "recoveredAcknowledgements": 3,
                   "eventDeletionVerified": True, "eventWorkerVerified": True}
        verify_event_proof(device, backend, deletion=True, worker=True)
        for name in ("eventWorkerVerified", "eventWorkerRetryVerified", "eventWorkerDisabledRetained"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                verify_event_proof(dict(device, **{name: False}), backend, deletion=True, worker=True)
        with self.assertRaises(ValueError):
            verify_event_proof(device, dict(backend, eventWorkerVerified=False), deletion=True, worker=True)

    def test_deletion_proof_requires_rollback_reopen_inbox_and_disabled_retention(self):
        device_id = "7512f600-bb5b-4353-b67a-3a56126308d7"
        event_id = "374823b7-0e54-4893-b661-e638cc03310c"
        device = {"deviceId": device_id, "eventUploadVerified": True, "retainedEventId": event_id,
                  "eventAcknowledgement": {"schemaVersion": 1, "deviceId": device_id, "eventId": event_id,
                                           "receivedAt": "2026-10-17T12:00:00Z"},
                  "eventDeletionVerified": True, "eventDeleteRollbackVerified": True,
                  "eventInboxRetained": True, "disabledEventRetained": True}
        backend = {"eventUploadVerified": True, "suppressedAcknowledgements": 1,
                   "recoveredAcknowledgements": 3, "eventDeletionVerified": True}
        verify_event_proof(device, backend, deletion=True)
        for name in ("eventDeletionVerified", "eventDeleteRollbackVerified", "eventInboxRetained", "disabledEventRetained"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                verify_event_proof(dict(device, **{name: False}), backend, deletion=True)
        for name, value in (("eventDeletionVerified", False), ("recoveredAcknowledgements", 1)):
            with self.subTest(name=name), self.assertRaises(ValueError):
                verify_event_proof(device, dict(backend, **{name: value}), deletion=True)

    def test_event_proof_requires_committed_recovery_and_retained_matching_event(self):
        device_id = "7512f600-bb5b-4353-b67a-3a56126308d7"
        event_id = "374823b7-0e54-4893-b661-e638cc03310c"
        device = {"deviceId": device_id, "eventUploadVerified": True, "retainedEventId": event_id,
                  "eventAcknowledgement": {"schemaVersion": 1, "deviceId": device_id,
                                           "eventId": event_id, "receivedAt": "2026-10-17T12:00:00Z"}}
        backend = {"eventUploadVerified": True, "suppressedAcknowledgements": 1, "recoveredAcknowledgements": 1}
        verify_event_proof(device, backend)
        for key, value in (("eventUploadVerified", False), ("suppressedAcknowledgements", 0),
                           ("recoveredAcknowledgements", True), ("recoveredAcknowledgements", 0)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify_event_proof(device, dict(backend, **{key: value}))
        for key, value in (("eventUploadVerified", False), ("retainedEventId", "other"), ("eventAcknowledgement", None)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify_event_proof(dict(device, **{key: value}), backend)
        for key, value in (("schemaVersion", True), ("deviceId", "other"), ("eventId", "other"),
                           ("receivedAt", "2026-10-17T12:00:00"), ("extra", "unexpected")):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify_event_proof(dict(device, eventAcknowledgement=dict(device["eventAcknowledgement"], **{key: value})), backend)

    def test_mqtt_proof_requires_exact_certificate_and_independent_rejections(self):
        device = {"deviceId": "device-id", "mqttAuthenticated": True, "mqttDisabledRejected": True,
                  "responseBase64": base64.b64encode(json.dumps({"certificateDer": base64.b64encode(b"leaf").decode()}).encode()).decode()}
        broker = {"stage": "mqtt-verified", "stopped": True, "probeRoundtrips": 1,
                  "identities": {"device-id": hashlib.sha256(b"leaf").hexdigest()},
                  "crossDeviceDenied": 1, "wildcardDenied": 1, "disabledSubscribeDenied": 1,
                  "disabledConnectionsRejected": 1}
        command_id = "7512f600-bb5b-4353-b67a-3a56126308d7"
        broker.update(commandId=command_id, commandDeliveries=2)
        device.update(mqttJournalVerified=True, mqttReceipt={"schemaVersion": 1,
            "eventId": "374823b7-0e54-4893-b661-e638cc03310c", "commandId": command_id,
            "deviceId": "device-id", "correlationId": command_id, "recordedAt": "2026-10-17T12:00:00Z", "state": "received"})
        verify_mqtt_proof(device, broker)
        for key, value in (("stopped", False), ("identities", {"other": "0" * 64}), ("probeRoundtrips", 0),
                           ("crossDeviceDenied", 0), ("wildcardDenied", True), ("disabledSubscribeDenied", 0),
                           ("disabledConnectionsRejected", 0), ("commandDeliveries", True), ("commandDeliveries", 1),
                           ("commandId", "0451e62c-b484-4c2b-bcbd-c41099fe0937")):
            with self.subTest(key=key):
                with self.assertRaises(ValueError):
                    verify_mqtt_proof(device, dict(broker, **{key: value}))
        for key, value in (("mqttJournalVerified", False), ("mqttReceipt", None)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify_mqtt_proof(dict(device, **{key: value}), broker)
        for key, value in (("schemaVersion", True), ("eventId", "bad-id"), ("deviceId", "other"),
                           ("state", "succeeded"), ("correlationId", "other"), ("recordedAt", "2026-10-17T12:00:00")):
            with self.subTest(key=key), self.assertRaises(ValueError):
                verify_mqtt_proof(dict(device, mqttReceipt=dict(device["mqttReceipt"], **{key: value})), broker)

    def test_tls_diagnostics_only_retains_fixed_public_labels(self):
        self.assertEqual(["TLS:ACCEPTED", "TLS:CHAIN_REJECTED"], public_tls_diagnostics(
            "sensitive-test-sentinel\nTLS:ACCEPTED\nTLS:ACCEPTED secret\nTLS:CHAIN_REJECTED\n"))
        self.assertEqual(["TLS:ERROR:IOException:-2146232800"], public_tls_diagnostics(
            "TLS:ERROR:IOException:-2146232800\nTLS:ERROR:SecretException:42\nTLS:ERROR:IOException:42 private\n"))

    def test_public_configuration_is_allowlisted_and_never_retains_bootstrap_secret(self):
        configuration = {"namespace": "integration-example", "serverRootDer": "public-server-root",
                         "deviceRootDer": "public-device-root", "unexpected": "sensitive-test-sentinel",
                         "bootstrap": {"schemaVersion": 1, "endpoint": "https://10.0.2.2:1234/api/v1/enrollments",
                                       "pendingEnrollmentId": "example-id", "bootstrapToken": "sensitive-test-sentinel",
                                       "extra": "sensitive-test-sentinel"}}
        public = public_configuration(configuration)
        self.assertNotIn("sensitive-test-sentinel", json.dumps(public))
        self.assertNotIn("bootstrapToken", public["bootstrap"])
        self.assertEqual({"schemaVersion", "endpoint", "pendingEnrollmentId"}, set(public["bootstrap"]))
        self.assertEqual("sensitive-test-sentinel", configuration["bootstrap"]["bootstrapToken"])
        configuration.update(eventUpload=True, eventDeletion=True, eventWorker=True, eventCommandId="public-command", eventCorrelationId="public-correlation")
        event_public = public_configuration(configuration)
        self.assertTrue(event_public["eventUpload"])
        self.assertTrue(event_public["eventDeletion"])
        self.assertTrue(event_public["eventWorker"])
        self.assertEqual("public-command", event_public["eventCommandId"])
        self.assertEqual("public-correlation", event_public["eventCorrelationId"])
        self.assertNotIn("sensitive-test-sentinel", json.dumps(event_public))


if __name__ == "__main__":
    unittest.main()