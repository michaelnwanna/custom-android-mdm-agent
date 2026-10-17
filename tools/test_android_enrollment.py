import unittest

from check_android_enrollment import instrumentation_passed


class EnrollmentEvidenceTests(unittest.TestCase):
    def test_requires_exact_positive_instrumentation_count(self):
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