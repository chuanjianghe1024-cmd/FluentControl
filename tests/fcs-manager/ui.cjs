const {chromium}=require('playwright');
const fs=require('node:fs');const path=require('node:path');const assert=require('node:assert/strict');
(async()=>{const browser=await chromium.launch({headless:true,executablePath:process.env.FCS_TEST_BROWSER,channel:process.platform==='win32'?'msedge':undefined});try{
const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));const calls=[];
const folder=path.resolve('tools/fcs-manager');const token='fixture-secret';let enabled=false;
await page.route('http://127.0.0.1:18765/**',async route=>{const req=route.request(),p=new URL(req.url()).pathname;
if(req.method()==='GET'){return route.fulfill({contentType:p==='/manager.js'?'text/javascript':'text/html',body:fs.readFileSync(path.join(folder,p==='/manager.js'?'manager.js':'manager.html'))})}
assert.equal(req.headers().authorization,'Bearer '+token);const q=JSON.parse(req.postData());calls.push(q);let r={protocol:'fcs-gateway/1'};
if(q.operation==='manager.capabilities')r={...r,managerVersion:1,platform:'windows',capabilities:{usbDiscovery:true,usbManagement:true,recovery:true,scriptsList:true,nodeConfiguration:false},scriptCommand:'FluentControl.FcsGateway.exe',limitations:'USB required'};
if(q.operation==='list'||q.operation==='discover')r.devices=[{deviceId:'fixture-c3',status:'online',usbPort:'COM7',ip:'192.168.1.42'},{deviceId:'lan-only',status:'online',ip:'192.168.1.43'}];
if(q.operation==='usb.discover')r.usbDevices=[];
if(q.operation==='scripts.list')r.scripts=[{id:'gateway-rule',enabled:false,status:'configured'}];
if(q.operation==='recovery.ports')r.ports=['COM7','COM8'];
if(q.operation==='recovery.inspect'){if(q.port==='COM8')r.error='identity_missing';else r.identity={board:'fcs-c3-bh1750-gpio4-3',mac:'102030405060',identity:'a'.repeat(64)};}
if(q.operation==='usb.manage')r.result={ok:true,sourceId:q.deviceId,state:'ready'};
if(q.operation==='node.request'){
r.result={ok:true,protocol:'fcs-node/2',deviceId:q.deviceId};
if(q.command==='sensors.list')r.result.sensors=[{id:'ambient-01',model:'BH1750',driver:{status:'ready'},signals:[{id:'illuminance',unit:'lx',readable:true}]}];
if(q.command==='plugin.list')r.result.plugins=[{id:'wol',enabled,settings:{mac:'02:00:00:00:00:01'}}];
if(q.command==='plugin.configure')enabled=q.arguments.enabled;
if(q.command==='rules.list')r.result.rules=[];
}
return route.fulfill({contentType:'application/json',body:JSON.stringify(r)});
});
await page.goto('http://127.0.0.1:18765/#'+token);await page.getByRole('heading',{name:'◉ fixture-c3'}).waitFor();assert.equal(new URL(page.url()).hash,'');
await page.getByRole('button',{name:'管理设备'}).first().click();await page.locator('#details').filter({hasText:'BH1750'}).waitFor();
await page.locator('#plugins').click();await page.locator('#plugin-id option').waitFor({state:'attached'});assert.match(await page.locator('#plugin-settings').inputValue(),/02:00/);
// Dismissing a destructive action must not send a command.
page.once('dialog',d=>d.dismiss());await page.locator('#factory').click();assert.equal(calls.filter(x=>x.command==='factory.reset').length,0);
page.once('dialog',d=>d.accept());await page.locator('#enable').click();await page.waitForFunction(()=>document.getElementById('status').textContent==='操作完成');assert.equal(enabled,true);
await page.locator('#close-detail').click();await page.getByRole('button',{name:'管理设备'}).nth(1).click();await page.locator('#device-title').filter({hasText:'lan-only'}).waitFor();assert.equal(await page.locator('#wifi-save').isDisabled(),true);assert.equal(await page.locator('#install').isDisabled(),true);
await page.locator('#close-detail').click();await page.locator('#scripts').click();await page.locator('#script-list').filter({hasText:'gateway-rule'}).waitFor();await page.locator('#close-gateway').click();
await page.locator('#recover').click();await page.locator('#port option').first().waitFor({state:'attached'});assert.equal(await page.locator('#flash').isDisabled(),true);
await page.locator('#verify-device').click();await page.locator('#identity-result').filter({hasText:'102030405060'}).waitFor();assert.equal(await page.locator('#flash').isDisabled(),false);
await page.locator('#flash').click();assert.equal(calls.filter(x=>x.operation==='recovery.start').length,0);
await page.selectOption('#port','COM8');assert.equal(await page.locator('#flash').isDisabled(),true);
await page.locator('#verify-device').click();await page.locator('#recovery-log').filter({hasText:'identity_missing'}).waitFor();assert.equal(await page.locator('#flash').isDisabled(),true);
assert.deepEqual(errors,[]);console.log('Manager UI: discovery, device details, plugin settings, cancel boundaries, LAN permissions and recovery confirmation passed');
}finally{await browser.close()}})().catch(e=>{console.error(e);process.exitCode=1});
