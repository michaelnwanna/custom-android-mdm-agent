import asyncio
import hashlib
import json
import logging
import tempfile
import unittest
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import AsyncMock

from amqtt.client import MQTTClient
from amqtt.contexts import Action
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import ExtendedKeyUsageOID, NameOID

from mqtt_device_broker import create_broker


class DeviceMqttWireTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.now = datetime.now(timezone.utc).replace(microsecond=0)
        self.clients = []
        self.device_root, self.device_key = self.root("device-root")
        self.server_root, self.server_key = self.root("server-root")
        self.device_root_file = self.write("device-root.pem", self.device_root.public_bytes(serialization.Encoding.PEM))
        self.server_root_file = self.write("server-root.pem", self.server_root.public_bytes(serialization.Encoding.PEM))
        self.server = self.leaf("localhost", self.server_root, self.server_key, server=True)
        self.first = self.leaf(str(uuid.uuid4()), self.device_root, self.device_key)
        self.second = self.leaf(str(uuid.uuid4()), self.device_root, self.device_key)
        self.memberships = self.directory / "members.json"
        self.document = {"schemaVersion": 1, "devices": {
            item[0]: {"certificateSha256": hashlib.sha256(item[3]).hexdigest(), "enabled": True}
            for item in (self.first, self.second)
        }}
        self.save_memberships()
        self.broker = create_broker(self.server[1], self.server[2], self.device_root_file, self.memberships, port=0)
        await self.broker.start()
        self.addAsyncCleanup(self.broker.shutdown)
        self.port = self.broker._servers["default"].instance.sockets[0].getsockname()[1]

    def write(self, name, content):
        filename = self.directory / name
        filename.touch(mode=0o600)
        filename.write_bytes(content)
        return filename

    def root(self, name):
        key = ec.generate_private_key(ec.SECP256R1())
        subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, name)])
        certificate = (x509.CertificateBuilder().subject_name(subject).issuer_name(subject).public_key(key.public_key())
                       .serial_number(x509.random_serial_number()).not_valid_before(self.now - timedelta(minutes=1))
                       .not_valid_after(self.now + timedelta(days=1))
                       .add_extension(x509.BasicConstraints(ca=True, path_length=0), critical=True)
                       .add_extension(x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False)
                       .add_extension(x509.AuthorityKeyIdentifier.from_issuer_public_key(key.public_key()), critical=False)
                       .add_extension(x509.KeyUsage(False, False, False, False, False, True, True, False, False), critical=True)
                       .sign(key, hashes.SHA256()))
        return certificate, key

    def leaf(self, identity, root, issuer_key, server=False):
        key = ec.generate_private_key(ec.SECP256R1())
        certificate = (x509.CertificateBuilder()
                       .subject_name(x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, identity)]))
                       .issuer_name(root.subject).public_key(key.public_key()).serial_number(x509.random_serial_number())
                       .not_valid_before(self.now - timedelta(seconds=30)).not_valid_after(self.now + timedelta(hours=1))
                       .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
                       .add_extension(x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False)
                       .add_extension(x509.AuthorityKeyIdentifier.from_issuer_public_key(issuer_key.public_key()), critical=False)
                       .add_extension(x509.KeyUsage(True, False, False, False, False, False, False, False, False), critical=True)
                       .add_extension(x509.SubjectAlternativeName([
                           x509.DNSName("localhost") if server else x509.UniformResourceIdentifier("urn:fleet:device:" + identity)
                       ]), critical=False)
                       .add_extension(x509.ExtendedKeyUsage([
                           ExtendedKeyUsageOID.SERVER_AUTH if server else ExtendedKeyUsageOID.CLIENT_AUTH
                       ]), critical=False).sign(issuer_key, hashes.SHA256()))
        filename = str(uuid.uuid4())
        certfile = self.write(filename + ".pem", certificate.public_bytes(serialization.Encoding.PEM))
        keyfile = self.write(filename + "-key.pem", key.private_bytes(
            serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
        return identity, certfile, keyfile, certificate.public_bytes(serialization.Encoding.DER)

    def save_memberships(self):
        self.memberships.write_text(json.dumps(self.document))

    def client(self, identity=None, client_id=None, server_root=None):
        config = {"auto_reconnect": False, "connection_timeout": 2, "plugins": {}, "connection": {}}
        if identity is not None:
            config["connection"] = {"certfile": str(identity[1]), "keyfile": str(identity[2])}
        client = MQTTClient(client_id=client_id or (identity[0] if identity else str(uuid.uuid4())), config=config)
        self.clients.append(client)
        self.addAsyncCleanup(client.disconnect)
        return client, str(server_root or self.server_root_file)

    async def connect(self, identity=None, client_id=None, server_root=None, clean=True):
        client, root = self.client(identity, client_id, server_root)
        async with asyncio.timeout(4):
            await client.connect("mqtts://localhost:" + str(self.port), cafile=root, cleansession=clean)
        return client

    async def test_real_mtls_directional_subscriptions_and_cross_device_isolation(self):
        first = await self.connect(self.first)
        second = await self.connect(self.second)
        first_commands = "fleet/devices/" + self.first[0] + "/commands"
        second_commands = "fleet/devices/" + self.second[0] + "/commands"
        self.assertEqual([1, 1], await first.subscribe([(first_commands, 1), (first_commands.replace("commands", "event-acks"), 1)]))
        for topic in ("#", "fleet/#", "fleet/devices/+/commands", second_commands,
                      first_commands.replace("commands", "command-events")):
            self.assertEqual([128], await first.subscribe([(topic, 1)]), topic)
        self.assertEqual([1], await second.subscribe([(second_commands, 1)]))
        await self.broker.internal_message_broadcast(first_commands, b"first-only", qos=1)
        async with asyncio.timeout(2):
            message = await first.deliver_message()
        self.assertEqual(b"first-only", bytes(message.data))
        with self.assertRaises(TimeoutError):
            async with asyncio.timeout(0.2):
                await second.deliver_message()

    async def test_missing_wrong_unregistered_and_forged_credentials_rejected_before_session_mutation(self):
        first = await self.connect(self.first)
        topic = "fleet/devices/" + self.first[0] + "/commands"
        self.assertEqual([1], await first.subscribe([(topic, 1)]))
        wrong_root, wrong_key = self.root("untrusted-device-root")
        wrong = self.leaf(str(uuid.uuid4()), wrong_root, wrong_key)
        alternate = self.leaf(self.first[0], self.device_root, self.device_key)
        for identity, claimed_id, root in ((None, None, None), (wrong, None, None),
                                           (alternate, None, None), (self.second, self.first[0], None),
                                           (self.second, None, self.device_root_file)):
            with self.subTest(identity=None if identity is None else identity[0], claimed=claimed_id, root=root):
                with self.assertRaises(Exception):
                    await self.connect(identity, claimed_id, root)
        await self.broker.internal_message_broadcast(topic, b"still-subscribed", qos=1)
        async with asyncio.timeout(2):
            message = await first.deliver_message()
        self.assertEqual(b"still-subscribed", bytes(message.data))

    async def test_disable_blocks_new_subscriptions_receive_and_reconnect(self):
        client = await self.connect(self.first)
        topic = "fleet/devices/" + self.first[0] + "/commands"
        self.assertEqual([1], await client.subscribe([(topic, 1)]))
        self.document["devices"][self.first[0]]["enabled"] = False
        self.save_memberships()
        self.assertEqual([128], await client.subscribe([(topic, 1)]))
        await self.broker.internal_message_broadcast(topic, b"must-not-arrive", qos=1)
        with self.assertRaises(TimeoutError):
            async with asyncio.timeout(0.2):
                await client.deliver_message()
        with self.assertRaises(Exception):
            await self.connect(self.first)

    async def test_publish_direction_and_disable_enforced_before_broadcast(self):
        client = await self.connect(self.first)
        publish_checks = asyncio.Queue()
        filtering = self.broker._topic_filtering

        async def observe(session, topic, action):
            permitted = await filtering(session, topic, action)
            if action == Action.PUBLISH:
                publish_checks.put_nowait((topic, permitted))
            return permitted

        self.broker._topic_filtering = observe
        self.broker._broadcast_message = AsyncMock(wraps=self.broker._broadcast_message)
        prefix = "fleet/devices/" + self.first[0] + "/"
        for topic, permitted in ((prefix + "command-events", True), (prefix + "commands", False),
                                 ("fleet/devices/" + self.second[0] + "/command-events", False)):
            await client.publish(topic, b"opaque-test-payload", qos=1)
            async with asyncio.timeout(2):
                self.assertEqual((topic, permitted), await publish_checks.get())
        self.assertEqual(1, self.broker._broadcast_message.await_count)
        self.document["devices"][self.first[0]]["enabled"] = False
        self.save_memberships()
        await client.publish(prefix + "command-events", b"disabled", qos=1)
        async with asyncio.timeout(2):
            self.assertEqual((prefix + "command-events", False), await publish_checks.get())
        self.assertEqual(1, self.broker._broadcast_message.await_count)

    async def test_persistent_sessions_are_rejected(self):
        with self.assertRaises(Exception):
            await self.connect(self.first, clean=False)


if __name__ == "__main__":
    logging.basicConfig(level=logging.CRITICAL)
    unittest.main()