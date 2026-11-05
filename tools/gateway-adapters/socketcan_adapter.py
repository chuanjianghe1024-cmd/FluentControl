"""Read-only Linux SocketCAN producer. Hardware interfaces MUST be listen-only."""
import argparse
import json
import math
import re
import select
import socket
import struct
import subprocess
import time
from common import Publisher, fields, load

EFF, RTR, ERR = 0x80000000, 0x40000000, 0x20000000


def validate(config):
    fields(config, ("adapterId", "endpoint", "interface", "inputs"), ("fd",))
    if not re.fullmatch(r"[A-Za-z0-9_-]{1,15}", config["interface"]) or type(config.get("fd", False)) is not bool:
        raise ValueError("Invalid CAN interface/options")
    for item in config["inputs"]:
        fields(item, ("deviceId", "sensorId", "signalId", "staleMilliseconds", "canId", "extended",
                      "offset", "length", "byteOrder", "signed", "factor", "bias"))
        if type(item["extended"]) is not bool or type(item["signed"]) is not bool:
            raise ValueError("CAN flags must be boolean")
        if type(item["canId"]) is not int or not 0 <= item["canId"] <= (0x1fffffff if item["extended"] else 0x7ff):
            raise ValueError("Invalid arbitration ID")
        if (type(item["offset"]) is not int or type(item["length"]) is not int or
                not 1 <= item["length"] <= 8 or item["offset"] < 0 or
                item["offset"] + item["length"] > (64 if config.get("fd") else 8) or
                item["byteOrder"] not in ("little", "big") or
                any(type(item[k]) not in (int, float) or not math.isfinite(item[k]) for k in ("factor", "bias"))):
            raise ValueError("Invalid byte-aligned CAN mapping")


def check_interface(name, fd=False):
    result = subprocess.run(["ip", "-details", "-json", "link", "show", "dev", name],
                            check=True, capture_output=True, text=True, timeout=3)
    interfaces = json.loads(result.stdout)
    if len(interfaces) != 1 or "UP" not in interfaces[0].get("flags", []):
        raise ValueError("CAN interface must already be UP")
    info = interfaces[0].get("linkinfo", {})
    if info.get("info_kind") == "vcan":
        return
    data = info.get("info_data", {})
    modes = data.get("ctrlmode", [])
    if info.get("info_kind") != "can" or "LISTEN-ONLY" not in modes:
        raise ValueError("Physical CAN requires verified listen-only mode")
    if fd and "FD" not in modes:
        raise ValueError("CAN FD is not enabled on this interface")


def decode(packet, item):
    if len(packet) not in (16, 72):
        return None
    identifier, length = struct.unpack_from("=IB", packet)
    if identifier & (RTR | ERR) or bool(identifier & EFF) != item["extended"]:
        return None
    if identifier & 0x1fffffff != item["canId"] or length > (8 if len(packet) == 16 else 64):
        return None
    start, size = item["offset"], item["length"]
    if start + size > length:
        return None
    raw = int.from_bytes(packet[8 + start:8 + start + size], item["byteOrder"], signed=item["signed"])
    return raw * item["factor"] + item["bias"]


def run(config):
    validate(config)
    publisher = Publisher(config)
    interface = config["interface"]
    check_interface(interface, config.get("fd", False))
    with socket.socket(socket.AF_CAN, socket.SOCK_RAW, socket.CAN_RAW) as bus:
        # Capture kernel receive time so queued frames cannot acquire a fresh timestamp.
        timestamp_option = getattr(socket, "SO_TIMESTAMPNS", 35)  # Linux x64/arm64
        bus.setsockopt(socket.SOL_SOCKET, timestamp_option, 1)
        bus.setsockopt(socket.SOL_CAN_RAW, socket.CAN_RAW_RECV_OWN_MSGS, 0)
        if config.get("fd", False):
            bus.setsockopt(socket.SOL_CAN_RAW, socket.CAN_RAW_FD_FRAMES, 1)
        # ERR is deliberately NOT in this mask: Linux uses it to select the
        # error-filter list, not as an ordinary data-frame discriminator.
        filters = {(i["canId"] | (EFF if i["extended"] else 0), 0x1fffffff | EFF | RTR) for i in config["inputs"]}
        bus.setsockopt(socket.SOL_CAN_RAW, socket.CAN_RAW_FILTER, b"".join(struct.pack("=II", *f) for f in filters))
        bus.bind((interface,))
        recheck = time.monotonic() + 1
        while True:
            if time.monotonic() >= recheck:
                check_interface(interface, config.get("fd", False))
                recheck = time.monotonic() + 1
            if select.select([bus], [], [], .05)[0]:
                packet, ancillary, flags, _ = bus.recvmsg(72, 128)
                if flags & (socket.MSG_TRUNC | socket.MSG_CTRUNC):
                    continue
                stamp = next((struct.unpack("@ll", data[:struct.calcsize("@ll")])
                              for level, kind, data in ancillary
                              if level == socket.SOL_SOCKET and kind == timestamp_option), None)
                if stamp is None:
                    continue  # Never substitute 'now' for an unknown queued frame age.
                received = stamp[0] + stamp[1] / 1e9
                for item in config["inputs"]:
                    value = decode(packet, item)
                    if value is not None:
                        publisher.put(item, value, received)
            publisher.flush()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config")
    args = parser.parse_args()
    try:
        run(load(args.config))
    except KeyboardInterrupt:
        pass
