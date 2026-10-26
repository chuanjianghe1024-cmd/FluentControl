"""Exercise the real HTTP boundary; accepts an executable command after the script."""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

with tempfile.TemporaryDirectory(prefix="fcs-host-") as folder:
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    env = dict(os.environ, FCS_DATA=folder, ASPNETCORE_URLS=base)
    with open(Path(folder) / "host.log", "w+") as log:
        command = sys.argv[1:]
        if command[:1] == ["--docker"]:
            command = ["docker", "run", "--rm", "--network", "host", "--user", str(os.getuid()),
                       "-e", "FCS_DATA", "-e", "ASPNETCORE_URLS", "-v", f"{folder}:{folder}", command[1]]
        process = subprocess.Popen(command, env=env, stdout=log, stderr=log)
        try:
            for _ in range(100):
                if process.poll() is not None:
                    raise RuntimeError("Host exited during startup")
                try:
                    with urllib.request.urlopen(base + "/healthz", timeout=1) as response:
                        assert response.status == 200
                    break
                except (OSError, urllib.error.URLError):
                    time.sleep(0.1)
            else:
                raise RuntimeError("Host did not start")
            token = (Path(folder) / "gateway.token").read_text().strip()

            def call(body, credential=None):
                headers = {"Content-Type": "application/json"}
                if credential:
                    headers["Authorization"] = "Bearer " + credential
                request = urllib.request.Request(base + "/rpc", data=body, headers=headers)
                try:
                    with urllib.request.urlopen(request, timeout=6) as reply:
                        return reply.status, json.load(reply)
                except urllib.error.HTTPError as error:
                    return error.code, None

            assert call(b'{"operation":"status"}')[0] == 401
            assert call(b'{"operation":"status"}', "0" * 64)[0] == 401
            status, body = call(b'{"operation":"status"}', token)
            assert status == 200 and body["protocol"] == "fcs-gateway/1"
            assert call(b'{"operation":"usb.manage"}', token)[0] == 400
            assert call(b'{"operation":"shutdown"}', token)[0] == 400
            assert call(b'{"operation":"node.request","deviceId":"test","command":"sampling.set"}', token)[0] == 400
            assert call(b'{"operation":"status","operation":"list"}', token)[0] == 400
            assert call(b" " * 4097, token)[0] in (400, 413)
            print("HTTP boundary passed: authentication, protocol, USB/write/shutdown rejection, duplicate and size limits.")
        except BaseException:
            log.flush()
            log.seek(0)
            print(log.read()[-12000:], file=sys.stderr)
            raise
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
