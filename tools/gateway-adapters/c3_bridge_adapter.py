"""Trusted C3 bridge reader -> existing local, explicitly granted FCS producer."""
import argparse
import hashlib
import hmac
import http.client
import ipaddress
import json
import math
from pathlib import Path
import re
import secrets
import signal
import threading
import time

from common import fields, identity, load, Publisher

TYPES = {'battery': ('number', '%'), 'temperature': ('number', 'Cel'),
         'humidity': ('number', '%'), 'illuminance': ('number', 'lx'),
         'motion': ('boolean', '1'), 'opening': ('boolean', '1')}

def validate(config):
    fields(config, ('adapterId', 'endpoint', 'bridge', 'inputs'))
    bridge = config['bridge']
    fields(bridge, ('ip', 'deviceId', 'bindingKeyFile'))
    ip = ipaddress.IPv4Address(bridge['ip'])
    if not ip.is_private or ip.is_loopback or ip.is_multicast or ip.is_unspecified:
        raise ValueError('Expected explicit private LAN IPv4')
    if not re.fullmatch(r'[A-Za-z0-9_-]{1,100}', bridge['deviceId']):
        raise ValueError('Invalid bridge identity')
    if not isinstance(config['inputs'], list) or not 1 <= len(config['inputs']) <= 32:
        raise ValueError('Expected 1..32 selected signals')
    selected, targets = set(), set()
    for item in config['inputs']:
        fields(item, ('deviceId', 'sensorId', 'signalId', 'staleMilliseconds', 'sourceId', 'sourceSignal'))
        target = identity(item, config['adapterId'])
        if not re.fullmatch(r'(espnow|ble)-[0-9a-f]{12}', item['sourceId']) or item['sourceSignal'] not in TYPES:
            raise ValueError('Unsupported remote signal')
        if item['sourceId'].startswith('espnow-') and item['sourceSignal'] != 'illuminance':
            raise ValueError('ESP-NOW preview supports BH1750 illuminance')
        key = (item['sourceId'], item['sourceSignal'])
        if key in selected or target in targets:
            raise ValueError('Duplicate source or output')
        selected.add(key)
        targets.add(target)

class Reader:
    def __init__(self, config, publisher):
        validate(config)
        self.config, self.publisher = config, publisher
        key = Path(config['bridge']['bindingKeyFile']).read_text().strip()
        if not re.fullmatch(r'[0-9a-f]{64}', key):
            raise ValueError('Expected private C3 binding-key file')
        self.key = bytes.fromhex(key)
        self.seen = {}
        self.inputs = {(x['sourceId'], x['sourceSignal']): x for x in config['inputs']}

    def accept(self, raw, signature, nonce, started, elapsed):
        expected = hmac.new(self.key, nonce.encode() + b'\n' + raw, hashlib.sha256).hexdigest()
        if not isinstance(signature, str) or not hmac.compare_digest(expected, signature):
            raise ValueError('Bridge signature mismatch')
        root = json.loads(raw)
        if (root.get('protocol') != 'fcs-node/2' or root.get('ok') is not True or
                root.get('deviceId') != self.config['bridge']['deviceId']):
            raise ValueError('Unexpected bridge envelope')
        frame = root['extensions']['fcs.bridge']
        fields(frame, ('protocol', 'bootId', 'data'))
        if frame['protocol'] != 'fcs-bridge/1' or not re.fullmatch('[0-9a-f]{32}', frame['bootId']):
            raise ValueError('Unexpected bridge session')
        if not isinstance(frame['data'], list) or len(frame['data']) > 32:
            raise ValueError('Bridge capacity exceeded')
        pending, keys = [], set()
        for sample in frame['data']:
            fields(sample, ('sourceId', 'signalId', 'valueType', 'unit', 'value', 'valid',
                            'sequence', 'ageMilliseconds', 'trust'))
            key = (sample['sourceId'], sample['signalId'])
            if key in keys:
                raise ValueError('Duplicate bridge signal')
            keys.add(key)
            item = self.inputs.get(key)
            if item is None:
                continue
            expected_type, unit = TYPES[item['sourceSignal']]
            trust = 'paired-key' if key[0].startswith('espnow-') else 'unauthenticated-broadcast'
            if sample['valueType'] != expected_type or sample['unit'] != unit or sample['trust'] != trust:
                raise ValueError('Unexpected signal metadata')
            age, sequence, valid, value = (sample[k] for k in ('ageMilliseconds', 'sequence', 'valid', 'value'))
            if (type(age) is not int or not 0 <= age < 2**31 or type(sequence) is not int or
                    not 0 <= sequence < 2**53 or type(valid) is not bool):
                raise ValueError('Invalid signal metadata')
            if valid:
                if expected_type == 'boolean':
                    if type(value) is not bool: raise ValueError('Invalid boolean')
                elif type(value) not in (int, float) or not math.isfinite(value):
                    raise ValueError('Invalid number')
            elif value is not None:
                raise ValueError('Invalid sample must be null')
            previous = self.seen.get(key)
            if previous and previous[0] == frame['bootId'] and sequence <= previous[1]:
                continue
            if age + elapsed * 1000 > item['staleMilliseconds']:
                continue
            pending.append((key, item, value, started - age / 1000, sequence))
        # Validate the complete frame before publishing any sample.
        for key, item, value, received, sequence in pending:
            self.publisher.put(item, value, received)
            self.seen[key] = (frame['bootId'], sequence)

    def poll(self):
        nonce = secrets.token_hex(16)
        bridge = self.config['bridge']
        signature = hmac.new(self.key, ('BRIDGE-READ\n' + bridge['deviceId'] + '\n' + nonce).encode(), hashlib.sha256).hexdigest()
        started, monotonic = time.time(), time.monotonic()
        connection = http.client.HTTPConnection(bridge['ip'], 8766, timeout=2)
        try:
            connection.request('GET', '/v2/bridge', headers={'X-FC-Nonce': nonce, 'X-FC-Signature': signature})
            response = connection.getresponse()
            raw = response.read(32769)
            if response.status != 200 or len(raw) > 32768:
                raise ValueError('Bridge unavailable or oversized response')
            elapsed = time.monotonic() - monotonic
            if elapsed > 3: raise ValueError('Bridge response too old')
            self.accept(raw, response.getheader('X-FC-Signature'), nonce, started, elapsed)
        finally:
            connection.close()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('configuration')
    config = load(parser.parse_args().configuration)
    validate(config)
    publisher = Publisher(config)
    reader = Reader(config, publisher)
    stop = threading.Event()
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, lambda *_: stop.set())
    while not stop.is_set():
        try:
            reader.poll()
        except (OSError, ValueError, KeyError, TypeError, AttributeError, http.client.HTTPException):
            pass  # Other Gateway sources remain independent; samples expire normally.
        publisher.flush()
        stop.wait(.5)

if __name__ == '__main__':
    main()
