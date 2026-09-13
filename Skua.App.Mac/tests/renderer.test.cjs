const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('script controls and elapsed time follow host lifecycle, and logs remain bounded', () => {
  const elements = new Map();
  const element = () => ({textContent:'', children:[], disabled:false, dataset:{},
    classList:{toggle(){}}, setAttribute(){}, hasAttribute(){return true;},
    addEventListener(){}, append(...items){this.children.push(...items);},
    replaceChildren(){this.children=[];}, get childElementCount(){return this.children.length;},
    get firstElementChild(){const owner=this; return {remove(){owner.children.shift();}};}});
  const html = fs.readFileSync(path.join(__dirname,'../desktop/index.html'),'utf8');
  for (const match of html.matchAll(/id="([^"]+)"/g)) elements.set(match[1],element());
  let now = 0, timer;
  const context = {document:{getElementById:id=>{assert.ok(elements.has(id), `Missing ${id}`);return elements.get(id);},
    createElement:element, createTextNode:text=>text, body:element(), querySelectorAll:()=>[]},
    window:{}, console:{log(){}}, Date:class extends Date {static now(){return now;}},
    setInterval:callback=>{timer=callback;}, setTimeout(){}, clearTimeout(){}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../desktop/renderer.cjs'),'utf8'),context);
  const receive = context.window.receiveHostMessage;
  assert.equal(elements.get('vibe-questing').hidden,true);
  receive({type:'active-quests',quests:[{id:42,name:'Quest',ready:false,rewards:[]}]});
  assert.equal(elements.get('active-quest-open').hidden,false);
  assert.equal(elements.get('active-quest-list').childElementCount,1);
  assert.equal(elements.get('ledger-list').childElementCount,1);
  receive({type:'active-quests',quests:[]});
  assert.equal(elements.get('active-quest-open').hidden,true);
  assert.equal(elements.get('active-quest-list').childElementCount,0);
  assert.equal(elements.get('ledger-list').childElementCount,0);
  receive({type:'quest-plan',level:100,bankLoaded:true,equipped:['Void Highlord'],eventAvailable:false,eventDetail:'No bot',goals:[{Id:'vhl',Item:'Void Highlord',Detail:'Owned class',Action:'Owned · Bank',CanRun:false}]});
  assert.equal(elements.get('quest-goals').children[0].children[1].disabled,true);
  assert.match(elements.get('quest-equipped').textContent,/Void Highlord/);
  assert.equal(elements.get('quest-event-go').disabled,true);
  receive({type:'quest-catalog',total:19065,matches:1,page:0,bankLoaded:true,items:[{id:100,name:'Sword',category:'Sword',ownership:'Bank',detail:'Reward',description:'',availability:'Unverified',canFind:false}]});
  assert.equal(elements.get('catalog-items').children[0].children[1].disabled,true);
  assert.equal(elements.get('catalog-next').disabled,true);
  receive({type:'gear-sources',sources:[{Name:'Open shop',Description:'Shop route',File:'Evidence',Code:'script',Action:'Go — open shop',Id:'route'}],message:'Generated route'});
  assert.equal(elements.get('gear-sources').children[0].children[3].textContent,'Go — open shop');
  assert.equal(elements.get('gear-status').textContent,'Generated route');
  receive({type:'quest-news',posts:[{Date:'2026-09-11',Title:'Boss battles',Url:'https://www.aq.com/gamedesignnotes/test'}],message:'Fresh announcements'});
  assert.equal(elements.get('quest-news').childElementCount,1);
  receive({type:'quest-error',message:'Log in first'});
  assert.equal(elements.get('quest-status').textContent,'Log in first');
  assert.equal(elements.get('quest-refresh').disabled,false);
  receive({type:'gear-player',data:{player:'alof',ownershipNote:'Bank timed out',items:[{slot:'ar',name:'Class name',id:74195,ownership:'Unknown'}]}});
  assert.equal(elements.get('gear-items').children[0].children[1].disabled,false);
  assert.match(elements.get('gear-status').textContent,/Bank timed out/);
  assert.equal(elements.get('run').disabled,true);
  receive({type:'ready',architecture:'arm64'});
  receive({type:'selected',path:'/Scripts/Test.cs'});
  assert.equal(elements.get('run').disabled,true);
  receive({type:'game-ready'});
  assert.equal(elements.get('run').disabled,false);
  receive({type:'auto-status',enabled:true,className:'Mage',mode:'Farm',health:100,maximum:100,target:'Slime',encounter:'Mob',supported:true});
  assert.equal(elements.get('run').disabled,true);
  assert.equal(elements.get('auto-toggle').textContent,'Disable');
  receive({type:'auto-status',enabled:false});
  assert.equal(elements.get('run').disabled,false);
  receive({type:'status',running:true});
  assert.equal(elements.get('choose').disabled,true);
  assert.equal(elements.get('stop').disabled,false);
  assert.equal(elements.get('vibe-questing').hidden,false);
  now=65000; timer(); assert.equal(elements.get('elapsed').textContent,'00:01:05');
  context.window.invokeFlash({function:'killLag',args:[true]});
  let commands=JSON.parse(context.window.skuaNextFlashCommands());
  assert.ok(commands.some(c=>c.function==='killLag' && c.args[0]===true),'Running script may enable anti-lag');
  context.window.invokeFlash({function:'killLag',args:[true]});
  elements.get('stop').onclick();
  commands=JSON.parse(context.window.skuaNextFlashCommands());
  assert.ok(commands.length>=2,'Stop queues an explicit restore');
  assert.ok(commands.filter(c=>c.function==='killLag').every(c=>c.args[0]===false),'Queued anti-lag must not re-enable after Stop');

  receive({type:'status',running:false});
  assert.equal(elements.get('vibe-questing').hidden,true);
  context.window.invokeFlash({function:'killLag',args:[true]});
  assert.ok(JSON.parse(context.window.skuaNextFlashCommands()).every(c=>c.function!=='killLag'||c.args[0]===false),'Late timer calls stay disabled after completion');
  now=90000; timer(); assert.equal(elements.get('elapsed').textContent,'00:01:05');
  receive({type:'status',running:true});
  assert.equal(elements.get('elapsed').textContent,'00:00:00');
  receive({type:'game-error',message:'Disconnected'});
  assert.equal(elements.get('vibe-questing').hidden,true);
  receive({type:'game-ready'});
  assert.equal(elements.get('vibe-questing').hidden,false);
  receive({type:'engine-exit',code:1});
  assert.equal(elements.get('vibe-questing').hidden,true);
  assert.equal(elements.get('run').disabled,true);
  assert.equal(elements.get('stop').disabled,true);
  assert.equal(elements.get('script-state').textContent,'Idle');
  for(let i=0;i<410;i++)receive({type:'log',message:'Activity '+i});
  assert.equal(elements.get('logs').childElementCount,400);
  elements.get('clear').onclick();
  assert.equal(elements.get('logs').childElementCount,0);
  assert.equal(elements.get('log-count').textContent,'0');
});
