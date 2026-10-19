# MicroPython reference firmware, RP2040-Zero or ESP32-C3 + BH1750.
# C3 default SDA=GPIO4 SCL=GPIO5; RP2040 SDA=GP0 SCL=GP1. Verify board labels.
# Power BH1750 from 3V3. Four wires: VCC/GND/SDA/SCL. Sensor ADDR tied low locally.
# Copy BOTH main.py and fc_device.py to device. USB console must be enabled in firmware.
import sys, os, time, select, json, binascii, machine
from fc_device import Device, mac, equal

IS_C3 = sys.platform == 'esp32'
if IS_C3:
    import network, socket

class Backend:
    wifi = IS_C3
    def __init__(self):
        self.wlan = network.WLAN(network.STA_IF) if IS_C3 else None
        self.i2c = machine.I2C(0, sda=machine.Pin(4 if IS_C3 else 0), scl=machine.Pin(5 if IS_C3 else 1), freq=100000)
    def load(self):
        try:
            with open('fc-config.json') as f: return json.load(f)
        except (OSError, ValueError): return {}
    def save(self, config):
        with open('fc-config.tmp', 'w') as f: json.dump(config, f)
        os.rename('fc-config.tmp', 'fc-config.json')
    def random_key(self): return binascii.hexlify(os.urandom(32)).decode()
    def now(self): return time.ticks_ms()
    def elapsed(self, start): return time.ticks_diff(time.ticks_ms(), start)
    def connect(self, ssid, password):
        if self.wlan:
            self.wlan.active(True); self.wlan.disconnect(); self.wlan.connect(ssid, password)
    def disconnect(self):
        if self.wlan and self.wlan.active(): self.wlan.disconnect()
    def disable(self):
        if self.wlan: self.wlan.active(False)
    def connected(self): return bool(self.wlan and self.wlan.isconnected())
    def ip(self): return self.wlan.ifconfig()[0] if self.connected() else ''
    def scan(self):
        if not self.wlan: return []
        active = self.wlan.active(); self.wlan.active(True)
        try: return sorted(set(x[0].decode('utf-8', 'ignore') for x in self.wlan.scan() if x[0]))
        finally:
            if not active: self.wlan.active(False)
    def restart(self): machine.reset()
    def measure(self):
        found = self.i2c.scan(); addr = next((a for a in (0x23, 0x5c) if a in found), None)
        if addr is None: raise OSError('sensor absent')
        self.i2c.writeto(addr, b'\x01'); self.i2c.writeto(addr, b'\x20'); time.sleep_ms(200)
        data = self.i2c.readfrom(addr, 2); return round(((data[0]<<8)|data[1])/1.2, 2)

backend = Backend()
device = Device(('c3' if IS_C3 else 'rp2040') + '-bh1750-' + binascii.hexlify(machine.unique_id()).decode(), backend)
poll = select.poll(); poll.register(sys.stdin, select.POLLIN)
udp = http = None
if IS_C3:
    udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); udp.bind(('0.0.0.0', 42735)); udp.setblocking(False)
    http = socket.socket(); http.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1); http.bind(('0.0.0.0', 8766)); http.listen(2); http.setblocking(False)

line = ''; discard = False
while True:
    device.tick()
    if poll.poll(10):
        ch = sys.stdin.read(1)
        if ch == '\n':
            if not discard:
                response = device.serial(line)
                if response: print(response)
            line = ''; discard = False
        elif not discard:
            line += ch
            if len(line) > 2200: line = ''; discard = True
    if not IS_C3 or not backend.connected(): continue
    try:
        message, sender = udp.recvfrom(64)
        if message == b'FC-DISCOVER/1': udp.sendto(json.dumps({'protocol': 'fc-device/1', 'sourceId': device.identity}).encode(), sender)
    except OSError: pass
    try: conn, address = http.accept()
    except OSError: continue
    try:
        conn.settimeout(.5); raw = b''
        while b'\r\n\r\n' not in raw and len(raw) < 4096:
            chunk = conn.recv(512)
            if not chunk: break
            raw += chunk
        lines = raw.decode().split('\r\n'); headers = {}
        for header in lines[1:]:
            if ':' in header:
                k, v = header.split(':', 1); headers[k.lower()] = v.strip()
        nonce = headers.get('x-fc-nonce', '')
        allowed = len(nonce) == 32 and all(c in '0123456789abcdef' for c in nonce)
        expected = mac(device.config['bindingKey'], 'READ\n' + device.identity + '\n' + nonce)
        if lines[0] != 'GET /v1/sample HTTP/1.1' or not allowed or not equal(expected, headers.get('x-fc-signature', '')):
            conn.write(b'HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n')
        else:
            body = json.dumps(device.sample())
            signature = mac(device.config['bindingKey'], nonce + '\n' + body)
            conn.write(('HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nCache-Control: no-store\r\nX-FC-Signature: ' + signature + '\r\nContent-Length: ' + str(len(body.encode())) + '\r\nConnection: close\r\n\r\n' + body).encode())
    except (OSError, ValueError, UnicodeError): pass
    finally: conn.close()
