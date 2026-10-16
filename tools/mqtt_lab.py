#!/usr/bin/env python3
import argparse
import asyncio
import json
from pathlib import Path
import signal
import subprocess
import uuid


ROOT = Path(__file__).resolve().parents[1]
PRIVATE = ROOT / ".lab-private" / "mqtt"


def prepare():
    PRIVATE.mkdir(parents=True, exist_ok=True, mode=0o700)
    certificate = PRIVATE / "server.pem"
    key = PRIVATE / "server-key.pem"
    if not certificate.exists() or not key.exists():
        subprocess.run(["openssl", "req", "-x509", "-newkey", "ec", "-pkeyopt", "ec_paramgen_curve:P-256",
                        "-nodes", "-keyout", str(key), "-out", str(certificate), "-days", "7",
                        "-subj", "/CN=localhost", "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1",
                        "-addext", "basicConstraints=critical,CA:TRUE"], check=True, capture_output=True)
        key.chmod(0o600)
    print(json.dumps({"certificate": str(certificate), "listener": "127.0.0.1:8883", "labOnly": True}))


async def serve():
    from amqtt.broker import Broker
    broker = Broker({
        "listeners": {"default": {"type": "tcp", "bind": "127.0.0.1:8883", "ssl": True,
                                  "certfile": str(PRIVATE / "server.pem"),
                                  "keyfile": str(PRIVATE / "server-key.pem"), "max_connections": 8}},
        "plugins": {"amqtt.plugins.authentication.AnonymousAuthPlugin": {"allow_anonymous": True}},
        "session_expiry_interval": 300
    })
    await broker.start()
    print("LAB BROKER READY: TLS 127.0.0.1:8883; anonymous synthetic probes only", flush=True)
    stopped = asyncio.Event()
    for name in (signal.SIGINT, signal.SIGTERM):
        asyncio.get_running_loop().add_signal_handler(name, stopped.set)
    try:
        await stopped.wait()
    finally:
        await broker.shutdown()


async def probe():
    from amqtt.client import MQTTClient
    client = MQTTClient(config={"auto_reconnect": False})
    token = "probe-" + str(uuid.uuid4())
    await client.connect("mqtts://localhost:8883", cafile=str(PRIVATE / "server.pem"))
    try:
        await client.subscribe([("fleet/lab/ack", 1)])
        await client.publish("fleet/lab/probe", token.encode(), qos=1)
        async with asyncio.timeout(30):
            while True:
                message = await client.deliver_message()
                if bytes(message.data).decode() == token:
                    print(json.dumps({"result": "passed", "probe": token, "acknowledgedOverTls": True}))
                    return
    finally:
        await client.disconnect()


def main():
    parser = argparse.ArgumentParser(description="Loopback-only synthetic MQTT lifecycle lab, not fleet infrastructure")
    parser.add_argument("mode", choices=("prepare", "serve", "probe"))
    arguments = parser.parse_args()
    if arguments.mode == "prepare":
        prepare()
    else:
        asyncio.run(serve() if arguments.mode == "serve" else probe())


if __name__ == "__main__":
    main()