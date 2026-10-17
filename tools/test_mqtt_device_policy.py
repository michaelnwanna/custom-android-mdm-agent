import hashlib
import unittest
import uuid
from datetime import datetime, timedelta, timezone

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import ExtendedKeyUsageOID, NameOID

from mqtt_device_policy import DeviceMembership, authenticate_certificate, authorize_topic


class DeviceMqttPolicyTests(unittest.TestCase):
    def setUp(self):
        self.device_id = str(uuid.uuid4())
        self.now = datetime.now(timezone.utc).replace(microsecond=0)
        self.key = ec.generate_private_key(ec.SECP256R1())

    def certificate(self, *, extra_san=False, server=False, expired=False, ca=False):
        subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "test")])
        names = [x509.UniformResourceIdentifier("urn:fleet:device:" + self.device_id)]
        if extra_san:
            names.append(x509.DNSName("untrusted.example"))
        start = self.now - timedelta(days=2) if expired else self.now - timedelta(seconds=30)
        end = self.now if expired else self.now + timedelta(hours=1)
        certificate = (x509.CertificateBuilder().subject_name(subject).issuer_name(subject)
                       .public_key(self.key.public_key()).serial_number(x509.random_serial_number())
                       .not_valid_before(start).not_valid_after(end)
                       .add_extension(x509.BasicConstraints(ca=ca, path_length=None), critical=True)
                       .add_extension(x509.SubjectAlternativeName(names), critical=False)
                       .add_extension(x509.KeyUsage(True, False, False, False, False, False, False, False, False), critical=True)
                       .add_extension(x509.ExtendedKeyUsage([
                           ExtendedKeyUsageOID.SERVER_AUTH if server else ExtendedKeyUsageOID.CLIENT_AUTH]), critical=False)
                       .sign(self.key, hashes.SHA256()))
        return certificate.public_bytes(serialization.Encoding.DER)

    def membership(self, certificate, enabled=True):
        return {self.device_id: DeviceMembership(hashlib.sha256(certificate).hexdigest(), enabled)}

    def test_exact_registered_certificate_and_client_id_required(self):
        certificate = self.certificate()
        members = self.membership(certificate)
        self.assertEqual(self.device_id, authenticate_certificate(certificate, self.device_id, members, self.now))
        for identity in (str(uuid.uuid4()), self.device_id.upper(), str(uuid.UUID(int=0)), "", None):
            with self.subTest(identity=identity):
                self.assertIsNone(authenticate_certificate(certificate, identity, members, self.now))
        self.assertIsNone(authenticate_certificate(self.certificate(), self.device_id, members, self.now))
        self.assertIsNone(authenticate_certificate(certificate, self.device_id, self.membership(certificate, False), self.now))
        self.assertIsNone(authenticate_certificate(b"invalid", self.device_id, self.membership(b"invalid"), self.now))

    def test_registered_certificate_still_requires_strict_leaf_policy(self):
        for options in ({"extra_san": True}, {"server": True}, {"expired": True}, {"ca": True}):
            with self.subTest(options=options):
                certificate = self.certificate(**options)
                self.assertIsNone(authenticate_certificate(certificate, self.device_id, self.membership(certificate), self.now))

    def test_directional_topics_deny_other_devices_wildcards_and_unknown_actions(self):
        prefix = "fleet/devices/" + self.device_id + "/"
        self.assertTrue(authorize_topic(self.device_id, prefix + "command-events", "publish"))
        for action in ("subscribe", "receive"):
            for suffix in ("commands", "event-acks"):
                self.assertTrue(authorize_topic(self.device_id, prefix + suffix, action))
        for action in ("publish", "subscribe", "receive", "unknown", None):
            for topic in ("#", "fleet/#", prefix + "+", prefix + "#", "$SYS/broker/uptime",
                          "fleet/devices/" + str(uuid.uuid4()) + "/commands", prefix + "commands/extra"):
                self.assertFalse(authorize_topic(self.device_id, topic, action))
        self.assertFalse(authorize_topic(self.device_id, prefix + "commands", "publish"))
        self.assertFalse(authorize_topic(self.device_id, prefix + "command-events", "subscribe"))
        self.assertFalse(authorize_topic(None, prefix + "commands", "subscribe"))


if __name__ == "__main__":
    unittest.main()