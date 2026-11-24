"""Exercise the real current-user Windows Gateway pipe with an adapter producer."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor
from integration import ROOT, port, until
from common import Publisher

with tempfile.TemporaryDirectory() as temporary:
    directory = Path(temporary)
    signal = dict(deviceId="ext-pipe-main", sensorId="light", signalId="illuminance", valueType="number", unit="lx", staleMilliseconds=3000)
    endpoint = port()
    (directory / "adapters.json").write_text(json.dumps(dict(listenPort=endpoint,
        adapters=[dict(id="pipe", kind="custom", enabled=True, signals=[signal])])))
    executable = ROOT / "src/FluentControl.FcsGateway/bin/Release/net8.0-windows/win-x64/FluentControl.FcsGateway.exe"
    child = subprocess.Popen([str(executable), "--serve"], env=dict(os.environ, FCS_DATA=str(directory)))
    def rpc(operation, **kwargs):
        # Like NamedPipeClientStream.ConnectAsync, wait for the next server
        # instance. Retry only connection establishment, never a sent request.
        deadline = time.monotonic() + 3
        while True:
            try:
                pipe = open(r"\\.\pipe\FluentControl.FcsGateway.v1", "r+b", buffering=0)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    raise
                time.sleep(.02)
        with pipe:
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
        def watch(cursor=None):
            return rpc("subscriptions.read", arguments=dict(signals=[{k: signal[k] for k in ("deviceId", "sensorId", "signalId")}],
                cursor=cursor, waitMilliseconds=2000))
        first = watch()
        assert first["samples"][0]["value"] == 55.5
        with ThreadPoolExecutor(max_workers=1) as executor:
            waiting = executor.submit(watch, first["cursor"])
            time.sleep(.1)
            started = time.monotonic()
            assert rpc("status")["protocol"] == "fcs-gateway/1"
            assert time.monotonic() - started < 1, "Long poll blocked control requests"
            assert not waiting.done(), "Unchanged data prematurely completed a long poll"
            producer.put(signal, 66.5)
            assert until(producer.flush)
            changed = waiting.result(timeout=3)
            assert changed["changed"] and changed["samples"][0]["value"] == 66.5
        for op in ("usb.read", "sample.read", "usb.manage"):
            assert "error" in rpc(op, deviceId=signal["deviceId"], command="factory.reset")
        assert "error" in rpc("node.request", deviceId=signal["deviceId"], command="sampling.set",
                              arguments=dict(sensorId="light", enabled=False, intervalMs=1000))
        until(lambda: not watch()["samples"][0]["valid"])
        rpc("shutdown")
        child.wait(timeout=5)
        assert child.returncode == 0
        print("PASS Windows pipe subscription, concurrent long poll/control, no USB fallback/write authority, expiration")
    finally:
        if child.poll() is None:
            child.terminate(); child.wait(timeout=5)
