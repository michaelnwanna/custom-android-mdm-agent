import argparse
import asyncio
import json
import signal
import ssl
from dataclasses import dataclass
from pathlib import Path

from amqtt.broker import Broker
from amqtt.errors import AMQTTError
from amqtt.mqtt.protocol.broker_handler import BrokerProtocolHandler
from amqtt.plugins.base import BaseAuthPlugin, BaseTopicPlugin

from mqtt_device_policy import DeviceMembership, authenticate_certificate, authorize_topic, canonical_device_id


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate membership field")
        result[key] = value
    return result


def read_memberships(filename):
    with Path(filename).open("rb") as stream:
        encoded = stream.read(65537)
    if len(encoded) > 65536:
        raise ValueError("Membership file too large")
    document = json.loads(encoded, object_pairs_hook=unique_object)
    if set(document) != {"schemaVersion", "devices"} or type(document["schemaVersion"]) is not int or document["schemaVersion"] != 1:
        raise ValueError("Invalid membership document")
    if not isinstance(document["devices"], dict) or len(document["devices"]) > 128:
        raise ValueError("Invalid device membership")
    result = {}
    for device_id, member in document["devices"].items():
        canonical_device_id(device_id)
        if not isinstance(member, dict) or set(member) != {"certificateSha256", "enabled"}:
            raise ValueError("Invalid membership fields")
        digest = member["certificateSha256"]
        if not isinstance(digest, str) or len(digest) != 64 or any(character not in "0123456789abcdef" for character in digest):
            raise ValueError("Invalid certificate fingerprint")
        if type(member["enabled"]) is not bool:
            raise ValueError("Invalid enabled state")
        result[device_id] = DeviceMembership(digest, member["enabled"])
    return result


class DeviceAccessPlugin(BaseAuthPlugin, BaseTopicPlugin):
    @dataclass
    class Config:
        membership_file: str

    def identity(self, session):
        if session is None or session.ssl_object is None or session.clean_session is not True:
            return None
        if session.will_flag or session.username is not None or session.password is not None:
            return None
        try:
            certificate = session.ssl_object.getpeercert(binary_form=True)
            return authenticate_certificate(certificate, session.client_id, read_memberships(self.config.membership_file))
        except (OSError, ValueError, TypeError, AttributeError):
            return None

    async def authenticate(self, *, session):
        return self.identity(session) is not None

    async def topic_filtering(self, *, session=None, topic=None, action=None):
        identity = self.identity(session)
        return identity is not None and authorize_topic(identity, topic, action)


class DeviceBroker(Broker):
    @staticmethod
    def _create_ssl_context(listener):
        context = Broker._create_ssl_context(listener)
        context.verify_mode = ssl.CERT_REQUIRED
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        return context

    async def _initialize_client_session(self, reader, writer, remote_address, remote_port):
        try:
            async with asyncio.timeout(10):
                handler, session = await BrokerProtocolHandler.init_from_connect(reader, writer, self.plugins_manager)
            if not await self._authenticate(session, self.listeners_config["default"]):
                raise AMQTTError("Device credentials rejected")
            await self._cleanup_session(session.client_id)
            session.parent = 0
            return handler, session
        except TimeoutError as error:
            await writer.close()
            raise AMQTTError("Device CONNECT timed out") from error
        except BaseException:
            await writer.close()
            raise

    async def retain_message(self, source_session, topic_name, data, qos=None):
        return None

    async def _publish_retained_messages_for_subscription(self, subscription, session):
        return None


def create_broker(server_certificate, server_key, device_root, membership_file, port=8884):
    read_memberships(membership_file)
    return DeviceBroker({
        "listeners": {"default": {
            "type": "tcp", "bind": "127.0.0.1:" + str(port), "ssl": True,
            "certfile": str(server_certificate), "keyfile": str(server_key),
            "cafile": str(device_root), "max_connections": 8,
        }},
        "plugins": {"mqtt_device_broker.DeviceAccessPlugin": {"membership_file": str(membership_file)}},
        "session_expiry_interval": 0,
    })


async def serve(arguments):
    broker = create_broker(arguments.server_certificate, arguments.server_key, arguments.device_root,
                           arguments.memberships, arguments.port)
    await broker.start()
    stopped = asyncio.Event()
    for name in (signal.SIGINT, signal.SIGTERM):
        asyncio.get_running_loop().add_signal_handler(name, stopped.set)
    print("DEVICE MQTT LAB READY: loopback mTLS; explicit lab memberships only", flush=True)
    try:
        await stopped.wait()
    finally:
        await broker.shutdown()


def main():
    parser = argparse.ArgumentParser(description="Isolated MQTT mTLS/ACL lab; not backend-connected fleet infrastructure")
    parser.add_argument("--server-certificate", type=Path, required=True)
    parser.add_argument("--server-key", type=Path, required=True)
    parser.add_argument("--device-root", type=Path, required=True)
    parser.add_argument("--memberships", type=Path, required=True)
    parser.add_argument("--port", type=int, default=8884)
    arguments = parser.parse_args()
    if not 1 <= arguments.port <= 65535:
        parser.error("Invalid port")
    asyncio.run(serve(arguments))


if __name__ == "__main__":
    main()