import hashlib
import json
import tempfile
import unittest
import uuid
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock

from mqtt_android_probe import attach_probe


class AndroidMqttProbeTests(unittest.IsolatedAsyncioTestCase):
    async def test_records_certificate_and_denials_and_echoes_only_the_exact_probe(self):
        device_id = str(uuid.uuid4())
        probe = "probe-" + str(uuid.uuid4())
        with tempfile.TemporaryDirectory() as temporary:
            filename = Path(temporary) / "members.json"
            filename.write_text(json.dumps({"schemaVersion": 1, "devices": {
                device_id: {"certificateSha256": "0" * 64, "enabled": False}}}))
            authentication = AsyncMock(return_value=True)
            filtering = AsyncMock(return_value=False)
            broadcast = AsyncMock()
            broker = SimpleNamespace(_authenticate=authentication, _topic_filtering=filtering, _broadcast_message=broadcast)
            session = SimpleNamespace(client_id=device_id, ssl_object=SimpleNamespace(getpeercert=lambda **kwargs: b"leaf"))
            state = attach_probe(broker, filename, probe)
            self.assertTrue(await broker._authenticate(session, {}))
            self.assertEqual({device_id: hashlib.sha256(b"leaf").hexdigest()}, state["identities"])
            authentication.return_value = False
            self.assertFalse(await broker._authenticate(session, {}))
            await broker._topic_filtering(session, "fleet/devices/" + device_id + "/commands", "subscribe")
            await broker._topic_filtering(session, "fleet/devices/" + str(uuid.uuid4()) + "/commands", "subscribe")
            await broker._topic_filtering(session, "fleet/#", "subscribe")
            self.assertEqual(1, state["disabledConnectionsRejected"])
            self.assertEqual(1, state["crossDeviceDenied"])
            self.assertEqual(1, state["wildcardDenied"])
            self.assertEqual(3, state["disabledSubscribeDenied"])
            topic = "fleet/devices/" + device_id + "/command-events"
            await broker._broadcast_message(session, topic, b"wrong-probe")
            self.assertEqual(0, state["probeRoundtrips"])
            await broker._broadcast_message(session, topic, probe.encode())
            self.assertEqual(1, state["probeRoundtrips"])
            broadcast.assert_awaited_with(None, "fleet/devices/" + device_id + "/commands", probe.encode(), 1)


if __name__ == "__main__":
    unittest.main()