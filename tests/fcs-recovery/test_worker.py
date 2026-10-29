import hashlib
import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location('worker', Path(__file__).resolve().parents[2]/'tools/fcs-recovery/worker.py')
w = importlib.util.module_from_spec(spec); spec.loader.exec_module(w)
MAC = bytes.fromhex('102030405060')

def marker(mac=MAC):
    header = b'FCSID001' + mac + b'\x01\x00' + b'fcs-c3-bh1750-gpio4-3'.ljust(32,b'\0')
    return header + hashlib.sha256(header).digest() + b'\xff'*(4096-80)

def image():
    data = bytearray(b'\xff'*65536); data[0] = 0xe9
    entries = [(1,2,0x9000,0x6000,b'nvs'),(1,1,0xf000,0x1000,b'phy_init'),(0,0,0x10000,0x1f0000,b'factory'),(1,0x81,0x200000,0x1ff000,b'vfs'),(1,0x40,0x3ff000,4096,b'fcs_id')]
    for i, (t,st,o,s,n) in enumerate(entries):
        struct.pack_into('<HBBII16sI',data,0x8000+32*i,0x50aa,t,st,o,s,n,0)
    return bytes(data)

class Fake:
    CHIP_NAME='ESP32-C3'; secure_download_mode=False
    def __init__(self): self.data=marker(); self.calls=[]; self.mac=MAC; self.size='4MB'; self.fail=False
    def get_secure_boot_enabled(self): return False
    def get_flash_encryption_enabled(self): return False
    def read_mac(self): return self.mac
    def detect_flash_size(self,esp): return self.size
    def read_flash(self,esp,offset,size,**kw):
        assert esp is self and offset==w.OFFSET and size==4096
        return self.data
    def erase_region(self,esp,offset,size):
        assert esp is self and (offset,size)==(0,w.OFFSET)
        self.calls.append('erase')
    def write_flash(self,esp,parts,**kw):
        assert esp is self and parts[0][0]==0 and len(parts[0][1])<=0x200000
        self.calls.append('write')
        if self.fail: raise OSError('Disconnected')
    def verify_flash(self,esp,parts,**kw): self.calls.append('verify')

class RecoveryTests(unittest.TestCase):
    def reject(self, f, fingerprint=None, data=None):
        with self.assertRaises(ValueError): w.operate(f,f,image() if data is None else data,fingerprint or hashlib.sha256(marker()).hexdigest())
        self.assertEqual(f.calls,[])
    def test_foreign_blank_corrupt_and_copied_marker_never_erase(self):
        for data in [b'\xff'*4096, b'\0'*4096, marker()[:-1]+b'\0', marker(b'\x11'*6)]:
            f=Fake(); f.data=data; self.reject(f)
    def test_wrong_model_chip_capacity_and_changed_device_never_erase(self):
        f=Fake(); f.CHIP_NAME='ESP32-S3'; self.reject(f)
        f=Fake(); f.size='8MB'; self.reject(f)
        f=Fake(); self.reject(f,'0'*64)
        f=Fake(); f.data=f.data[:16]+b'x'+f.data[17:]; self.reject(f)
    def test_bad_partition_and_oversize_never_erase(self):
        f=Fake(); b=bytearray(image()); b[0x8080]=0; self.reject(f,data=b)
        self.reject(Fake(),data=image()+b'\0'*0x200000)
    def test_probe_has_no_writes_and_recovery_preserves_marker(self):
        f=Fake(); before=f.data; result=w.operate(f,f); self.assertEqual(f.calls,[])
        w.operate(f,f,image(),result['identity'])
        self.assertEqual(f.calls,['erase','write','verify']); self.assertEqual(f.data,before)
    def test_interrupted_write_preserves_identity_and_does_not_retry(self):
        f=Fake(); f.fail=True
        with self.assertRaises(OSError): w.operate(f,f,image(),hashlib.sha256(f.data).hexdigest())
        self.assertEqual(f.calls,['erase','write']); self.assertEqual(f.data,marker())
