import copy
import hashlib
import hmac
import json
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/gateway-adapters'))
from c3_bridge_adapter import Reader, validate
from common import Publisher

class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        (root/'binding.key').write_text('11'*32)
        (root/'producer.token').write_text('22'*32)
        self.input = dict(deviceId='ext-radio-room',sensorId='climate',signalId='temperature',
                          staleMilliseconds=5000,sourceId='ble-aabbccddeeff',sourceSignal='temperature')
        self.config = dict(adapterId='radio',endpoint=dict(port=18767,tokenFile=str(root/'producer.token')),
                           bridge=dict(ip='192.168.1.42',deviceId='c3-test',bindingKeyFile=str(root/'binding.key')),
                           inputs=[self.input])
        self.publisher = Publisher(self.config)
        self.reader = Reader(self.config,self.publisher)
        self.sample = dict(sourceId='ble-aabbccddeeff',signalId='temperature',valueType='number',unit='Cel',
                           value=21.5,valid=True,sequence=1,ageMilliseconds=1000,trust='unauthenticated-broadcast')
        self.frame = dict(protocol='fcs-node/2',deviceId='c3-test',ok=True,
                          extensions={'fcs.bridge':dict(protocol='fcs-bridge/1',bootId='a'*32,data=[self.sample])})

    def encoded(self, frame=None, nonce='b'*32):
        raw=json.dumps(frame or self.frame).encode()
        signature=hmac.new(bytes.fromhex('11'*32),nonce.encode()+b'\n'+raw,hashlib.sha256).hexdigest()
        return raw,signature,nonce

    def accept(self, elapsed=.25):
        self.reader.accept(*self.encoded(),time.time()-elapsed,elapsed)

    def test_preserves_source_age_and_never_renews_duplicates(self):
        self.accept()
        value=next(iter(self.publisher.pending.values()))
        self.assertAlmostEqual(time.monotonic()-value[0],1.25,delta=.05)
        self.sample['ageMilliseconds']=0
        self.sample['value']=99
        self.accept()
        self.assertEqual(next(iter(self.publisher.pending.values())),value)
        self.frame['extensions']['fcs.bridge']['bootId']='c'*32
        self.accept()
        self.assertEqual(next(iter(self.publisher.pending.values()))[1]['value'],99)

    def test_stale_network_delay_and_invalid_samples(self):
        self.sample['ageMilliseconds']=4900
        self.accept()
        self.assertFalse(self.publisher.pending)
        self.sample.update(ageMilliseconds=0,valid=False,value=None)
        self.accept()
        self.assertFalse(next(iter(self.publisher.pending.values()))[1]['valid'])

    def test_signature_identity_type_and_atomicity(self):
        raw,signature,nonce=self.encoded()
        with self.assertRaises(ValueError):self.reader.accept(raw,signature,'c'*32,time.time(),0)
        with self.assertRaises(ValueError):self.reader.accept(raw+b' ',signature,nonce,time.time(),0)
        for change in ({'unit':'K'}, {'value':True}, {'value':float('nan')}, {'valid':False},
                       {'trust':'paired-key'}, {'ageMilliseconds':-1}, {'sequence':True}):
            saved=copy.deepcopy(self.sample);self.sample.update(change)
            with self.assertRaises(ValueError): self.accept()
            self.sample.clear();self.sample.update(saved)
        self.frame['extensions']['fcs.bridge']['data'].append(dict(self.sample))
        with self.assertRaises(ValueError): self.accept()
        self.assertFalse(self.publisher.pending)
        self.frame['deviceId']='different-c3'
        with self.assertRaises(ValueError): self.accept()

    def test_explicit_source_scope_and_configuration(self):
        self.sample['sourceId']='ble-aabbccddee00'
        self.accept()
        self.assertFalse(self.publisher.pending)
        for address in ('example.com','8.8.8.8','127.0.0.1','224.0.0.1'):
            config=copy.deepcopy(self.config);config['bridge']['ip']=address
            with self.assertRaises(ValueError): validate(config)
        self.config['inputs'].append(dict(self.input))
        with self.assertRaises(ValueError): validate(self.config)

    def test_actual_poll_request_authentication_and_bounded_read(self):
        raw,signature,nonce=self.encoded()
        class Response:
            status=200
            def read(self,size):
                self.size=size
                return raw
            def getheader(self,name):return signature
        response=Response()
        with patch('c3_bridge_adapter.secrets.token_hex',return_value=nonce), patch('c3_bridge_adapter.http.client.HTTPConnection') as client:
            client.return_value.getresponse.return_value=response
            self.reader.poll()
            args,kwargs=client.return_value.request.call_args
            self.assertEqual(args,('GET','/v2/bridge'))
            expected=hmac.new(bytes.fromhex('11'*32),('BRIDGE-READ\nc3-test\n'+nonce).encode(),hashlib.sha256).hexdigest()
            self.assertEqual(kwargs['headers']['X-FC-Signature'],expected)
            self.assertEqual(response.size,32769)
            client.return_value.close.assert_called_once()
        self.assertTrue(self.publisher.pending)

if __name__=='__main__':unittest.main()
