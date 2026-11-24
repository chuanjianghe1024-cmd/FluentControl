"""Exercise shipped CLI/worker end to end; never executes network actions."""
import json,os,subprocess,sys,tempfile
from pathlib import Path
exe=sys.argv[1:]
if not exe: raise SystemExit('Provide host/gateway command')
env=dict(os.environ,FCS_PYTHON=sys.executable)
with tempfile.TemporaryDirectory() as d:
    root=Path(d);job=root/'script.json';fixture=root/'signals.json'
    def run(*args,ok=True):
        p=subprocess.run(exe+list(map(str,args)),text=True,capture_output=True,env=env,timeout=10)
        assert (p.returncode==0)==ok,(p.returncode,p.stdout,p.stderr)
        return p.stdout
    run('--script-create',job);run('--script-create',job,ok=False)
    run('--script-validate',job)
    fixture.write_text(json.dumps({'desk':{'value':500}}))
    result=json.loads(run('--script-test',job,fixture));assert result['actions']==[{'action':'wol','target':'desktop'}]
    fixture.write_text(json.dumps({'desk':{'value':100}}))
    assert json.loads(run('--script-test',job,fixture))['actions']==[{'action':'rearm'}]
    fixture.write_text(json.dumps({'desk':{'value':250}}))
    assert json.loads(run('--script-test',job,fixture))['actions']==[]
    data=json.loads(job.read_text());data['source']='def run(signals, settings):\n    return [{"action":"wol","target":"missing"}]\n';job.write_text(json.dumps(data))
    run('--script-test',job,fixture,ok=False)
    data['source']='def run(signals, settings):\n    import os\n    return []\n';job.write_text(json.dumps(data))
    run('--script-validate',job,ok=False)
print('PASS: bundled worker, create/validate/dry-run, hysteresis intents, invalid targets and imports.')
