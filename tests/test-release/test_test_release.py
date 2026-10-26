import importlib.util,io,json,unittest,zipfile
from pathlib import Path
root=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('release',root/'scripts/publish-test-release.py')
r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)
class ReleaseTests(unittest.TestCase):
    def setUp(self):self.m=json.loads((root/'.github/test-release.json').read_text())
    def test_manifest(self):r.validate_manifest(self.m)
    def test_stable_or_changed_source_rejected(self):
        for key,value in [('tag','v0.3.201'),('sourceCommit','main'),('branch','main'),('requiredRuns',[])]:
            with self.subTest(key=key),self.assertRaises(ValueError):r.validate_manifest(dict(self.m,**{key:value}))
    def test_only_successful_exact_source(self):
        good={'head_sha':self.m['sourceCommit'],'status':'completed','conclusion':'success','event':'pull_request','head_repository':{'full_name':self.m['repository']}}
        r.validate_run(good,self.m)
        for key,value in [('head_sha','0'*40),('conclusion','failure'),('conclusion','cancelled'),('status','in_progress'),('head_repository',{'full_name':'another/repo'})]:
            with self.subTest(key=key),self.assertRaises(ValueError):r.validate_run(dict(good,**{key:value}),self.m)
    def test_unsafe_archive_rejected(self):
        for name in ['../secret','/absolute','dir\\secret']:
            data=io.BytesIO()
            with zipfile.ZipFile(data,'w') as z:z.writestr(name,b'test')
            with self.assertRaises(ValueError):r.inspect_zip(data.getvalue())
    def test_repeated_asset_names_rejected(self):
        self.m['artifacts']*=2
        with self.assertRaises(ValueError):r.validate_manifest(self.m)
if __name__=='__main__':unittest.main()
