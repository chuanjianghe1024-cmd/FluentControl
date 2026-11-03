"""Offline template contract checks, not a running HA acceptance test."""
import copy,unittest
from pathlib import Path
import yaml,jinja2
class Loader(yaml.SafeLoader):pass
Loader.add_constructor('!secret',lambda loader,node:loader.construct_scalar(node))
class HomeAssistantTemplateTests(unittest.TestCase):
    def setUp(self):
        cfg=yaml.load((Path(__file__).resolve().parents[2]/'docs/examples/fcs-home-assistant.yaml').read_text(),Loader=Loader)['sensor'][0]
        self.availability=jinja2.Environment().from_string(cfg['availability'])
        self.value=jinja2.Environment().from_string(cfg['value_template'])
        self.frame={'protocol':'fcs-gateway/1','sampleAgeMilliseconds':0,'maxAgeMilliseconds':5000,
                    'result':{'protocol':'fcs-node/2','deviceId':'c3-replace-me','ok':True,'data':[{'sensorId':'ambient-01','signalId':'illuminance','unit':'lx','valueType':'number','valid':True,'fresh':True,'value':123}]}}
    def available(self,frame):return self.availability.render(value_json=frame).strip()=='True'
    def test_valid_signal(self):
        self.assertTrue(self.available(self.frame));self.assertEqual(self.value.render(value_json=self.frame).strip(),'123')
    def test_invalid_identity_age_type_and_duplicates(self):
        changes=[lambda r:r.update(protocol='wrong'),lambda r:r.update(sampleAgeMilliseconds=5001),lambda r:r.update(sampleAgeMilliseconds=-1),
                 lambda r:r['result'].update(deviceId='other'),lambda r:r['result']['data'].append(copy.deepcopy(r['result']['data'][0]))]
        for key,value in [('valid',False),('fresh',False),('value',None),('value',True),('value',-1),('value',200001),('unit','%')]:
            changes.append(lambda r,k=key,v=value:r['result']['data'][0].update({k:v}))
        for change in changes:
            frame=copy.deepcopy(self.frame);change(frame);self.assertFalse(self.available(frame))
        self.assertFalse(self.available({}));self.assertFalse(self.available({'error':'offline'}))
if __name__=='__main__':unittest.main()
