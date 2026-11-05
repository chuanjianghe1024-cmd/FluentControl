"""Explicit ROS 2 scalar topic imports and FCS telemetry exports. No control API."""
import argparse
import json
import math
import re
import time
import queue
import threading
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
        self.cursor = None
        self.token = Path(config["tokenFile"]).read_text().strip()
        if not re.fullmatch(r"[0-9a-fA-F]{64}", self.token):
            raise ValueError("Invalid reader token")
        # Never pass credentials through a proxy or HTTP redirect.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    @staticmethod
    def unavailable(item):
        return dict(protocol="fcs-telemetry/1", deviceId=item["deviceId"], sensorId=item["sensorId"],
                    signalId=item["signalId"], valueType=item["valueType"], unit=item["unit"],
                    value=None, valid=False, status="unavailable", receivedAtUtc=None)

    def read(self, items):
        result = [self.unavailable(item) for item in items]
        started = time.monotonic()
        try:
            request = urllib.request.Request(self.url, json.dumps(dict(operation="subscriptions.read", arguments=dict(
                signals=[{key: item[key] for key in ("deviceId", "sensorId", "signalId")} for item in items],
                cursor=self.cursor, waitMilliseconds=1000))).encode(),
                {"Content-Type": "application/json", "Authorization": "Bearer " + self.token})
            with self.opener.open(request, timeout=3) as response:
                raw = response.read(262145)
            if len(raw) > 262144:
                raise ValueError("Reply too large")
            envelope = json.loads(raw)
            if envelope["protocol"] != "fcs-gateway/1" or envelope["subscriptionProtocol"] != "fcs-subscription/1":
                raise ValueError("Gateway subscription protocol required")
            self.cursor = envelope["cursor"]
            waited = envelope["waitedMilliseconds"]
            if type(waited) is not int or not 0 <= waited <= 5000:
                raise ValueError("Invalid subscription timing")
            transit = max(0, (time.monotonic() - started) * 1000 - waited)
            for index, item in enumerate(items):
                values = [v for v in envelope["samples"] if all(v[k] == item[k] for k in ("deviceId", "sensorId", "signalId"))]
                if len(values) != 1:
                    continue
                v = values[0]
                if v["valid"] is not True or v["fresh"] is not True:
                    result[index]["status"] = v["status"]
                    continue
                value = v["value"]
                type_ok = {"number": type(value) in (int, float) and math.isfinite(value) if type(value) in (int, float) else False,
                           "integer": type(value) is int, "boolean": type(value) is bool, "string": type(value) is str}[item["valueType"]]
                if v["unit"] != item["unit"] or v["valueType"] != item["valueType"] or not type_ok:
                    continue
                age, maximum = v["sampleAgeMilliseconds"], v["maxAgeMilliseconds"]
                if (type(age) not in (int, float) or type(maximum) is not int or not 500 <= maximum <= 60000 or
                        not 0 <= age + transit <= maximum):
                    continue
                result[index].update(value=value, valid=True, status="ready", receivedAtUtc=v["receivedAtUtc"],
                    sequence=v["sequence"], sampleAgeMilliseconds=age + transit,
                    maxAgeMilliseconds=maximum)
        except (OSError, ValueError, KeyError, TypeError):
            self.cursor = None  # Reconnect obtains a fresh snapshot; never replays an old queue.
        return result

    @staticmethod
    def age(sample, elapsed):
        sample = dict(sample)
        if sample["valid"]:
            sample["sampleAgeMilliseconds"] += elapsed * 1000
            if sample["sampleAgeMilliseconds"] > sample["maxAgeMilliseconds"]:
                sample.update(value=None, valid=False, status="stale")
        return sample


def run(config):
    validate(config)
    import rclpy
    from rclpy.executors import ExternalShutdownException
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
    # A blocking network subscription must never stall ROS input callbacks.
    pending = queue.Queue(maxsize=1)
    stopping = threading.Event()
    def receive_exports():
        items = [item for item, _ in exports]
        while not stopping.is_set():
            samples = reader.read(items)
            batch = (time.monotonic(), samples)
            try:
                pending.put_nowait(batch)
            except queue.Full:
                try:
                    pending.get_nowait()
                except queue.Empty:
                    pass
                pending.put_nowait(batch)
            stopping.wait(.1)
    worker = threading.Thread(target=receive_exports, daemon=True) if exports else None
    if worker:
        worker.start()
    def export():
        try:
            received, samples = pending.get_nowait()
        except queue.Empty:
            return
        for (_, publisher), sample in zip(exports, samples):
            message = String()
            message.data = json.dumps(reader.age(sample, time.monotonic() - received), separators=(",", ":"), allow_nan=False)
            publisher.publish(message)
    if producer:
        node.create_timer(.1, producer.flush)
    if exports:
        node.create_timer(.1, export)
    try:
        rclpy.spin(node)
    except (KeyboardInterrupt, ExternalShutdownException):
        pass
    finally:
        stopping.set()
        if worker:
            worker.join(timeout=4)
        node.destroy_node()
        rclpy.try_shutdown()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config")
    args = parser.parse_args()
    try:
        run(load(args.config))
    except KeyboardInterrupt:
        pass
