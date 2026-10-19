import hashlib, hmac, importlib.util, json, unittest
from pathlib import Path
spec = importlib.util.spec_from_file_location('fc_device', Path(__file__).resolve().parents[2]/'tools/light-source/device-v1/fc_device.py')
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
class Backend:
    wifi = True
    def __init__(self): self.config={'ssid':'old','password':'old-secret','bindingKey':'11'*32}; self.time=0; self.online=False; self.saves=0; self.restarts=0
    def load(self): return dict(self.config)
    def save(self,c): self.config=dict(c); self.saves+=1
    def random_key(self): return '22'*32
    def connect(self,s,p): self.target=(s,p); self.online=False
    def disconnect(self): self.online=False
    def disable(self): self.online=False
    def connected(self): return self.online
    def ip(self): return '192.0.2.1' if self.online else ''
    def scan(self): return ['test']
    def now(self): return self.time
    def elapsed(self,t): return self.time-t
    def restart(self): self.restarts+=1
    def measure(self): return 42.5
class Tests(unittest.TestCase):
    def setUp(self): self.b=Backend(); self.d=m.Device('c3-test',self.b)
    def test_usb_admin_needs_no_binding(self):
        self.assertTrue(self.d.handle('wifi.forget')['ok']);self.assertNotIn('ssid',self.b.config)
    def test_wireless_cannot_claim_usb_in_payload(self):
        for t in ['wifi','ble','unknown']:
            self.assertFalse(self.d.handle('wifi.forget',{'transport':'usb'},t)['ok'])
        self.assertEqual(self.b.config['ssid'],'old')
    def test_cancel_keeps_known_good(self):
        self.d.handle('wifi.configure',{'ssid':'new','password':'new-secret'})
        self.assertEqual(self.b.config['ssid'],'old')
        self.d.handle('wifi.cancel');self.assertEqual(self.b.target,('old','old-secret'));self.assertIsNone(self.d.pending)
    def test_failure_restores_old(self):
        self.d.handle('wifi.configure',{'ssid':'new','password':'new-secret'});self.b.time=21000;self.d.tick()
        self.assertEqual(self.b.target,('old','old-secret'));self.assertEqual(self.b.config['ssid'],'old')
    def test_success_persists_once(self):
        self.d.handle('wifi.configure',{'ssid':'new','password':'new-secret'});self.b.online=True;self.d.tick();self.d.tick()
        self.assertEqual(self.b.config['ssid'],'new');self.assertEqual(self.b.saves,1)
    def test_disable_preserves_credentials_and_usb(self):
        self.d.handle('wireless.disable');self.assertEqual(self.b.config['ssid'],'old');self.assertTrue(self.d.handle('status')['ok'])
        self.d.handle('wireless.enable');self.assertEqual(self.b.target,('old','old-secret'))
    def test_factory_reset_retains_identity(self):
        self.d.handle('factory.reset');self.assertNotIn('password',self.b.config);self.assertEqual(self.d.hello()['sourceId'],'c3-test')
    def test_rotate_revokes_old(self):
        old=m.mac(self.d.config['bindingKey'],'test');self.d.handle('binding.rotate');self.assertFalse(m.equal(old,m.mac(self.d.config['bindingKey'],'test')))
    def test_reboot_ack_before_restart(self):
        self.assertEqual(self.d.handle('reboot')['state'],'restart_accepted');self.assertEqual(self.b.restarts,0)
        self.b.time=500;self.d.tick();self.assertEqual(self.b.restarts,1)
    def test_hmac_matches_standard(self):
        key='11'*32;text='READ\nc3-test\n'+'a'*32
        self.assertEqual(m.mac(key,text),hmac.new(bytes.fromhex(key),text.encode(),hashlib.sha256).hexdigest())
    def test_serial_correlation_and_no_secret_read(self):
        result=json.loads(self.d.serial('FC '+'a'*32+' '+json.dumps({'operation':'status'})))
        self.assertEqual(result['requestId'],'a'*32);self.assertNotIn('password',json.dumps(result))
        self.assertNotIn('bindingKey',json.dumps(self.d.sample()))
    def test_bounds_and_identity(self):
        for _ in range(70): self.d.sample()
        self.assertEqual(len(self.d.history),64);self.assertEqual(self.d.history[0][0],7)
        self.assertIsNone(self.d.serial('FC bad {}'))
        self.assertFalse(self.d.handle('wifi.configure',{'ssid':'汉'*12,'password':''})['ok'])
    def test_non_wifi_board_capabilities(self):
        self.b.wifi=False;self.d=m.Device('rp-test',self.b)
        self.assertFalse(self.d.handle('wifi.configure',{'ssid':'test','password':''})['ok'])
        self.assertTrue(self.d.handle('status')['ok'])
if __name__=='__main__':unittest.main()
