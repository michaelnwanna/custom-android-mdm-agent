import json
import unittest

from check_android_backend import public_configuration


class AndroidBackendEvidenceTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()