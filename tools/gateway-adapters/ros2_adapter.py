"""Explicit ROS 2 scalar topic imports and FCS telemetry exports. No control API."""
import argparse
import json
import math
import re
import time
from pathlib import Path
import urllib.error
import urllib.request
from common import Publisher, fields, load

# Fixed types/fields; configuration never supplies a Python expression or module.
TYPES = {"std_msgs/msg/Float64": ("data",), "std_msgs/msg/Int64": ("data",),
         "std_msgs/msg/Bool": ("data",), "std_msgs/msg/String": ("data",),
         "sensor_msgs/msg/Illuminance": ("illuminance",),
         "sensor_msgs/msg/Temperature": ("temperature",),
         "sensor_msgs/msg/BatteryState": ("voltage", "current", "charge", "capacity", "percentage", "present")}


def validate(config):
    fields(config, ("adapterId", "inputs"), ("endpoint", "exports", "gateway"))
    if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_-]{0,31}", config["adapterId"]):
        raise ValueError("Invalid ROS adapter ID")
    if not isinstance(config["inputs"], list) or len(config["inputs"]) > 64:
        raise ValueError("At most 64 inputs")
    if config["inputs"] and "endpoint" not in config:
        raise ValueError("Imports require a local producer endpoint")
    topics = set()
    for item in config["inputs"]:
        fields(item, ("deviceId", "sensorId", "signalId", "staleMilliseconds", "topic", "type", "field", "reliability"))
        if (not re.fullmatch(r"/[A-Za-z_][A-Za-z0-9_/]*", item["topic"]) or
                item["topic"].startswith("/fcs/telemetry/") or item["type"] not in TYPES or
                item["field"] not in TYPES[item["type"]] or item["reliability"] not in ("best_effort", "reliable")):
            raise ValueError("Unsupported ROS input mapping/QoS or feedback loop")
        topics.add(item["topic"])
    exports = config.get("exports", [])
    if not isinstance(exports, list) or len(exports) > 16:
        raise ValueError("At most 16 exports")
    if not exports and not config["inputs"]:
        raise ValueError("No inputs or exports configured")
    for item in exports:
        fields(item, ("deviceId", "sensorId", "signalId", "topic", "unit", "valueType"))
        if (not re.fullmatch(r"/fcs/telemetry/[A-Za-z_][A-Za-z0-9_/]*", item["topic"]) or item["topic"] in topics or
                item["deviceId"].startswith("ext-") or item["valueType"] not in ("number", "integer", "boolean", "string") or
                any(not re.fullmatch(r"[A-Za-z0-9_-]{1,100}", item[k]) for k in ("deviceId", "sensorId", "signalId"))):
            raise ValueError("Invalid telemetry export or adapter feedback loop")
        topics.add(item["topic"])
    if exports:
        fields(config["gateway"], ("url", "tokenFile"))
        if not re.fullmatch(r"http://127\.0\.0\.1:[0-9]{1,5}/rpc", config["gateway"]["url"]):
            raise ValueError("Telemetry exports require a same-host HTTP Gateway reader")


class Reader:
    def __init__(self, config):
        self.url = config["url"]
        self.token = Path(config["tokenFile"]).read_text().strip()
        if not re.fullmatch(r"[0-9a-fA-F]{64}", self.token):
            raise ValueError("Invalid reader token")
        # Never pass credentials through a proxy or HTTP redirect.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def read(self, item):
        payload = dict(protocol="fcs-telemetry/1", deviceId=item["deviceId"], sensorId=item["sensorId"],
                       signalId=item["signalId"], valueType=item["valueType"], unit=item["unit"],
                       value=None, valid=False, status="unavailable", receivedAtUtc=None)
        started = time.monotonic()
        try:
            request = urllib.request.Request(self.url, json.dumps(dict(operation="node.request", deviceId=item["deviceId"],
                command="data.read", arguments=dict(sensorId=item["sensorId"], signalId=item["signalId"], mode="fresh"))).encode(),
                {"Content-Type": "application/json", "Authorization": "Bearer " + self.token})
            with self.opener.open(request, timeout=.5) as response:
                raw = response.read(65537)
            if len(raw) > 65536:
                raise ValueError("Reply too large")
            envelope = json.loads(raw)
            frame = envelope["result"]
            age, maximum = envelope["sampleAgeMilliseconds"], envelope["maxAgeMilliseconds"]
            if (envelope["protocol"] != "fcs-gateway/1" or frame["protocol"] != "fcs-node/2" or
                    frame["deviceId"] != item["deviceId"] or frame["ok"] is not True or
                    type(age) not in (int, float) or type(maximum) not in (int, float) or
                    not 0 <= age + (time.monotonic() - started) * 1000 <= min(maximum, 10000)):
                raise ValueError("Invalid or stale Gateway reply")
            values = [v for v in frame["data"] if v["sensorId"] == item["sensorId"] and v["signalId"] == item["signalId"]]
            if len(values) != 1:
                raise ValueError("Ambiguous signal")
            v = values[0]
            value = v["value"]
            type_ok = {"number": type(value) in (int, float) and math.isfinite(value) if type(value) in (int, float) else False,
                       "integer": type(value) is int, "boolean": type(value) is bool, "string": type(value) is str}[item["valueType"]]
            if (v["valid"] is not True or v["fresh"] is not True or v["unit"] != item["unit"] or
                    v["valueType"] != item["valueType"] or not type_ok):
                raise ValueError("Invalid value/type/unit")
            payload.update(value=value, valid=True, status="ready", receivedAtUtc=envelope["receivedAtUtc"],
                           sequence=v["sequence"], sampleAgeMilliseconds=age)
        except (OSError, ValueError, KeyError, TypeError):
            pass
        return payload


def run(config):
    validate(config)
    import rclpy
    from rclpy.qos import QoSProfile, ReliabilityPolicy, DurabilityPolicy, HistoryPolicy
    from std_msgs.msg import Float64, Int64, Bool, String
    from sensor_msgs.msg import Illuminance, Temperature, BatteryState
    messages = dict(zip(TYPES, (Float64, Int64, Bool, String, Illuminance, Temperature, BatteryState)))
    producer = Publisher(config) if config["inputs"] else None
    rclpy.init()
    node = rclpy.create_node("fcs_" + config["adapterId"].replace("-", "_"))
    def qos(reliable):
        return QoSProfile(depth=1, history=HistoryPolicy.KEEP_LAST,
            reliability=ReliabilityPolicy.RELIABLE if reliable else ReliabilityPolicy.BEST_EFFORT,
            durability=DurabilityPolicy.VOLATILE)
    for item in config["inputs"]:
        node.create_subscription(messages[item["type"]], item["topic"],
            lambda message, item=item: producer.put(item, getattr(message, item["field"])), qos(item["reliability"] == "reliable"))
    exports = [(item, node.create_publisher(String, item["topic"], qos(True))) for item in config.get("exports", [])]
    reader = Reader(config["gateway"]) if exports else None
    # Read one export per tick: slow/disconnected sources cannot block imports for N timeouts.
    index = 0
    def export():
        nonlocal index
        item, publisher = exports[index]
        index = (index + 1) % len(exports)
        message = String()
        message.data = json.dumps(reader.read(item), separators=(",", ":"), allow_nan=False)
        publisher.publish(message)
    if producer:
        node.create_timer(.1, producer.flush)
    if exports:
        node.create_timer(.1, export)
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config")
    args = parser.parse_args()
    try:
        run(load(args.config))
    except KeyboardInterrupt:
        pass
