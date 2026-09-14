const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const {EventEmitter}=require('node:events');

test('late renderer callbacks and host delivery tolerate destroyed windows and contents',async()=>{
  let created,reads=0,delivered=0;
  const app=new EventEmitter();
  Object.assign(app,{setName(){},setPath(){},getPath(){return '/tmp';},commandLine:{appendSwitch(){}},whenReady:()=>Promise.resolve(),quit(){}});
  class Window extends EventEmitter {
    constructor(){super();created=this;this.dead=false;this.contents=new EventEmitter();
      Object.assign(this.contents,{dead:false,isDestroyed(){return this.dead;},getURL(){reads++;if(this.dead)throw new Error('Object has been destroyed');return 'file:///test/index.html';},executeJavaScript(){delivered++;return Promise.resolve();},session:{setPermissionRequestHandler(){},webRequest:{onCompleted(){},onErrorOccurred(){}}}});
    }
    isDestroyed(){return this.dead;}
    get webContents(){if(this.dead)throw new Error('Object has been destroyed');return this.contents;}
    loadURL(){}
  }
  const electron={app,BrowserWindow:Window,Menu:{setApplicationMenu(){},buildFromTemplate(){return[];}},dialog:{showErrorBox(_title,message){throw new Error(message);}},clipboard:{},shell:{}};
  const context={require:name=>name==='electron'?electron:name==='fs'?{existsSync:()=>false,mkdirSync(){},appendFileSync(){}}:require(name),process:{env:{},resourcesPath:'/test'},__dirname:'/test',console,setImmediate,setTimeout,clearTimeout};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../desktop/main.cjs'),'utf8'),context);
  await new Promise(resolve=>setImmediate(resolve));
  const contents=created.contents;
  contents.emit('console-message',{},0,'__SKUA_UI__{"type":"command","command":"area-quest-open","value":"key"}');
  assert.equal(reads,1,'Live renderer messages still validate their origin');
  vm.runInNewContext('toWindow({type:"a"});toWindow({type:"b"})',context);
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(delivered,1,'Host UI messages flush as one executeJavaScript call');
  contents.dead=true;
  assert.doesNotThrow(()=>contents.emit('console-message',{},0,'__SKUA_UI__{}'));
  assert.doesNotThrow(()=>vm.runInNewContext('toWindow({type:"test"})',context));
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(reads,1,'Destroyed webContents never receives getURL');
  assert.equal(delivered,1,'Destroyed webContents never receives JavaScript');
  created.dead=true;
  assert.doesNotThrow(()=>contents.emit('console-message',{},0,'__SKUA_UI__{}'));
  created.emit('closed');
  assert.doesNotThrow(()=>contents.emit('console-message',{},0,'__SKUA_UI__{}'));
  assert.doesNotThrow(()=>contents.emit('plugin-crashed'));
  assert.doesNotThrow(()=>vm.runInNewContext('toWindow({type:"test"})',context));
  await vm.runInNewContext('request({id:1,kind:"flash",data:{}})',context);
});
