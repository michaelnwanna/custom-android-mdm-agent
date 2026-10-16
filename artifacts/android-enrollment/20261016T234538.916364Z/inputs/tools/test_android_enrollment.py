import unittest

from check_android_enrollment import instrumentation_passed


class EnrollmentEvidenceTests(unittest.TestCase):
    def test_requires_explicit_test_pass_and_success_instrumentation_code(self):
        self.assertTrue(instrumentation_passed("OK (1 test)\n\nINSTRUMENTATION_CODE: -1\n"))
        for text in ("", "OK (0 tests)\nINSTRUMENTATION_CODE: -1", "OK (1 test)",
                     "FAILURES\nOK (1 test)\nINSTRUMENTATION_CODE: -1",
                     "INSTRUMENTATION_FAILED\nOK (1 test)\nINSTRUMENTATION_CODE: -1"):
            with self.subTest(text=text):
                self.assertFalse(instrumentation_passed(text))


if __name__ == "__main__":
    unittest.main()