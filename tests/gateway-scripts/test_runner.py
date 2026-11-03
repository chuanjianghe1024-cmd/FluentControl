import importlib.util,json,unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location('runner',Path(__file__).resolve().parents[2]/'tools/gateway-scripts/runner.py')
r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)
class ScriptTests(unittest.TestCase):
    def run_script(self,source,**inputs):return r.execute(dict(source=source,**inputs))
    def test_multisignal_condition_returns_intent_only(self):
        source='def run(signals, settings):\n    if signals["a"]["value"] > settings["above"] and signals["b"]["value"] < 100:\n        return [{"action":"wol","target":"desktop"}]\n    return []\n'
        self.assertEqual(self.run_script(source,signals={'a':{'value':500},'b':{'value':10}},settings={'above':300})['actions'],[{'action':'wol','target':'desktop'}])
        self.assertEqual(self.run_script(source,signals={'a':{'value':20},'b':{'value':10}},settings={'above':300})['actions'],[])
    def test_validation_does_not_run(self):
        self.assertTrue(r.execute({'mode':'validate','source':'def run(signals, settings):\n    return [1/0]\n'})['ok'])
    def test_io_and_code_execution_rejected(self):
        for body in ['import os','return signals.__class__','return eval("1")','return open("x")','while True:\n        x = 1','return [x for x in range(3)]','return __import__("os")','return [2**999]']:
            with self.subTest(body=body),self.assertRaises(ValueError):r.validate('def run(signals, settings):\n    '+body+'\n')
    def test_bounded_loops_and_memory(self):
        for body in ['return range(1000000000)','return ["x" * 1000000000]','return [1e300]']:
            with self.subTest(body=body),self.assertRaises(ValueError):self.run_script('def run(signals, settings):\n    '+body+'\n')
        source='def run(signals, settings):\n    for i in range(64):\n        for j in range(64):\n            for k in range(64):\n                x = i + j + k\n    return []\n'
        with self.assertRaisesRegex(ValueError,'instruction_budget'):self.run_script(source)
    def test_supported_arithmetic_and_for(self):
        source='def run(signals, settings):\n    total = 0\n    for x in range(4):\n        total = total + x\n    return [{"action":"https","target":"cloud","data":{"value":round(total/4,2)}}]\n'
        self.assertEqual(self.run_script(source)['actions'][0]['data']['value'],1.5)
if __name__=='__main__':unittest.main()
