"""FC device/1 reference core. USB is authoritative; no remote management endpoint.
MicroPython + CPython-compatible core for state-machine tests. No credentials in logs.
"""
import json
import binascii
import hashlib

OPERATIONS = ('status', 'wifi.scan', 'wifi.configure', 'wifi.cancel', 'wifi.forget',
              'wireless.disable', 'wireless.enable', 'binding.rotate', 'reboot', 'factory.reset')

def mac(key, message):
    key = binascii.unhexlify(key)
    key = key + b'\0' * (64 - len(key))
    inner = bytes(x ^ 0x36 for x in key)
    outer = bytes(x ^ 0x5c for x in key)
    return binascii.hexlify(hashlib.sha256(outer + hashlib.sha256(inner + message.encode()).digest()).digest()).decode()

def equal(a, b):
    if not isinstance(a, str) or not isinstance(b, str) or len(a) != len(b):
        return False
    result = 0
    for x, y in zip(a.encode(), b.encode()):
        result |= x ^ y
    return result == 0

class Device:
    def __init__(self, identity, backend):
        self.identity, self.backend = identity, backend
        self.config = backend.load()
        self.pending = None
        self.state = 'idle'
        self.sequence = 0
        self.history = []
        self.restart_at = None
        if not self.config.get('bindingKey'):
            self.config['bindingKey'] = backend.random_key()
            self.save()
        if backend.wifi and self.config.get('wireless', True) and self.config.get('ssid'):
            backend.connect(self.config['ssid'], self.config.get('password', ''))

    def save(self):
        self.backend.save(self.config)

    def hello(self):
        return {'protocol': 'fc-light-usb/1', 'sourceId': self.identity,
                'management': 'fc-device/1', 'signals': [{'id': 'illuminance', 'unit': 'lx', 'minIntervalMs': 200}],
                'operations': list(OPERATIONS if self.backend.wifi else ('status', 'reboot', 'factory.reset'))}

    def status(self):
        return {'ok': True, 'state': 'configuring' if self.pending else ('connected' if self.backend.connected() else self.state),
                'ip': self.backend.ip(), 'operations': self.hello()['operations'],
                'bindingKey': self.config['bindingKey'] if self.backend.wifi else None,
                'sourceId': self.identity}

    def handle(self, operation, args=None, transport='usb'):
        # Transport is supplied by the physical endpoint, never read from request JSON.
        if transport != 'usb':
            return {'ok': False, 'error': 'usb_required', 'sourceId': self.identity}
        args = args or {}
        if operation not in self.hello()['operations']:
            return {'ok': False, 'error': 'unsupported', 'sourceId': self.identity}
        if operation == 'wifi.scan':
            result = self.status()
            result['networks'] = self.backend.scan()[:32]
            return result
        if operation == 'wifi.configure':
            ssid, password = args.get('ssid'), args.get('password')
            if not isinstance(ssid, str) or not 1 <= len(ssid.encode()) <= 32 or not isinstance(password, str) or len(password.encode()) > 64:
                return {'ok': False, 'error': 'invalid_network', 'sourceId': self.identity}
            # Old known-good credentials remain on flash until the new network connects.
            self.pending = {'ssid': ssid, 'password': password, 'started': self.backend.now()}
            self.backend.connect(ssid, password)
        elif operation == 'wifi.cancel':
            self.pending = None
            self.restore()
        elif operation == 'wifi.forget':
            self.pending = None
            self.config.pop('ssid', None); self.config.pop('password', None)
            self.save(); self.backend.disconnect(); self.state = 'forgotten'
        elif operation == 'wireless.disable':
            self.pending = None; self.config['wireless'] = False
            self.save(); self.backend.disable(); self.state = 'disabled'
        elif operation == 'wireless.enable':
            self.config['wireless'] = True; self.save(); self.restore()
        elif operation == 'binding.rotate':
            self.config['bindingKey'] = self.backend.random_key(); self.save()
        elif operation == 'factory.reset':
            self.pending = None; self.backend.disable()
            self.config = {'bindingKey': self.backend.random_key(), 'wireless': False}
            self.save(); self.history = []; self.state = 'reset'
        elif operation == 'reboot':
            self.restart_at = self.backend.now(); self.state = 'restart_accepted'
        return self.status()

    def restore(self):
        self.backend.disconnect()
        if self.config.get('wireless', True) and self.config.get('ssid'):
            self.backend.connect(self.config['ssid'], self.config.get('password', ''))
        elif not self.config.get('wireless', True):
            self.backend.disable()
        self.state = 'idle'

    def tick(self):
        if self.pending:
            if self.backend.connected():
                self.config.update({'ssid': self.pending['ssid'], 'password': self.pending['password'], 'wireless': True})
                self.save(); self.pending = None; self.state = 'connected'
            elif self.backend.elapsed(self.pending['started']) > 20000:
                self.pending = None; self.restore(); self.state = 'failed_restored'
        if self.restart_at is not None and self.backend.elapsed(self.restart_at) >= 500:
            self.backend.restart()

    def sample(self):
        result = {'protocol': 'fc-device/1', 'sourceId': self.identity, 'signal': 'illuminance',
                  'unit': 'lx', 'valid': False, 'value': None, 'sequence': self.sequence, 'uptimeMs': self.backend.now()}
        try:
            result['value'] = self.backend.measure(); result['valid'] = True
            self.sequence += 1; result['sequence'] = self.sequence
            self.history.append([self.sequence, result['uptimeMs'], result['value']])
            if len(self.history) > 64: self.history.pop(0)
        except OSError:
            pass
        return result

    def serial(self, line):
        parts = line.strip().split(' ', 2)
        if len(parts) < 2 or len(parts[1]) != 32 or any(c not in '0123456789abcdef' for c in parts[1]):
            return None
        command, request = parts[:2]
        if command == 'PING': result = self.hello()
        elif command == 'READ':
            s = self.sample(); result = {'sourceId': self.identity, 'valid': s['valid'], 'lux': s['value']}
        elif command == 'HISTORY': result = {'sourceId': self.identity, 'timebase': 'ticks_ms', 'capacity': 64, 'samples': self.history}
        elif command == 'FC' and len(parts) == 3 and len(parts[2].encode()) <= 2048:
            try:
                data = json.loads(parts[2]); result = self.handle(data.get('operation'), data.get('arguments'), 'usb')
            except (ValueError, TypeError, AttributeError, OSError):
                result = {'ok': False, 'sourceId': self.identity, 'error': 'invalid_request'}
        else: return None
        result['requestId'] = request
        return json.dumps(result)
