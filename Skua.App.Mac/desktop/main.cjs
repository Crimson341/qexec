const {app, BrowserWindow, Menu, dialog, clipboard, shell, net} = require('electron');
const {spawn, execFileSync} = require('child_process');
const fs = require('fs');
const path = require('path');
const readline = require('readline');
const {pathToFileURL} = require('url');
const updateCheck = require('./update-check.cjs');

const flashPath = process.env.SKUA_FLASH_PLUGIN || '/Applications/Artix Game Launcher.app/Contents/Resources/plugins/PepperFlashPlayer.plugin';
const swfPath = process.env.SKUA_SWF || path.join(__dirname, 'assets', 'skua.swf');
const bundledHost = path.join(process.resourcesPath, 'backend', 'Skua.Mac.Host');
const hostPath = process.env.SKUA_HOST || (fs.existsSync(bundledHost) ? bundledHost : path.resolve(__dirname, '../host/bin/Release/net10.0/Skua.Mac.Host.dll'));
app.setName('Skua Mac');
app.setPath('userData', path.join(app.getPath('appData'), 'Skua Mac'));
fs.mkdirSync(app.getPath('userData'), {recursive:true});
const diagnostic = text => fs.appendFileSync(path.join(app.getPath('userData'), 'startup.log'), new Date().toISOString() + ' ' + text + '\n');
app.commandLine.appendSwitch('ppapi-flash-path', flashPath);
app.commandLine.appendSwitch('ppapi-flash-version', '32.0.0.344');
let window, host, selected, running = false, pageURL, updateUrl = '', pendingUpdateNotice = null;
const requests = new Map();
const events = new Set(['requestLoadGame','loaded','debug','pext','packet','pre-load','game-error']);
function send(value) { if (host && !host.killed && host.stdin.writable) host.stdin.write(JSON.stringify(value) + '\n'); }
function liveContents() {
  if (!window || window.isDestroyed()) return null;
  const contents = window.webContents;
  return contents && !contents.isDestroyed() ? contents : null;
}
const pendingUi = [];
let flushScheduled = false;
function scheduleFlush() {
  if (flushScheduled) return;
  flushScheduled = true;
  const later = typeof setImmediate === 'function' ? setImmediate : fn => setTimeout(fn, 0);
  later(flushToWindow);
}
function flushToWindow() {
  flushScheduled = false;
  if (!pendingUi.length) return;
  const batch = pendingUi.splice(0);
  try {
    const contents = liveContents();
    if (!contents) return;
    const code = batch.length === 1
      ? 'window.receiveHostMessage(' + JSON.stringify(batch[0]) + ')'
      : 'window.receiveHostMessages(' + JSON.stringify(batch) + ')';
    contents.executeJavaScript(code).catch(error => diagnostic('UI delivery: '+error.message));
  } catch (error) { diagnostic('UI delivery: '+error.message); }
}
function toWindow(value) {
  pendingUi.push(value);
  scheduleFlush();
}
function reply(id, value, error) { send({type:'reply', id, value, error}); }
function log(message, kind = 'Error') { toWindow({type:'log', kind, message}); }
async function chooseScript() {
  if (running || !liveContents()) return;
  const result = await dialog.showOpenDialog(window, {title:'Choose a Skua script', defaultPath:path.join(app.getPath('documents'), 'Skua', 'Scripts'), properties:['openFile'], filters:[{name:'C# scripts', extensions:['cs']}]});
  if (!result.canceled && result.filePaths.length === 1) send({type:'command', command:'load', path:result.filePaths[0]});
}
function deliverUpdateNotice() {
  if (!pendingUpdateNotice || !liveContents()) return;
  toWindow({type:'app-update', message:pendingUpdateNotice.message, url:pendingUpdateNotice.url, aheadBy:pendingUpdateNotice.aheadBy});
  pendingUpdateNotice = null;
}
function checkAppUpdate() {
  try {
    if (!net || typeof net.request !== 'function') return Promise.resolve();
    const identity = updateCheck.resolveIdentity({fs, path, dirname:__dirname, env:process.env, execFileSync});
    const headers = updateCheck.githubHeaders(identity.version);
    const getJson = url => updateCheck.requestGithubJson(net, url, headers, 10000);
    return updateCheck.findUpdate({getJson, identity}).then(result => {
      if (!result || !result.available || !updateCheck.isAllowedUpdateUrl(result.url)) return;
      updateUrl = result.url;
      pendingUpdateNotice = {message:result.message, url:result.url, aheadBy:result.aheadBy};
      deliverUpdateNotice();
    }).catch(error => diagnostic('Update check: ' + (error && error.message ? error.message : error)));
  } catch (error) {
    diagnostic('Update check: ' + error.message);
    return Promise.resolve();
  }
}
function command(name, value) {
  if (name === 'app-update-open') {
    if (!updateCheck.isAllowedUpdateUrl(updateUrl)) return;
    const opened = shell.openExternal(updateUrl);
    if (opened && typeof opened.then === 'function') {
      opened.catch(error => log('Could not open the update page: ' + error.message));
    }
    return;
  }
  if (name === 'gear-wiki-open') {
    if (typeof value !== 'string' || !/^http:\/\/aqwwiki\.wikidot\.com\/[a-z0-9-]+$/.test(value)) return;
    const browser=spawn('/usr/bin/open',['-a','Google Chrome',value]); browser.on('error',error=>log('Could not open Chrome: '+error.message)); return;
  }
  if (name === 'quest-news-open') {
    if (typeof value !== 'string' || !/^https:\/\/www\.aq\.com\/gamedesignnotes\/[a-zA-Z0-9-]+\/?$/.test(value)) return;
    const browser = spawn('/usr/bin/open',['-a','Google Chrome',value]);
    browser.on('error',error => log('Could not open Chrome: ' + error.message));
    return;
  }
  if (['become-op','area-acquire','area-item-shop','item-preview','area-quest-open','area-quest-accept','area-deep','area-location','area-scan','area-shop','area-monster','area-quests','area-plan','area-go','area-cancel','gear-inspect','gear-find','gear-go','quest-refresh','quest-go','quest-catalog','catalog-farm','catalog-go','active-quests','active-quest-go','active-quest-open','cancel-active-quest','achievements','achievements-go'].includes(name)) {
    if (value !== undefined && (typeof value !== 'string' || value.length > (name === 'gear-find' || name === 'catalog-farm' ? 512 : 200))) return;
    return send({type:'command',command:name,value});
  }
  if (name === 'choose') return chooseScript();
  if (name === 'start' && (!selected || running)) return;
  if (['start','stop','auto-start','auto-stop'].includes(name)) send({type:'command', command:name});
}
async function request(message) {
  const {id,kind,data} = message;
  try {
    if (kind === 'flash') {
      const contents = liveContents();
      if (!contents) throw new Error('Game window closed.');
      const value = await contents.executeJavaScript('window.invokeFlash(' + JSON.stringify(data) + ')');
      return reply(id,value === undefined ? null : value);
    }
    if (kind === 'options' && !liveContents()) throw new Error('Game window closed.');
    if (kind === 'options') { requests.set(id,kind); toWindow(message); return; }
    if (kind === 'dialog') {
      if (!liveContents()) throw new Error('Game window closed.');
      const {response} = await dialog.showMessageBox(window, {title:data.caption,message:data.message || ' ',buttons:data.buttons,cancelId:-1,noLink:true});
      return reply(id,response);
    }
    if (kind === 'clipboard-read') return reply(id,clipboard.readText());
    if (kind === 'clipboard-write') {clipboard.writeText(data.text);return reply(id,null);}
    throw new Error('Unsupported host request: '+kind);
  } catch(error) {reply(id,null,error.message);}
}
function startHost() {
  const isDll = hostPath.endsWith('.dll');
  const dataDirectory = process.env.SKUA_DATA_DIR || path.join(app.getPath('userData'),'data');
  const scriptsDirectory = process.env.SKUA_SCRIPTS_DIR || (process.env.SKUA_DATA_DIR
    ? path.join(dataDirectory,'Scripts') : path.join(app.getPath('documents'),'Skua','Scripts'));
  host = spawn(isDll ? (process.env.SKUA_DOTNET || '/usr/local/share/dotnet/dotnet') : hostPath, isDll ? [hostPath] : [],
    {stdio:['pipe','pipe','pipe'],cwd:app.getPath('userData'),env:{...process.env,SKUA_DATA_DIR:dataDirectory,SKUA_SCRIPTS_DIR:scriptsDirectory}});
  diagnostic('Engine spawned: '+host.pid);
  host.on('error',error=>log('Cannot start the C# engine: '+error.message));
  readline.createInterface({input:host.stdout}).on('line',line=>{
    try {
      const message = JSON.parse(line);
      if (['ready','game-ready'].includes(message.type)) diagnostic('Engine: '+message.type);
      if (message.type === 'request') return void request(message);
      if (message.type === 'selected') selected = message.path;
      if (message.type === 'status') running = message.running;
      if (message.type === 'beep') return shell.beep();
      toWindow(message);
    } catch(error) {log('Invalid engine message: '+error.message);}
  });
  readline.createInterface({input:host.stderr}).on('line',line=>{diagnostic(line);log(line,'Diagnostic');});
  host.on('exit',code=>{running=false;toWindow({type:'engine-exit',code});requests.clear();});
}
app.whenReady().then(async()=>{
  pageURL = pathToFileURL(path.join(__dirname,'index.html')).href;
  // Pepper requires the page's JS world. There is deliberately NO preload and NO Node integration.
  // Desktop actions stay in this process and accept only the narrow structured messages below.
  window = new BrowserWindow({title:'Skua Mac',width:1280,height:850,minWidth:900,minHeight:620,backgroundColor:'#191b1d',
    webPreferences:{plugins:true,nodeIntegration:false,contextIsolation:false,sandbox:true,enableRemoteModule:false,
      webSecurity:true,backgroundThrottling:false}});
  const contents = window.webContents;
  window.on('closed',()=>{window=null;requests.clear();if(host && !host.killed)host.kill();});
  window.webContents.on('will-navigate',event=>event.preventDefault());
  window.webContents.on('new-window',event=>event.preventDefault());
  window.webContents.session.setPermissionRequestHandler((_contents,permission,callback)=>callback(permission==='plugins'));
  const startupRequests = {urls:['https://game.aq.com/game/api/data/gameversion','https://game.aq.com/crossdomain.xml']};
  window.webContents.session.webRequest.onCompleted(startupRequests,details=>{
    const message = 'Game startup HTTP '+details.statusCode+' ('+new URL(details.url).pathname+').';
    diagnostic(message); log(message,'Diagnostic');
  });
  window.webContents.session.webRequest.onErrorOccurred(startupRequests,details=>{
    const message = 'Game startup request failed: '+details.error+' ('+new URL(details.url).pathname+').';
    diagnostic(message); log(message);
  });
  window.webContents.on('plugin-crashed',()=>log('The Flash renderer crashed. Reopen Skua Mac.'));
  window.webContents.on('render-process-gone',()=>{if(host)host.kill();});
  window.webContents.on('console-message',(_event,level,text)=>{
    if (!window || window.isDestroyed() || contents.isDestroyed()) return;
    if (!text.startsWith('__SKUA_UI__')) {if(level>=2)diagnostic('Renderer: '+text);return;}
    try {
      if (contents.getURL()!==pageURL || text.length>8*1024*1024) return;
      const message = JSON.parse(text.slice('__SKUA_UI__'.length));
      if (message.type==='event' && events.has(message.name) && Array.isArray(message.args)) {
        if(['requestLoadGame','loaded'].includes(message.name))diagnostic('Flash event: '+message.name);
        if(message.name==='game-error')return toWindow({type:'game-error',message:String(message.args[0])});
        if(message.name==='debug')return log(String(message.args[0]),'Flash');
        send(message);
      } else if(message.type==='command') command(message.command,message.value);
      else if(message.type==='reply' && requests.has(message.id)) {requests.delete(message.id);reply(message.id,message.value,message.error);}
    } catch(error) {log('Invalid client message: '+error.message);}
  });
  window.webContents.once('did-finish-load',()=>{
    if (!liveContents()) return;
    startHost();
    const missing=[];
    if(!fs.existsSync(flashPath))missing.push('Mac Flash plugin: '+flashPath);
    if(!fs.existsSync(swfPath))missing.push('Skua game bridge: '+swfPath);
    if(missing.length) toWindow({type:'setup-error',message:'Missing '+missing.join('\n')+'\nSee MACOS.md for setup.'});
    else toWindow({type:'load-game',url:pathToFileURL(swfPath).href});
    deliverUpdateNotice();
  });
  Menu.setApplicationMenu(Menu.buildFromTemplate([
    {label:'Skua Mac',submenu:[{role:'about'},{type:'separator'},{role:'quit'}]},
    {label:'Script',submenu:[{label:'Choose script…',accelerator:'CmdOrCtrl+O',click:chooseScript},
      {label:'Run',accelerator:'CmdOrCtrl+R',click:()=>command('start')},{label:'Stop',accelerator:'CmdOrCtrl+.',click:()=>command('stop')}]},
    {label:'Edit',submenu:[{role:'undo'},{role:'redo'},{type:'separator'},{role:'cut'},{role:'copy'},{role:'paste'},{role:'selectAll'}]},
    {label:'Window',submenu:[{role:'minimize'},{role:'zoom'},{role:'close'}]}
  ]));
  window.loadURL(pageURL);
  void checkAppUpdate();
}).catch(error=>{dialog.showErrorBox('Skua Mac could not start',error.message);app.quit();});
app.on('window-all-closed',()=>app.quit());
app.on('before-quit',()=>{if(host){host.stdin.end();host.kill();}});
