"""Real Gateway HTTP/TCP contract tests, optional vcan and ROS 2 process tests."""
import argparse
import copy
import datetime as dt
import json
import os
from pathlib import Path
import socket
import struct
import subprocess
import sys
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/gateway-adapters"))
from common import Publisher, receive_exact, utc
from socketcan_adapter import decode, validate as validate_can, check_interface
from ros2_adapter import validate as validate_ros


class TelemetryFixture:
    """Protocol fixture for a C3 signal: exercise the real ROS publication path."""
    def __init__(self, token):
        self.valid = True
        fixture = self
        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                assert self.path == "/rpc"
                assert self.headers.get("Authorization") == "Bearer " + token
                request = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                assert request["operation"] == "subscriptions.read"
                assert request["arguments"]["signals"][0]["deviceId"] == "c3-fixture"
                value = dict(deviceId="c3-fixture", sensorId="light", signalId="illuminance", valueType="number", unit="lx", value=77.25,
                             valid=fixture.valid, fresh=fixture.valid, status="ready" if fixture.valid else "stale", sequence=5, uptimeMs=5000,
                             sampleAgeMilliseconds=0 if fixture.valid else 20000, maxAgeMilliseconds=5000, receivedAtUtc=utc())
                frame = dict(protocol="fcs-gateway/1", subscriptionProtocol="fcs-subscription/1", cursor="fixture", changed=True,
                             waitedMilliseconds=0, samples=[value])
                raw = json.dumps(frame).encode()
                self.send_response(200); self.send_header("Content-Length", str(len(raw))); self.end_headers(); self.wfile.write(raw)
            def log_message(self, *args):
                pass
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.url = f"http://127.0.0.1:{self.server.server_port}/rpc"

    def close(self):
        self.server.shutdown(); self.server.server_close()


def port():
    with socket.socket() as stream:
        stream.bind(("127.0.0.1", 0))
        return stream.getsockname()[1]


def until(action, timeout=10):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            value = action()
            if value:
                return value
        except (OSError, KeyError, ValueError) as error:
            last = error
        time.sleep(.1)
    raise AssertionError("Timed out: " + str(last))


def main(args):
    with tempfile.TemporaryDirectory() as temporary:
        directory = Path(temporary)
        http_port, producer_port = port(), port()
        signal = dict(deviceId="ext-test-main", sensorId="light", signalId="illuminance", valueType="number", unit="lx", staleMilliseconds=1000)
        config = dict(listenPort=producer_port, adapters=[dict(id="test", kind="socketcan", enabled=True, signals=[signal]),
            dict(id="other", kind="custom", enabled=True, signals=[dict(signal, deviceId="ext-other-main")])])
        (directory / "adapters.json").write_text(json.dumps(config))
        (directory / "nodes.json").write_text(json.dumps(dict(Nodes=[], Discover=False)))
        environment = dict(os.environ, FCS_DATA=str(directory), ASPNETCORE_URLS=f"http://127.0.0.1:{http_port}")
        log = (directory / "host.log").open("w+")
        host = subprocess.Popen([args.dotnet] + ([args.host] if args.host else []), env=environment, stdout=log, stderr=log)
        children = []
        telemetry = None
        try:
            url = f"http://127.0.0.1:{http_port}/rpc"
            until(lambda: urllib.request.urlopen(url.replace("/rpc", "/healthz"), timeout=1).status == 200)
            reader = (directory / "gateway.token").read_text()
            manager = (directory / "manager.token").read_text()
            token = (directory / "adapter-tokens/test.token").read_text()
            # A configured but unavailable producer port must fail startup,
            # rather than leave an apparently healthy Gateway without ingestion.
            conflict = subprocess.Popen([args.dotnet] + ([args.host] if args.host else []),
                env=dict(environment, ASPNETCORE_URLS=f"http://127.0.0.1:{port()}"), stdout=log, stderr=log)
            try:
                conflict.wait(timeout=10)
                assert conflict.returncode != 0, "Conflicting adapter port reported a successful exit"
            finally:
                if conflict.poll() is None:
                    conflict.terminate(); conflict.wait(timeout=5)
            def rpc(operation, auth=reader, **kwargs):
                request = urllib.request.Request(url, json.dumps(dict(operation=operation, **kwargs)).encode(),
                    {"Authorization": "Bearer " + auth, "Content-Type": "application/json"})
                with urllib.request.urlopen(request, timeout=3) as response:
                    return json.load(response)
            def data():
                return rpc("node.request", deviceId=signal["deviceId"], command="data.read",
                    arguments=dict(sensorId="light", signalId="illuminance", mode="fresh"))
            def value():
                return data()["result"]["data"][0]
            def watch(cursor=None, wait=0):
                return rpc("subscriptions.read", arguments=dict(signals=[{k: signal[k] for k in ("deviceId", "sensorId", "signalId")}],
                    cursor=cursor, waitMilliseconds=wait))
            def unified_value():
                return watch()["samples"][0]
            def denied(operation, status, **kwargs):
                try:
                    rpc(operation, **kwargs)
                except urllib.error.HTTPError as error:
                    assert error.code == status, (operation, error.code)
                else:
                    raise AssertionError("Unexpected authority: " + operation)
            boot = uuid.uuid4().hex
            sample = dict(deviceId=signal["deviceId"], sensorId="light", signalId="illuminance", sequence=1,
                          receivedAtUtc=utc(), value=123.5, valid=True)
            def frame(seq, item=None, **kwargs):
                return dict(protocol="fcs-adapter/1", adapterId="test", token=token, bootId=boot,
                            sequence=seq, data=[item or sample], **kwargs)
            def send(payload):
                time.sleep(.11)
                raw = json.dumps(payload).encode()
                with socket.create_connection(("127.0.0.1", producer_port), timeout=3) as connection:
                    connection.sendall(struct.pack("!I", len(raw)) + raw)
                    size, = struct.unpack("!I", receive_exact(connection, 4))
                    return json.loads(receive_exact(connection, size))["ok"]
            assert len(rpc("list")["devices"]) == 2
            assert not value()["valid"]
            catalog = rpc("node.request", deviceId=signal["deviceId"], command="sensors.list", arguments={})
            assert catalog["result"]["sensors"][0]["physical"] is False
            denied("adapters.status", 403)
            assert len(rpc("adapters.status", auth=manager)["adapters"]) == 2
            denied("adapter.publish", 400)
            denied("nodes.save", 400, auth=manager, deviceId="ext-test-main", ip="192.168.1.20")
            denied("nodes.remove", 400, auth=manager, deviceId="ext-test-main")
            denied("node.request", 400, deviceId="ext-unknown-main", command="data.read", arguments={})
            denied("node.request", 400, deviceId=signal["deviceId"], command="sampling.set",
                   arguments=dict(sensorId="light", enabled=False, intervalMs=1000))
            sample["receivedAtUtc"] = utc()
            bad = frame(0); bad["token"] = reader
            assert not send(bad)
            assert not send(frame(0, dict(sample, deviceId="ext-other-main")))
            assert not send(frame(0, dict(sample, value="wrong")))
            assert not send(frame(0, dict(sample, receivedAtUtc=utc(time.time() - 10))))
            sample["receivedAtUtc"] = utc()
            assert send(frame(0))
            assert value()["value"] == 123.5 and value()["fresh"]
            age = data()["sampleAgeMilliseconds"]
            assert not send(frame(0))  # Batch replay.
            assert send(frame(1))  # Identical heartbeat must not renew age.
            assert data()["sampleAgeMilliseconds"] > age + 150
            assert not send(frame(2, dict(sample, value=99)))  # Changed sample, same sequence.
            until(lambda: not value()["valid"])
            assert value()["value"] is None
            new_boot = uuid.uuid4().hex
            sample.update(sequence=0, receivedAtUtc=utc())
            current = frame(0); current["bootId"] = new_boot
            assert send(current)
            sample.update(sequence=2, receivedAtUtc=utc())
            assert not send(frame(3))  # Retired process boot cannot come back.
            # Malformed frame must not kill the listener.
            assert not send({"protocol": 1})
            mapping = {k: signal[k] for k in ("deviceId", "sensorId", "signalId", "staleMilliseconds")}
            mapping.update(canId=0x123, extended=False, offset=0, length=2, byteOrder="little", signed=False, factor=.5, bias=0)
            worker = dict(adapterId="test", endpoint=dict(port=producer_port, tokenFile=str(directory / "adapter-tokens/test.token")),
                          interface="vcan0", fd=False, inputs=[mapping])
            validate_can(worker)
            packet = struct.pack("=IB3x8s", 0x123, 2, b"\x00\x01" + b"\0" * 6)
            assert decode(packet, mapping) == 128
            assert decode(struct.pack("=IB3x8s", 0x123 | 0x40000000, 2, b"\0" * 8), mapping) is None
            assert decode(packet, dict(mapping, extended=True)) is None
            assert decode(packet, dict(mapping, offset=7)) is None
            assert decode(packet, dict(mapping, byteOrder="big")) == .5
            producer = Publisher(worker)
            producer.put(mapping, 200)
            assert until(producer.flush)
            assert value()["value"] == 200
            first = watch(wait=1000)
            assert first["samples"][0]["value"] == 200
            with ThreadPoolExecutor(max_workers=1) as executor:
                waiting = executor.submit(watch, first["cursor"], 2000)
                time.sleep(.1)
                started = time.monotonic()
                assert rpc("status")["protocol"] == "fcs-gateway/1"
                assert time.monotonic() - started < 1, "Long poll blocked control requests"
                assert not waiting.done()
                producer.put(mapping, 201)
                assert until(producer.flush)
                changed = waiting.result(timeout=3)
                assert changed["changed"] and changed["samples"][0]["value"] == 201
            denied("subscriptions.read", 401, auth=token, arguments=dict(signals=[{k: signal[k] for k in ("deviceId", "sensorId", "signalId")}]))
            denied("subscriptions.read", 400, arguments=dict(signals=[], command="recovery.start"))
            until(lambda: not value()["valid"])
            assert producer.flush() is None  # No cached-value heartbeat.
            print("PASS Gateway authority, scope, replay, expiry, virtual metadata, publisher and CAN decoding", flush=True)

            if args.vcan:
                worker["interface"] = args.vcan
                check_interface(args.vcan)
                path = directory / "can.json"; path.write_text(json.dumps(worker))
                child = subprocess.Popen([sys.executable, str(ROOT / "tools/gateway-adapters/socketcan_adapter.py"), str(path)])
                children.append(child)
                with socket.socket(socket.AF_CAN, socket.SOCK_RAW, socket.CAN_RAW) as transmitter:
                    transmitter.bind((args.vcan,))
                    def transmit():
                        assert child.poll() is None, "CAN worker exited"
                        transmitter.send(packet)  # Test-only producer on virtual CAN, never real hardware.
                        return unified_value()["valid"] and unified_value()["value"] == 128
                    until(transmit)
                until(lambda: not value()["valid"])
                child.terminate(); child.wait(timeout=5)
                print("PASS real vcan kernel frames -> worker -> Gateway -> expiration", flush=True)
                worker["fd"] = True
                worker["inputs"] = [dict(mapping, canId=0x12345, extended=True, offset=62, factor=1, invalidRaw=[65535])]
                path.write_text(json.dumps(worker))
                child = subprocess.Popen([sys.executable, str(ROOT / "tools/gateway-adapters/socketcan_adapter.py"), str(path)])
                children.append(child)
                with socket.socket(socket.AF_CAN, socket.SOCK_RAW, socket.CAN_RAW) as transmitter:
                    transmitter.setsockopt(socket.SOL_CAN_RAW, socket.CAN_RAW_FD_FRAMES, 1)
                    transmitter.bind((args.vcan,))
                    fd_packet = struct.pack("=IBB2x64s", 0x80012345, 64, 1, b"\0" * 62 + b"\x34\x12")
                    def transmit_fd():
                        assert child.poll() is None, "CAN FD worker exited"
                        transmitter.send(fd_packet)
                        return unified_value()["valid"] and unified_value()["value"] == 0x1234
                    until(transmit_fd)
                    invalid_packet = struct.pack("=IBB2x64s", 0x80012345, 64, 1, b"\0" * 62 + b"\xff\xff")
                    def transmit_invalid():
                        transmitter.send(invalid_packet)
                        return not unified_value()["valid"]
                    until(transmit_invalid)
                child.terminate(); child.wait(timeout=5)
                print("PASS real vcan CAN FD extended ID, final payload bytes and invalid sentinel", flush=True)

            if args.ros or args.ros_peer:
                import rclpy
                from std_msgs.msg import Float64, String, Bool
                from rclpy.qos import QoSProfile, ReliabilityPolicy
                telemetry = TelemetryFixture(reader)
                ros_input = {k: signal[k] for k in ("deviceId", "sensorId", "signalId", "staleMilliseconds")}
                ros_input.update(topic="/test/light", type="std_msgs/msg/Float64", field="data", reliability="best_effort")
                ros = dict(adapterId="test", endpoint=worker["endpoint"], inputs=[ros_input],
                    gateway=dict(url=telemetry.url, tokenFile=str(directory / "gateway.token")),
                    exports=[dict(deviceId="c3-fixture", sensorId="light", signalId="illuminance", valueType="number", unit="lx", topic="/fcs/telemetry/light")])
                validate_ros(ros)
                path = directory / "ros.json"; path.write_text(json.dumps(ros))
                child = subprocess.Popen([sys.executable, str(ROOT / "tools/gateway-adapters/ros2_adapter.py"), str(path)])
                children.append(child)
                rclpy.init(); node = rclpy.create_node("fcs_adapter_test")
                publisher = node.create_publisher(Float64, "/test/light", QoSProfile(depth=1, reliability=ReliabilityPolicy.BEST_EFFORT))
                received = []
                subscription = node.create_subscription(String, "/test/export_seen" if args.ros_peer else "/fcs/telemetry/light", lambda m: received.append(json.loads(m.data)), 1)
                stop_peer = node.create_publisher(Bool, "/test/stop_light", 1)
                def publish():
                    assert child.poll() is None, "ROS worker exited"
                    message = Float64(); message.data = 321.5
                    if not args.ros_peer:
                        publisher.publish(message)
                    rclpy.spin_once(node, timeout_sec=.05)
                    return unified_value()["valid"] and unified_value()["value"] == 321.5 and received
                until(publish, 20)
                assert received[-1]["valid"] is True and received[-1]["value"] == 77.25
                telemetry.valid = False
                def invalid_export():
                    rclpy.spin_once(node, timeout_sec=.05)
                    return received[-1]["valid"] is False and received[-1]["value"] is None
                until(invalid_export)
                if args.ros_peer:
                    stop = Bool(); stop.data = True; stop_peer.publish(stop)
                until(lambda: not unified_value()["valid"])
                node.destroy_node(); rclpy.shutdown()
                child.terminate(); child.wait(timeout=5)
                assert child.returncode == 0, "ROS worker did not shut down cleanly"
                print("PASS real ROS pub/sub import, telemetry failure quality, expiration and clean shutdown", flush=True)
            assert host.poll() is None
        except BaseException:
            log.flush(); log.seek(0); print(log.read())
            raise
        finally:
            for child in children:
                if child.poll() is None:
                    child.terminate(); child.wait(timeout=5)
            host.terminate(); host.wait(timeout=10); log.close()
            if telemetry:
                telemetry.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--host", default=str(ROOT / "src/FluentControl.FcsHost/bin/Release/net10.0/FluentControl.FcsHost.dll"))
    parser.add_argument("--vcan")
    parser.add_argument("--ros", action="store_true")
    parser.add_argument("--ros-peer", action="store_true")
    main(parser.parse_args())
