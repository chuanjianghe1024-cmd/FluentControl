"""Trusted optional adapter workers; not the FCS user-script sandbox."""
import datetime as dt
import json
import math
from pathlib import Path
import re
import socket
import struct
import time
import uuid


def load(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate configuration key")
            result[key] = value
        return result
    raw = Path(path).read_bytes()
    if len(raw) > 65536:
        raise ValueError("Configuration exceeds 64 KiB")
    return json.loads(raw, object_pairs_hook=unique)


def fields(obj, required, optional=()):
    if not isinstance(obj, dict) or not set(required) <= obj.keys() or obj.keys() - set(required) - set(optional):
        raise ValueError("Missing or unknown configuration field")


def identity(item, adapter_id):
    for key in ("deviceId", "sensorId", "signalId"):
        if not isinstance(item[key], str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", item[key]):
            raise ValueError("Invalid resource ID")
    if not item["deviceId"].startswith("ext-" + adapter_id + "-"):
        raise ValueError("Device outside adapter namespace")
    if type(item["staleMilliseconds"]) is not int or not 500 <= item["staleMilliseconds"] <= 60000:
        raise ValueError("Invalid signal lifetime")
    return tuple(item[key] for key in ("deviceId", "sensorId", "signalId"))


def utc(timestamp=None):
    return dt.datetime.fromtimestamp(time.time() if timestamp is None else timestamp, dt.timezone.utc).isoformat()


def receive_exact(stream, count):
    data = bytearray()
    while len(data) < count:
        part = stream.recv(count - len(data))
        if not part:
            raise OSError("Adapter endpoint disconnected")
        data.extend(part)
    return data


class Publisher:
    """Bounded last-value buffer. Only new samples are sent; never refresh on retry."""
    def __init__(self, config):
        self.adapter_id = config["adapterId"]
        if not re.fullmatch(r"[A-Za-z0-9_-]{1,32}", self.adapter_id):
            raise ValueError("Invalid adapter ID")
        endpoint = config["endpoint"]
        fields(endpoint, ("port", "tokenFile"))
        self.port = endpoint["port"]
        if type(self.port) is not int or not 1 <= self.port <= 65535:
            raise ValueError("Invalid loopback port")
        self.token = Path(endpoint["tokenFile"]).read_text().strip()
        if not re.fullmatch(r"[0-9a-fA-F]{64}", self.token):
            raise ValueError("Invalid producer token file")
        self.boot = uuid.uuid4().hex
        self.sequence = 0
        self.next_send = 0.0
        self.pending = {}
        self.counters = {}
        self.lifetimes = {}
        if not 1 <= len(config["inputs"]) <= 64:
            raise ValueError("Expected 1..64 inputs")
        for item in config["inputs"]:
            key = identity(item, self.adapter_id)
            if key in self.lifetimes:
                raise ValueError("Duplicate input resource")
            self.lifetimes[key] = item["staleMilliseconds"] / 1000

    def put(self, item, value, received=None):
        key = tuple(item[k] for k in ("deviceId", "sensorId", "signalId"))
        if key not in self.lifetimes:
            raise ValueError("Unknown input")
        received = time.time() if received is None else received
        age = max(0, time.time() - received)
        if age > self.lifetimes[key]:
            return
        valid = (type(value) in (bool, int, float, str) and
                 (not isinstance(value, float) or math.isfinite(value)) and
                 (not isinstance(value, str) or len(value) <= 4096))
        sequence = self.counters.get(key, -1) + 1
        self.counters[key] = sequence
        self.pending[key] = (time.monotonic() - age, dict(zip(("deviceId", "sensorId", "signalId"), key),
            sequence=sequence, receivedAtUtc=utc(received), value=value if valid else None, valid=valid))

    def flush(self):
        now = time.monotonic()
        if now < self.next_send:
            return None
        self.next_send = now + .1
        self.pending = {key: value for key, value in self.pending.items() if now - value[0] <= self.lifetimes[key]}
        if not self.pending:
            return None
        # Split large string batches without unbounded queues or oversized frames.
        batch = []
        frame = dict(protocol="fcs-adapter/1", adapterId=self.adapter_id, token=self.token,
                     bootId=self.boot, sequence=self.sequence, data=batch)
        keys = []
        for key, (_, value) in self.pending.items():
            batch.append(value)
            if len(json.dumps(frame, separators=(",", ":")).encode()) > 32768:
                batch.pop()
                break
            keys.append(key)
        payload = json.dumps(frame, separators=(",", ":"), allow_nan=False).encode()
        self.sequence += 1
        try:
            with socket.create_connection(("127.0.0.1", self.port), timeout=1) as stream:
                stream.sendall(struct.pack("!I", len(payload)) + payload)
                size, = struct.unpack("!I", receive_exact(stream, 4))
                if not 0 < size <= 1024:
                    raise OSError("Invalid adapter reply")
                ok = json.loads(receive_exact(stream, size))["ok"] is True
            if ok:
                for key in keys:
                    self.pending.pop(key)
            return ok
        except (OSError, ValueError, KeyError):
            return False  # Preserve original receipt times; old samples expire.
