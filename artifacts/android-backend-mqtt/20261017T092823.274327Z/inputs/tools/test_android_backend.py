import json
import base64
import hashlib
import unittest

from check_android_backend import public_configuration, public_tls_diagnostics, verify_mqtt_proof


class AndroidBackendEvidenceTests(unittest.TestCase):
    def test_mqtt_proof_requires_exact_certificate_and_independent_rejections(self):
        device = {"deviceId": "device-id", "mqttAuthenticated": True, "mqttDisabledRejected": True,
                  "responseBase64": base64.b64encode(json.dumps({"certificateDer": base64.b64encode(b"leaf").decode()}).encode()).decode()}
        broker = {"stage": "mqtt-verified", "stopped": True, "probeRoundtrips": 1,
                  "identities": {"device-id": hashlib.sha256(b"leaf").hexdigest()},
                  "crossDeviceDenied": 1, "wildcardDenied": 1, "disabledSubscribeDenied": 1,
                  "disabledConnectionsRejected": 1}
        verify_mqtt_proof(device, broker)
        for key, value in (("stopped", False), ("identities", {"other": "0" * 64}), ("probeRoundtrips", 0),
                           ("crossDeviceDenied", 0), ("wildcardDenied", True), ("disabledSubscribeDenied", 0),
                           ("disabledConnectionsRejected", 0)):
            with self.subTest(key=key):
                with self.assertRaises(ValueError):
                    verify_mqtt_proof(device, dict(broker, **{key: value}))

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


if __name__ == "__main__":
    unittest.main()