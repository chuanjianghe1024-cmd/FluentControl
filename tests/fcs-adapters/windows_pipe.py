"""Exercise the real current-user Windows Gateway pipe with an adapter producer."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from integration import ROOT, port, until
from common import Publisher

with tempfile.TemporaryDirectory() as temporary:
    directory = Path(temporary)
    signal = dict(deviceId="ext-pipe-main", sensorId="light", signalId="illuminance", valueType="number", unit="lx", staleMilliseconds=1000)
    endpoint = port()
    (directory / "adapters.json").write_text(json.dumps(dict(listenPort=endpoint,
        adapters=[dict(id="pipe", kind="custom", enabled=True, signals=[signal])])))
    executable = ROOT / "src/FluentControl.FcsGateway/bin/Release/net8.0-windows/win-x64/FluentControl.FcsGateway.exe"
    child = subprocess.Popen([str(executable), "--serve"], env=dict(os.environ, FCS_DATA=str(directory)))
    def rpc(operation, **kwargs):
        with open(r"\\.\pipe\FluentControl.FcsGateway.v1", "r+b", buffering=0) as pipe:
            pipe.write(json.dumps(dict(operation=operation, **kwargs)).encode() + b"\n")
            response = bytearray()
            while len(response) < 65536:
                byte = pipe.read(1)
                if not byte or byte == b"\n":
                    return json.loads(response)
                response.extend(byte)
        raise AssertionError("Oversized pipe reply")
    try:
        until(lambda: rpc("list").get("devices"))
        assert any(d["deviceId"] == signal["deviceId"] and d["transport"] == "adapter" for d in rpc("list")["devices"])
        producer = Publisher(dict(adapterId="pipe", endpoint=dict(port=endpoint, tokenFile=str(directory / "adapter-tokens/pipe.token")), inputs=[signal]))
        producer.put(signal, 55.5)
        assert until(producer.flush)
        def read():
            return rpc("node.request", deviceId=signal["deviceId"], command="data.read", arguments=dict(mode="fresh"))
        assert read()["result"]["data"][0]["value"] == 55.5
        for op in ("usb.read", "sample.read", "usb.manage"):
            assert "error" in rpc(op, deviceId=signal["deviceId"], command="factory.reset")
        assert "error" in rpc("node.request", deviceId=signal["deviceId"], command="sampling.set",
                              arguments=dict(sensorId="light", enabled=False, intervalMs=1000))
        until(lambda: not read()["result"]["data"][0]["valid"])
        rpc("shutdown")
        child.wait(timeout=5)
        assert child.returncode == 0
        print("PASS Windows pipe listing, adapter read, no USB fallback/write authority, expiration")
    finally:
        if child.poll() is None:
            child.terminate(); child.wait(timeout=5)
