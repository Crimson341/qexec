const {test} = require('node:test');
const assert = require('node:assert/strict');
const {spawn} = require('node:child_process');
const {mkdtempSync,mkdirSync,writeFileSync,rmSync} = require('node:fs');
const {tmpdir} = require('node:os');
const path = require('node:path');
const readline = require('node:readline');
const hostPath = path.resolve(__dirname,'../host/bin/Release/net10.0/Skua.Mac.Host.dll');

test('real C# engine compiles Windows include paths, calls Flash, and stops a running script', {timeout:30000}, async () => {
  const data = mkdtempSync(path.join(tmpdir(),'skua-mac-test-'));
  mkdirSync(path.join(data,'Scripts'));
  writeFileSync(path.join(data,'Scripts','Helper.cs'),`public static class Helper {
    public static System.Type NetworkType => typeof(System.Net.Dns);
    public static System.Type ProcessType => typeof(System.Diagnostics.Process);
    public static string Message =>
#if SKUA_MAC
    "INCLUDE_OK_MAC";
#else
    "INCLUDE_OK_OTHER";
#endif
  }`);
  const script = path.join(data,'Scripts','Probe.cs');
  const coreInclude = process.env.SKUA_TEST_COREBOTS ? '//cs_include '+process.env.SKUA_TEST_COREBOTS+'\n' : '';
  const coreProbe = process.env.SKUA_TEST_COREBOTS ? 'bot.Log(typeof(CoreBots).Assembly.GetName().Name);' : '';
  writeFileSync(script, coreInclude + '//cs_include Scripts\\Helper.cs\nusing Skua.Core.Interfaces;\npublic class Probe { public void ScriptMain(IScriptInterface bot) { bot.Log(Helper.Message); if (!bot.Manager.CompiledScript.Contains("public static class Helper")) throw new System.Exception("Included source missing from CompiledScript"); ' + coreProbe + ' if (!bot.Flash.Call<bool>("probeBoolean", true)) throw new System.Exception("Boolean bridge failed"); bot.Log("BRIDGE_OK"); while (true) bot.Sleep(10); } }');
  const host = spawn(process.env.SKUA_DOTNET || '/usr/local/share/dotnet/dotnet', [hostPath], {env:{...process.env,SKUA_DATA_DIR:data},stdio:['pipe','pipe','pipe']});
  const messages = [], waiters = [];
  let errors = '';
  const send = value => host.stdin.write(JSON.stringify(value)+'\n');
  host.stderr.on('data', chunk => errors += chunk);
  const waitFor = predicate => new Promise((resolve,reject) => {
    const existing = messages.find(predicate); if (existing) return resolve(existing);
    const timer = setTimeout(() => reject(new Error('Host timeout: '+JSON.stringify(messages)+' '+errors)),15000);
    waiters.push({predicate,resolve:value => {clearTimeout(timer);resolve(value);}});
  });
  readline.createInterface({input:host.stdout}).on('line', line => {
    const message = JSON.parse(line); messages.push(message);
    if (message.type === 'request') {
      assert.equal(message.kind,'flash');
      const name = message.data.function;
      let value = null;
      if (name === 'isNull' || name === 'probeBoolean') value = true;
      if (name === 'probeBoolean') assert.deepEqual(message.data.args,[true]);
      send({type:'reply',id:message.id,value});
    }
    for (let i = waiters.length-1; i >= 0; i--) if (waiters[i].predicate(message)) waiters.splice(i,1)[0].resolve(message);
  });
  try {
    const ready = await waitFor(m => m.type === 'ready');
    assert.equal(ready.scripts,path.join(data,'Scripts'));
    send({type:'event',name:'requestLoadGame',args:[]});
    await waitFor(m => m.type === 'request' && m.data.function === 'loadClient');
    send({type:'command',command:'start'});
    await waitFor(m => m.type === 'log' && m.kind === 'Error' && m.message === 'Wait for AQW to finish loading.');
    send({type:'event',name:'loaded',args:[]});
    await waitFor(m => m.type === 'game-ready');
    send({type:'command',command:'load',path:script});
    await waitFor(m => m.type === 'selected');
    send({type:'command',command:'start'});
    await waitFor(m => m.type === 'log' && m.message === (process.platform === 'darwin' ? 'INCLUDE_OK_MAC' : 'INCLUDE_OK_OTHER'));
    await waitFor(m => m.type === 'log' && m.message === 'BRIDGE_OK');
    send({type:'command',command:'stop'});
    await waitFor(m => m.type === 'status' && m.running === false);
    assert.ok(messages.some(m => m.type === 'request' && m.data.function === 'killLag' && m.data.args[0] === false), 'Stopping restores game rendering');
    assert.deepEqual(messages.filter(m => m.kind === 'Error').map(m => m.message),['Wait for AQW to finish loading.']);
    // A second run creates a fresh load context and resolves helpers from disk cache.
    messages.length = 0;
    send({type:'command',command:'load',path:script});
    await waitFor(m => m.type === 'selected');
    send({type:'command',command:'start'});
    await waitFor(m => m.type === 'log' && m.message === 'BRIDGE_OK');
    send({type:'command',command:'stop'});
    await waitFor(m => m.type === 'status' && m.running === false);
    assert.deepEqual(messages.filter(m => m.kind === 'Error'),[]);

  } finally {
    host.stdin.end(); host.kill(); rmSync(data,{recursive:true,force:true});
  }
});
