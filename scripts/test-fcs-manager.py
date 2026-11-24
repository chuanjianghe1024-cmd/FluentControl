"""Real loopback manager authorization checks; never writes to a device."""
import json, subprocess, sys, urllib.request, urllib.error
from pathlib import Path
exe=str(Path(sys.argv[1]).resolve())
p=subprocess.Popen([exe,'--manage-headless'],stdout=subprocess.PIPE,text=True)
try:
    address=p.stdout.readline().strip()
    base,token=address.split('#')
    def fetch(path, body=None, headers=None):
        request=urllib.request.Request(base.rstrip('/')+path,data=body,headers=headers or {})
        try:
            with urllib.request.urlopen(request,timeout=15) as response: return response.status,response.read()
        except urllib.error.HTTPError as e: return e.code,e.read()
    assert fetch('/')[0]==200
    assert fetch('/manager.js')[0]==200
    payload=b'{"operation":"list"}'
    assert fetch('/rpc',payload)[0]==403
    assert fetch('/rpc',payload,{'Authorization':'Bearer wrong'})[0]==403
    assert fetch('/rpc',payload,{'Authorization':'Bearer '+token,'Origin':'https://evil.example'})[0]==403
    status,data=fetch('/rpc',payload,{'Authorization':'Bearer '+token,'Origin':base.rstrip('/')})
    assert status==200 and json.loads(data)['protocol']=='fcs-gateway/1'
    status,data=fetch('/rpc',b'{"operation":"manager.capabilities"}',{'Authorization':'Bearer '+token})
    capabilities=json.loads(data)
    assert status==200 and capabilities['managerVersion']==1
    assert capabilities['capabilities']['usbManagement'] and capabilities['capabilities']['recovery']
    assert not capabilities['capabilities']['nodeConfiguration']
    assert fetch('/rpc',b'{"operation":"nodes.save","deviceId":"unknown","ip":"192.168.1.2"}',{'Authorization':'Bearer '+token})[0]==400
    assert fetch('/rpc',b'{"operation":"shutdown"}',{'Authorization':'Bearer '+token})[0]==400
    assert fetch('/package',b'not a recovery zip',{'Authorization':'Bearer '+token})[0]==400
    print('FCS manager: real HTTP authentication, origin and package rejection checks passed')
finally:
    p.terminate();p.wait(timeout=10)
    subprocess.run([exe,'--stop'],timeout=10,check=True)
