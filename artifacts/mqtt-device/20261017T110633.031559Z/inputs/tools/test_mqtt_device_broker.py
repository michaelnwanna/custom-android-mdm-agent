import hashlib
import json
import logging
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

from amqtt.contexts import Action

from mqtt_device_broker import DeviceAccessPlugin, read_memberships
import test_mqtt_device_policy


class DeviceBrokerPluginTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.fixture = test_mqtt_device_policy.DeviceMqttPolicyTests()
        self.fixture.setUp()
        self.certificate = self.fixture.certificate()
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.filename = Path(self.temporary.name) / "members.json"
        self.document = {"schemaVersion": 1, "devices": {self.fixture.device_id: {
            "certificateSha256": hashlib.sha256(self.certificate).hexdigest(), "enabled": True,
        }}}
        self.save()
        context = SimpleNamespace(config=DeviceAccessPlugin.Config(str(self.filename)), logger=logging.getLogger("test"))
        self.plugin = DeviceAccessPlugin(context)
        self.session = SimpleNamespace(client_id=self.fixture.device_id, clean_session=True, will_flag=False,
                                       username=None, password=None,
                                       ssl_object=SimpleNamespace(getpeercert=lambda **kwargs: self.certificate))

    def save(self):
        self.filename.write_text(json.dumps(self.document))

    async def test_fresh_membership_for_authentication_and_every_topic_action(self):
        topic = "fleet/devices/" + self.fixture.device_id + "/commands"
        self.assertTrue(await self.plugin.authenticate(session=self.session))
        self.assertTrue(await self.plugin.topic_filtering(session=self.session, topic=topic, action=Action.SUBSCRIBE))
        self.document["devices"][self.fixture.device_id]["enabled"] = False
        self.save()
        self.assertFalse(await self.plugin.authenticate(session=self.session))
        for action in Action:
            self.assertFalse(await self.plugin.topic_filtering(session=self.session, topic=topic, action=action))

    async def test_missing_or_malformed_membership_fails_closed(self):
        for encoded in ('{"schemaVersion":1,"schemaVersion":1,"devices":{}}', '[]', 'null', '{',
                        '{"schemaVersion":true,"devices":{}}', 'x' * 65537):
            with self.subTest(encoded=encoded[:50]):
                self.filename.write_text(encoded)
                self.assertFalse(await self.plugin.authenticate(session=self.session))
        self.filename.unlink()
        self.assertFalse(await self.plugin.authenticate(session=self.session))

    async def test_rejects_non_tls_credentials_wills_and_persistent_sessions(self):
        for name, value in (("ssl_object", None), ("will_flag", True), ("username", "admin"),
                            ("password", "token"), ("clean_session", False), ("client_id", "forged")):
            with self.subTest(name=name):
                previous = getattr(self.session, name)
                setattr(self.session, name, value)
                self.assertFalse(await self.plugin.authenticate(session=self.session))
                setattr(self.session, name, previous)

    def test_registry_has_strict_field_and_value_types(self):
        for field, value in (("enabled", "true"), ("certificateSha256", "0" * 63),
                             ("certificateSha256", "A" * 64), ("unexpected", True)):
            with self.subTest(field=field, value=value):
                original = self.document["devices"][self.fixture.device_id].copy()
                self.document["devices"][self.fixture.device_id][field] = value
                self.save()
                with self.assertRaises(ValueError):
                    read_memberships(self.filename)
                self.document["devices"][self.fixture.device_id] = original


if __name__ == "__main__":
    unittest.main()