import asyncio
import hashlib
import json
import logging
from pathlib import Path
import re
import sys

from mqtt_device_broker import create_broker, read_memberships


def attach_probe(broker, membership_file, probe):
    state = {"schemaVersion": 1, "stage": "mqtt-verified", "identities": {}, "probeRoundtrips": 0,
             "crossDeviceDenied": 0, "wildcardDenied": 0, "disabledSubscribeDenied": 0,
             "disabledConnectionsRejected": 0}
    authenticate = broker._authenticate
    filtering = broker._topic_filtering
    broadcast = broker._broadcast_message

    async def authenticated(session, listener):
        accepted = await authenticate(session, listener)
        if accepted:
            state["identities"][session.client_id] = hashlib.sha256(
                session.ssl_object.getpeercert(binary_form=True)).hexdigest()
        else:
            member = read_memberships(membership_file).get(session.client_id)
            if member is not None and not member.enabled:
                state["disabledConnectionsRejected"] += 1
        return accepted

    async def authorized(session, topic, action):
        accepted = await filtering(session, topic, action)
        if not accepted:
            member = read_memberships(membership_file).get(session.client_id)
            if action == "subscribe" and member is not None and not member.enabled:
                state["disabledSubscribeDenied"] += 1
            if "#" in topic or "+" in topic:
                state["wildcardDenied"] += 1
            elif topic.startswith("fleet/devices/") and not topic.startswith("fleet/devices/" + session.client_id + "/"):
                state["crossDeviceDenied"] += 1
        return accepted

    async def echo(session, topic, data, force_qos=None):
        await broadcast(session, topic, data, force_qos)
        if (session is not None and topic == "fleet/devices/" + session.client_id + "/command-events"
                and bytes(data) == probe.encode()):
            state["probeRoundtrips"] += 1
            await broadcast(None, "fleet/devices/" + session.client_id + "/commands", data, 1)

    broker._authenticate = authenticated
    broker._topic_filtering = authorized
    broker._broadcast_message = echo
    return state


async def run():
    configuration = json.loads(sys.stdin.readline())
    directory = Path(configuration["directory"])
    probe = configuration["probe"]
    if not directory.is_absolute() or not re.fullmatch(r"probe-[0-9a-f-]{36}", probe):
        raise ValueError("Invalid private probe configuration")
    broker = create_broker(directory / "server.pem", directory / "server-key.pem", directory / "device-root.pem",
                           directory / "members.json", port=0)
    state = attach_probe(broker, directory / "members.json", probe)
    await broker.start()
    try:
        port = broker._servers["default"].instance.sockets[0].getsockname()[1]
        print(json.dumps({"schemaVersion": 1, "stage": "mqtt-ready", "port": port}), flush=True)
        command = await asyncio.wait_for(asyncio.to_thread(sys.stdin.readline), timeout=240)
        if command.strip() != "stop":
            raise ValueError("Explicit probe shutdown required")
    finally:
        await broker.shutdown()
    state["stopped"] = True
    print(json.dumps(state), flush=True)


if __name__ == "__main__":
    logging.basicConfig(level=logging.CRITICAL)
    try:
        asyncio.run(run())
    except Exception:
        print("MQTT probe failed; private configuration redacted.", file=sys.stderr)
        sys.exit(1)