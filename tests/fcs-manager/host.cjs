// Exercise the shared UI against the real Linux HTTP host, not mocked RPCs.
const {chromium}=require('playwright');
const fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const {spawn}=require('node:child_process');
const assert=require('node:assert/strict');
const net=require('node:net');
(async()=>{
  const directory=fs.mkdtempSync(path.join(os.tmpdir(),'fcs-manager-'));
  const socket=net.createServer();await new Promise(resolve=>socket.listen(0,'127.0.0.1',resolve));
  const port=socket.address().port;await new Promise(resolve=>socket.close(resolve));
  const base='http://127.0.0.1:'+port;
  let server,browser,logs='';
  async function start(){
    server=spawn(path.resolve(process.argv[2]),[],{env:{...process.env,FCS_DATA:directory,ASPNETCORE_URLS:base},stdio:['ignore','pipe','pipe']});
    server.stdout.on('data',x=>logs+=x);server.stderr.on('data',x=>logs+=x);
    for(let i=0;i<100;i++){try{if((await fetch(base+'/healthz')).ok)return}catch{}await new Promise(r=>setTimeout(r,100))}
    throw Error('Host did not start: '+logs);
  }
  async function stop(){if(server&&server.exitCode===null){const exited=new Promise(r=>server.once('exit',r));server.kill('SIGTERM');await exited}}
  try{
    await start();
    browser=await chromium.launch({headless:true,executablePath:process.env.FCS_TEST_BROWSER});
    const page=await browser.newPage();const errors=[],calls=[];
    page.on('pageerror',e=>errors.push(e.message));
    page.on('request',r=>{if(r.method()==='POST')calls.push(JSON.parse(r.postData()))});
    await page.goto(base);await page.locator('#login').waitFor({state:'visible'});
    assert.equal(await page.locator('#scan').isDisabled(),true);
    // The existing FC reader token must not turn into a browser administration credential.
    await page.locator('#manager-token').fill(fs.readFileSync(path.join(directory,'gateway.token'),'utf8').trim());
    await page.locator('#connect').click();
    await page.locator('#status').filter({hasText:'权限不足'}).waitFor();
    assert.equal(await page.locator('#scan').isDisabled(),true);
    await page.locator('#manager-token').fill(fs.readFileSync(path.join(directory,'manager.token'),'utf8').trim());
    await page.locator('#connect').click();
    await page.locator('#host-label').filter({hasText:'Linux / Docker'}).waitFor();
    assert.equal(await page.locator('#recover').isDisabled(),true);
    assert.equal(await page.locator('#manager-token').inputValue(),'');
    await page.locator('#add-node').click();
    await page.locator('#node-id').fill('fixture-c3');await page.locator('#node-ip').fill('8.8.8.8');
    await page.locator('#save-node').click();await page.locator('#status').filter({hasText:'操作失败'}).waitFor();
    assert.equal(JSON.parse(fs.readFileSync(path.join(directory,'nodes.json'),'utf8')).Nodes.length,0);
    await page.locator('#node-ip').fill('192.168.254.250');await page.locator('#save-node').click();
    await page.getByRole('heading',{name:'◉ fixture-c3'}).waitFor();
    // Registration survives an actual host restart, without restoring historical live data.
    await stop();await start();await page.locator('#refresh').click();
    await page.getByRole('button',{name:'管理设备'}).click();
    await page.locator('#device-title').filter({hasText:'fixture-c3'}).waitFor();
    for(const id of ['wifi-save','install','plugins','rules'])assert.equal(await page.locator('#'+id).isDisabled(),true);
    page.once('dialog',d=>d.dismiss());await page.locator('#remove-node').click();
    assert.equal(calls.filter(c=>c.operation==='nodes.remove').length,0);
    page.once('dialog',d=>d.accept());await page.locator('#remove-node').click();
    await page.locator('#devices').filter({hasText:'尚未发现'}).waitFor();
    assert.equal(JSON.parse(fs.readFileSync(path.join(directory,'nodes.json'),'utf8')).Nodes.length,0);
    await page.locator('#scripts').click();await page.locator('#gateway').waitFor({state:'visible'});
    assert.match(await page.locator('#gateway').textContent(),/FluentControl.FcsHost/);
    await page.locator('#close-gateway').click();
    await page.locator('#scan').click();await page.locator('#status').filter({hasText:/操作(完成|失败)/}).waitFor();
    assert.equal(calls.filter(c=>c.operation==='discover').length,1); // Broadcast may be unavailable in an isolated runner.
    assert.equal(calls.filter(c=>c.operation==='usb.discover').length,0);
    await page.locator('#disconnect').click();assert.equal(await page.locator('#scan').isDisabled(),true);
    assert.equal(await page.evaluate(()=>localStorage.length+sessionStorage.length),0);
    assert.deepEqual(errors,[]);
    console.log('Linux manager: real HTTP login, credential separation, capability gates, persistence/restart, remove confirmation, scripts and logout passed');
  }finally{if(browser)await browser.close();await stop();fs.rmSync(directory,{recursive:true,force:true})}
})().catch(e=>{console.error(e);process.exitCode=1});
