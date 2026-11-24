"""Single-connection FCS recovery; esptool 5.1.0. No enrollment or force path.
Distributed alongside esptool under GPL-2.0-or-later; see LICENSE-esptool.txt.
"""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path

OFFSET = 0x3ff000
SIZE = 4096
BOARD = b'fcs-c3-bh1750-gpio4-3'


def validate_marker(data, mac):
    mac = bytes(mac)
    if len(mac) != 6 or mac in (b'\0' * 6, b'\xff' * 6):
        raise ValueError('Invalid chip MAC')
    header = b'FCSID001' + mac + b'\x01\x00' + BOARD + b'\0' * (32 - len(BOARD))
    expected = header + hashlib.sha256(header).digest() + b'\xff' * (SIZE - 80)
    if data != expected:
        raise ValueError('FCS identity missing, damaged, wrong board or bound to another chip. No erase performed.')
    return {'board': BOARD.decode(), 'mac': mac.hex(), 'identity': hashlib.sha256(data).hexdigest()}


def validate_image(data):
    if not 65536 <= len(data) <= 0x200000 or data[0] != 0xe9:
        raise ValueError('Invalid FCS image size/header')
    expected = [(1, 2, 0x9000, 0x6000, b'nvs'), (1, 1, 0xf000, 0x1000, b'phy_init'),
                (0, 0, 0x10000, 0x1f0000, b'factory'),
                (1, 0x81, 0x200000, 0x1ff000, b'vfs'),
                (1, 0x40, OFFSET, SIZE, b'fcs_id')]
    for index, entry in enumerate(expected):
        magic, kind, subtype, offset, size, label, flags = struct.unpack_from('<HBBII16sI', data, 0x8000 + index * 32)
        if magic != 0x50aa or flags != 0 or (kind, subtype, offset, size, label.rstrip(b'\0')) != entry:
            raise ValueError('Image does not reserve the supported FCS identity partition')
    # No extra partition may overlap the identity or filesystem.
    if data[0x80a0:0x80a2] not in (b'\xeb\xeb', b'\xff\xff'):
        raise ValueError('Unexpected extra partition')


def operate(esp, api, image=None, expected=None):
    """All validation and flash I/O use one already-open physical connection."""
    if esp.CHIP_NAME != 'ESP32-C3' or esp.secure_download_mode:
        raise ValueError('Only unencrypted ESP32-C3 FCS devices are supported')
    if esp.get_secure_boot_enabled() or esp.get_flash_encryption_enabled():
        raise ValueError('Protected chip is not supported; no eFuse changes are allowed')
    if api.detect_flash_size(esp) != '4MB':
        raise ValueError('Expected exactly 4 MB flash')
    mac = esp.read_mac()
    marker = api.read_flash(esp, OFFSET, SIZE, no_progress=True)
    identity = validate_marker(marker, mac)
    if image is not None:
        validate_image(image)
        if not expected or identity['identity'] != expected:
            raise ValueError('Device changed since verification. No erase performed.')
        # Re-read immediately before the first destructive command; no reconnect/retry.
        validate_marker(api.read_flash(esp, OFFSET, SIZE, no_progress=True), mac)
        api.erase_region(esp, 0, OFFSET)
        api.write_flash(esp, [(0, image)], flash_size='keep', compress=True, no_progress=True)
        api.verify_flash(esp, [(0, image)], flash_size='keep')
        after = api.read_flash(esp, OFFSET, SIZE, no_progress=True)
        if after != marker:
            raise ValueError('Identity changed during recovery; do not report success')
    return identity


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--port', required=True)
    p.add_argument('--image')
    p.add_argument('--expected')
    a = p.parse_args()
    if not re.fullmatch(r'COM[1-9][0-9]{0,4}', a.port, re.I):
        p.error('Explicit local COM port required')
    if bool(a.image) != bool(a.expected) or (a.expected and not re.fullmatch('[a-f0-9]{64}', a.expected)):
        p.error('Recovery requires the verified identity fingerprint')
    image = Path(a.image).read_bytes() if a.image else None
    if image is not None:
        validate_image(image)
    from esptool import cmds as api
    esp = api.detect_chip(a.port, baud=115200, connect_attempts=1)
    try:
        if esp.CHIP_NAME != 'ESP32-C3':
            raise ValueError('Not an ESP32-C3; no flash written')
        esp = api.run_stub(esp)
        api.attach_flash(esp)
        result = operate(esp, api, image, a.expected)
        print('FCS_RESULT=' + json.dumps(result), flush=True)
        api.reset_chip(esp)
    finally:
        esp._port.close()


if __name__ == '__main__':
    main()
