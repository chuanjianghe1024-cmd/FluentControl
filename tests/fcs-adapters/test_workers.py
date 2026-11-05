import copy
import io
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/gateway-adapters"))
from common import load, Publisher
from socketcan_adapter import decode, validate, check_interface
from ros2_adapter import validate as validate_ros, Reader


class Workers(unittest.TestCase):
    def setUp(self):
        self.signal = dict(deviceId="ext-test-main", sensorId="sensor", signalId="reading", staleMilliseconds=500,
                           canId=0x123, extended=False, offset=0, length=2, byteOrder="little", signed=True, factor=.1, bias=2)
        self.config = dict(adapterId="test", endpoint=dict(port=19500, tokenFile="unused"), interface="can0", inputs=[self.signal])

    def test_signed_scaling(self):
        self.assertEqual(decode(struct.pack("=IB3x8s", 0x123, 2, b"\x9c\xff" + b"\0" * 6), self.signal), -8)

    def test_extended_fd_tail(self):
        signal = dict(self.signal, extended=True, canId=0x12345, offset=62, length=2, signed=False, factor=1, bias=0)
        packet = struct.pack("=IBB2x64s", 0x80012345, 64, 1, b"\0" * 62 + b"\x34\x12")
        self.assertEqual(decode(packet, signal), 0x1234)
        self.assertIsNone(decode(packet[:16], signal))

    def test_no_error_or_remote_frames(self):
        for flag in (0x20000000, 0x40000000):
            self.assertIsNone(decode(struct.pack("=IB3x8s", flag | 0x123, 2, b"\0" * 8), self.signal))

    def test_vendor_invalid_sentinel(self):
        packet = struct.pack("=IB3x8s", 0x123, 2, b"\xff\xff" + b"\0" * 6)
        self.assertTrue(math.isnan(decode(packet, dict(self.signal, invalidRaw=[-1]))))

    def test_reject_bad_maps(self):
        for key, value in (("length", 0), ("length", 9), ("canId", 0x800), ("canId", True), ("offset", -1), ("byteOrder", "motorola"), ("factor", float("nan"))):
            config = copy.deepcopy(self.config); config["inputs"][0][key] = value
            with self.assertRaises(ValueError):
                validate(config)

    def test_physical_bus_requires_listen_only(self):
        def response(modes):
            class Result:
                stdout = json.dumps([dict(flags=["UP"], linkinfo=dict(info_kind="can", info_data=dict(ctrlmode=modes)))])
            return Result()
        with patch("socketcan_adapter.subprocess.run", return_value=response([])):
            with self.assertRaises(ValueError):
                check_interface("can0")
        with patch("socketcan_adapter.subprocess.run", return_value=response(["LISTEN-ONLY"])):
            check_interface("can0")
            with self.assertRaises(ValueError):
                check_interface("can0", True)

    def test_duplicate_json(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "config.json"; path.write_text('{"id":1,"id":2}')
            with self.assertRaises(ValueError):
                load(path)

    def test_ros_feedback_and_code_fields(self):
        item = {k: self.signal[k] for k in ("deviceId", "sensorId", "signalId", "staleMilliseconds")}
        item.update(topic="/sensor/light", type="std_msgs/msg/Float64", field="data", reliability="best_effort")
        config = dict(adapterId="test", endpoint=self.config["endpoint"], inputs=[item])
        validate_ros(config)
        for key, value in (("field", "__class__"), ("type", "os/system"), ("topic", "/fcs/telemetry/loop")):
            bad = copy.deepcopy(config); bad["inputs"][0][key] = value
            with self.assertRaises(ValueError):
                validate_ros(bad)

    def test_pending_sample_does_not_refresh(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "token"; path.write_text("a" * 64)
            self.config["endpoint"]["tokenFile"] = str(path)
            producer = Publisher(self.config)
            with patch("common.time.monotonic", return_value=0):
                producer.put(self.signal, 12)
            with patch("common.time.monotonic", return_value=1), patch("common.socket.create_connection") as connect:
                self.assertIsNone(producer.flush())
                connect.assert_not_called()

    def test_ros_source_fans_out_without_duplicate_subscription(self):
        source = dict(deviceId="c3-test", sensorId="light", signalId="illuminance", valueType="number", unit="lx")
        other = dict(source, deviceId="c3-other")
        exports = [dict(source, topic="/fcs/telemetry/first"), dict(other, topic="/fcs/telemetry/other"),
                   dict(source, topic="/fcs/telemetry/copy")]
        with tempfile.TemporaryDirectory() as directory:
            token = Path(directory) / "token"; token.write_text("a" * 64)
            gateway = dict(url="http://127.0.0.1:19500/rpc", tokenFile=str(token))
            validate_ros(dict(adapterId="test", inputs=[], gateway=gateway, exports=exports))
            reader = Reader(gateway)
            def sample(item, value):
                return dict(item, value=value, valid=True, fresh=True, status="ready", sequence=7,
                    receivedAtUtc="2026-11-05T12:00:00Z", sampleAgeMilliseconds=0, maxAgeMilliseconds=5000)
            values = [sample(other, 20), sample(source, 10)]  # Response order is unrelated to topic order.
            requests = []
            def response(request, timeout):
                args = json.loads(request.data)["arguments"]; requests.append(args)
                self.assertEqual(len(args["signals"]), 2, "Gateway rejects repeated selectors")
                self.assertEqual({tuple(v[k] for k in ("deviceId", "sensorId", "signalId")) for v in args["signals"]},
                                 {(source["deviceId"], "light", "illuminance"), (other["deviceId"], "light", "illuminance")})
                return io.BytesIO(json.dumps(dict(protocol="fcs-gateway/1", subscriptionProtocol="fcs-subscription/1",
                    cursor="shared-cursor", waitedMilliseconds=0, samples=values)).encode())
            with patch.object(reader.opener, "open", side_effect=response):
                result = reader.read(exports)
                self.assertEqual([v["value"] for v in result], [10, 20, 10])
                self.assertTrue(all(v["valid"] for v in result))
                self.assertIsNot(result[0], result[2])
                values[1].update(valid=False, fresh=False, value=None, status="stale")
                stale = reader.read(exports)
                self.assertEqual([v["valid"] for v in stale], [False, True, False])
                self.assertEqual(requests[1]["cursor"], "shared-cursor")

    def test_ros_subscription_and_buffered_expiry(self):
        item = dict(deviceId="c3-test", sensorId="light", signalId="illuminance", valueType="number", unit="lx")
        with tempfile.TemporaryDirectory() as directory:
            token = Path(directory) / "token"; token.write_text("a" * 64)
            reader = Reader(dict(url="http://127.0.0.1:19500/rpc", tokenFile=str(token)))
            value = dict(item, value=12.5, valid=True, fresh=True, status="ready", sequence=1,
                         receivedAtUtc="2026-11-05T12:00:00Z", sampleAgeMilliseconds=100, maxAgeMilliseconds=500)
            envelope = dict(protocol="fcs-gateway/1", subscriptionProtocol="fcs-subscription/1", cursor="cursor-one",
                            waitedMilliseconds=1000, samples=[value])
            with patch.object(reader.opener, "open", return_value=io.BytesIO(json.dumps(envelope).encode())) as opened, \
                    patch("ros2_adapter.time.monotonic", side_effect=[0, 1.05]):
                sample = reader.read([item])[0]
            request = json.loads(opened.call_args.args[0].data)
            self.assertEqual(request["operation"], "subscriptions.read")
            self.assertEqual(request["arguments"]["signals"][0], {k: item[k] for k in ("deviceId", "sensorId", "signalId")})
            self.assertTrue(sample["valid"], "Server long-poll duration must not age the sample twice")
            self.assertAlmostEqual(sample["sampleAgeMilliseconds"], 150)
            self.assertFalse(Reader.age(sample, .4)["valid"])
            self.assertIsNone(Reader.age(sample, .4)["value"])
            self.assertTrue(sample["valid"], "Queue expiry must not mutate the shared snapshot")
            envelope["samples"][0]["value"] = True
            with patch.object(reader.opener, "open", return_value=io.BytesIO(json.dumps(envelope).encode())):
                self.assertFalse(reader.read([item])[0]["valid"], "Boolean must not satisfy a numeric signal")


if __name__ == "__main__":
    unittest.main()
