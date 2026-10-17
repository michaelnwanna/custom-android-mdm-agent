import unittest
import copy
import json
from pathlib import Path
import tempfile

from check_android_enrollment import COMMAND_CONTRACT_INPUTS, instrumentation_passed, verify_recovery_proof, run_process_recovery


class EnrollmentEvidenceTests(unittest.TestCase):
    def test_recovery_orchestration_checks_process_absence_and_cleans_up_on_failure(self):
        calls = []

        def command(arguments, timeout):
            calls.append(arguments)
            if "am" in arguments and "instrument" in arguments:
                return "OK (1 test)\nINSTRUMENTATION_CODE: -1\n"
            if any(str(argument).endswith("/prepared.json") for argument in arguments):
                return json.dumps({"schemaVersion": 1, "processId": 123})
            if "/proc/123" in arguments:
                raise RuntimeError("old process still exists")
            return ""

        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(RuntimeError, "old process still exists"):
                run_process_recovery(command, ["adb", "-s", "emulator-5554"], Path(directory))
            self.assertTrue((Path(directory) / "recovery-prepared.json").exists())
            self.assertFalse((Path(directory) / "recovery-verified.json").exists())
        self.assertTrue(any("force-stop" in call for call in calls))
        self.assertFalse(any(any("#resume" in str(argument) for argument in call) for call in calls))
        self.assertTrue(any(any("#cleanupOnlyThisRecoveryProbe" in str(argument) for argument in call) for call in calls))

    def test_recovery_proof_requires_same_work_new_process_and_real_retry_interval(self):
        nonce = "7512f600-bb5b-4353-b67a-3a56126308d7"
        work_id = "374823b7-0e54-4893-b661-e638cc03310c"
        prepared = {"schemaVersion": 1, "probe": nonce, "workId": work_id, "processId": 100}
        recovered = {"schemaVersion": 1, "probe": nonce, "workId": work_id, "state": "SUCCEEDED",
                     "preparedProcessId": 100, "recoveredProcessId": 200,
                     "first": {"attempt": 0, "workId": work_id, "processId": 200, "elapsedMillis": 10000},
                     "second": {"attempt": 1, "workId": work_id, "processId": 200, "elapsedMillis": 40000}}
        verify_recovery_proof(prepared, recovered, nonce)
        for name, value in (("workId", nonce), ("state", "ENQUEUED"), ("recoveredProcessId", 100),
                            ("preparedProcessId", 101), ("recoveredProcessId", True), ("probe", "other"),
                            ("schemaVersion", True)):
            with self.subTest(name=name), self.assertRaises(ValueError):
                verify_recovery_proof(prepared, dict(recovered, **{name: value}), nonce)
        for name, value in (("attempt", True), ("workId", nonce), ("processId", 100), ("processId", 200.0), ("elapsedMillis", 10001)):
            invalid = copy.deepcopy(recovered)
            invalid["second"][name] = value
            with self.subTest(name=name), self.assertRaises(ValueError):
                verify_recovery_proof(prepared, invalid, nonce)

    def test_command_snapshots_include_original_acknowledgement_schema_and_fixture(self):
        self.assertIn("contracts/events/event-ack-v1.schema.json", COMMAND_CONTRACT_INPUTS)
        self.assertIn("contracts/fixtures/event-acks-v1.json", COMMAND_CONTRACT_INPUTS)
        self.assertEqual(len(COMMAND_CONTRACT_INPUTS), len(set(COMMAND_CONTRACT_INPUTS)))

    def test_requires_exact_positive_instrumentation_count(self):
        self.assertTrue(instrumentation_passed("OK (2 tests)\nINSTRUMENTATION_CODE: -1\n", 2))
        self.assertFalse(instrumentation_passed("OK (1 test)\nINSTRUMENTATION_CODE: -1\n", 2))
        self.assertTrue(instrumentation_passed("OK (5 tests)\nINSTRUMENTATION_CODE: -1\n", 5))
        self.assertTrue(instrumentation_passed("OK (6 tests)\nINSTRUMENTATION_CODE: -1\n", 6))
        for count in (0, -1, True, "6", 1, 5, 7):
            with self.subTest(count=count):
                self.assertFalse(instrumentation_passed("OK (6 tests)\nINSTRUMENTATION_CODE: -1\n", count))
        self.assertFalse(instrumentation_passed("FAILURES\nOK (6 tests)\nINSTRUMENTATION_CODE: -1\n", 6))
        self.assertFalse(instrumentation_passed("OK (6 tests)\n", 6))

    def test_requires_explicit_test_pass_and_success_instrumentation_code(self):
        self.assertTrue(instrumentation_passed("OK (1 test)\n\nINSTRUMENTATION_CODE: -1\n"))
        for text in ("", "OK (0 tests)\nINSTRUMENTATION_CODE: -1", "OK (1 test)",
                     "FAILURES\nOK (1 test)\nINSTRUMENTATION_CODE: -1",
                     "INSTRUMENTATION_FAILED\nOK (1 test)\nINSTRUMENTATION_CODE: -1"):
            with self.subTest(text=text):
                self.assertFalse(instrumentation_passed(text))


if __name__ == "__main__":
    unittest.main()