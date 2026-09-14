const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

test('script controls and elapsed time follow host lifecycle, and logs remain bounded', () => {
  const elements = new Map();
  const element = () => ({textContent:'', children:[], disabled:false, dataset:{},style:{},
    classList:{toggle(){}, add(){}, remove(){}}, setAttribute(){}, hasAttribute(){return true;},
    addEventListener(){}, focus(){}, scrollIntoView(){}, close(){}, showModal(){},
    append(...items){this.children.push(...items);},
    replaceChildren(){this.children=[];}, get childElementCount(){return this.children.length;},
    get firstElementChild(){const owner=this; return {remove(){owner.children.shift();}};}});
  const html = fs.readFileSync(path.join(__dirname,'../desktop/index.html'),'utf8');
  for (const match of html.matchAll(/id="([^"]+)"/g)) elements.set(match[1],element());
  let now = 0, timer;
  const context = {document:{documentElement:{style:{setProperty(){}}},
    getElementById:id=>{assert.ok(elements.has(id), `Missing ${id}`);return elements.get(id);},
    createElement:element, createTextNode:text=>text, body:{...element(),classList:{toggle(){},remove(){}}}, querySelectorAll:()=>[]},
    localStorage:{getItem(){return null;},setItem(){}},
    window:{}, console:{log(){}}, Date:class extends Date {static now(){return now;}},
    setInterval:(callback,ms)=>{if(ms===1000)timer=callback;}, setTimeout(){}, clearTimeout(){}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../desktop/renderer.cjs'),'utf8'),context);
  const receive = context.window.receiveHostMessage;
  receive({type:'area-snapshot',map:'river',shops:[{Key:'shop',Name:'Chest Shop',ID:123}],monsters:[{Key:'monster',Name:'Kuro',HP:4060}],quests:[],accepted:[],note:'Discovered'});
  assert.equal(elements.get('area-title').textContent,'What to do in /river');
  assert.equal(elements.get('area-shops').childElementCount,1);
  receive({type:'area-shop',name:'Chest Shop',id:123,note:'Live',items:[{key:'item',id:1,name:'Blade',cost:0,owned:{inventory:0,bank:1},requirements:[{id:2,name:'Token',quantity:5,owned:{inventory:2,bank:null}}]}]});
  assert.equal(elements.get('area-items').childElementCount,1);
  const pictureButton=elements.get('area-items').children[0].children[0].children.find(child=>child.className==='item-picture');
  assert.equal(pictureButton.dataset.itemName,'Blade','Shop items get an image preview');
  assert.equal(vm.runInNewContext("safePicture('https://i.imgur.com.evil.test/x')",context),false);
  context.testPictureButton=pictureButton;
  vm.runInNewContext("paintPicture(testPictureButton,{Images:['https://i.imgur.com/blade.png']})",context);
  assert.equal(pictureButton.children[0].src,'https://i.imgur.com/blade.png');
  assert.equal(pictureButton.children[0].alt,'Blade');
  const farmCommands=[];context.window.skua.command=(...args)=>farmCommands.push(args);
  elements.get('area-quantity').value='1';
  const farmButton=elements.get('area-items').children[0].children[1];
  assert.equal(farmButton.textContent,'Farm & merge');
  farmButton.onclick();
  assert.equal(farmCommands[0][0],'area-acquire','Primary item action starts acquisition instead of stopping at a preview');
  assert.deepEqual(JSON.parse(farmCommands[0][1]),{key:'item',quantity:1});
  receive({type:'area-error',message:'Reset request'});
  receive({type:'area-shop',name:'AC Shop',id:1,note:'',items:[{key:'premium',name:'Premium item',cost:100,coins:true}]});
  const shopButton=elements.get('area-items').children[0].children[1];
  assert.equal(shopButton.textContent,'Open shop in game');shopButton.onclick();
  assert.deepEqual(farmCommands.pop(),['area-item-shop','premium'],'Premium items open the shop without buying');
  receive({type:'area-error',message:'Reset request'});
  receive({type:'area-quests',quests:[{key:'q374',name:'Ring Bearer',id:374,accepted:false},{key:'unknown',name:'Unknown',id:0,accepted:false}],note:'Open or accept'});
  const questRows=elements.get('area-items').children;
  assert.equal(questRows.length,2,'No misleading empty items message for quest pages');
  assert.equal(questRows[0].children[1].textContent,'Open in game');
  assert.equal(questRows[0].children[2].textContent,'Accept quest');
  assert.equal(questRows[1].children.length,1,'Unknown IDs do not produce executable buttons');
  const questCommands=[];context.window.skua.command=(...args)=>questCommands.push(args);
  questRows[0].children[1].onclick();
  assert.deepEqual(questCommands.pop(),['area-quest-open','q374']);
  receive({type:'area-error',message:'Reset pending request'});
  questRows[0].children[2].onclick();
  assert.deepEqual(questCommands.pop(),['area-quest-accept','q374']);
  receive({type:'area-error',message:'Reset pending request'});
  receive({type:'area-plan',key:'plan',root:{Name:'Blade',Kind:'shop',Map:'river',Shop:123,Cost:0,Quantity:1,Children:[]},code:'generated',note:'Ready'});
  assert.equal(elements.get('area-go').disabled,false);
  receive({type:'area-error',message:'Map changed'});
  assert.equal(elements.get('area-go').disabled,true);
  assert.equal(elements.get('area-status').textContent,'Map changed');

  assert.equal(elements.get('vibe-questing').hidden,true);
  receive({type:'active-quests',quests:[{id:42,name:'Quest',ready:false,rewards:[]}]});
  assert.equal(elements.get('active-quest-open').hidden,false);
  assert.equal(elements.get('active-quest-list').childElementCount,1);
  assert.equal(elements.get('ledger-list').childElementCount,1);
  const ledgerCommands=[];context.window.skua.command=(...args)=>ledgerCommands.push(args);
  const ledgerRow=elements.get('ledger-list').children[0];
  assert.equal(ledgerRow.children[1].textContent,'Open');
  ledgerRow.children[0].onclick();
  ledgerRow.children[0].onclick();
  assert.equal(ledgerCommands.length,1,'Repeated clicks do not start duplicate quest generation');
  assert.equal(ledgerCommands[0][0],'active-quest-go');
  assert.deepEqual(JSON.parse(ledgerCommands[0][1]),{id:42,reward:-1});
  receive({type:'active-quest-progress',message:'Finding objectives'});
  assert.equal(elements.get('ledger-status').textContent,'Finding objectives');
  receive({type:'active-quest-starting',path:'/tmp/q.cs'});
  assert.equal(elements.get('ledger-status').textContent,'Quest script started.');
  receive({type:'active-quest-progress',message:'Quest step: hunt Quest Fang'});
  assert.equal(elements.get('ledger-status').textContent,'Quest step: hunt Quest Fang','Step logs keep updating the ledger after start');
  receive({type:'active-quest-error',message:'Quest locked'});
  assert.equal(elements.get('ledger-status').textContent,'Quest locked','Ledger failures stay inline without a dialog');
  receive({type:'active-quests',quests:[{id:50,name:'Plant hunt',ready:false,rewards:[],objectives:[{name:'Plant Found',have:1,need:3}],dailyDone:false,member:false,locked:false,blocked:false}]});
  const plantRow=elements.get('ledger-list').children[0].children[0];
  assert.match(plantRow.children[1].textContent,/In progress/);
  assert.match(plantRow.children[2].textContent,/Plant Found 1\/3/);
  receive({type:'active-quests',quests:[{id:51,name:'Member daily',ready:false,rewards:[],objectives:[],dailyDone:true,member:true,locked:true,blocked:true}]});
  const blockedRow=elements.get('ledger-list').children[0].children[0];
  assert.match(blockedRow.children[1].textContent,/Daily done/);
  assert.match(blockedRow.children[1].textContent,/Member/);
  assert.match(blockedRow.children[1].textContent,/Locked/);
  assert.equal(blockedRow.disabled,true);
  receive({type:'active-quests',quests:[{id:43,name:'Choose reward',ready:false,rewards:[{id:1,name:'Sword'},{id:2,name:'Cape'}]}]});
  const rewardGroup=elements.get('ledger-list').children[0];
  assert.equal(rewardGroup.children[1].textContent,'Open');
  rewardGroup.children[1].onclick();
  assert.deepEqual(ledgerCommands.pop(),['active-quest-open','43'],'Open loads the quest in the game panel without Auto-do');
  receive({type:'active-quest-opened',opened:true,message:'Choose reward opened in game.'});
  assert.equal(elements.get('ledger-status').textContent,'Choose reward opened in game.');
  rewardGroup.children[2].value='2';rewardGroup.children[0].onclick();
  assert.deepEqual(JSON.parse(ledgerCommands.pop()[1]),{id:43,reward:2},'Inline reward selection starts the chosen quest');
  receive({type:'active-quest-error',message:'Reset'});
  receive({type:'active-quests',quests:[]});
  assert.equal(elements.get('active-quest-open').hidden,true);
  assert.equal(elements.get('active-quest-list').childElementCount,0);
  assert.equal(elements.get('ledger-list').childElementCount,0);
  receive({type:'quest-plan',level:100,bankLoaded:true,equipped:['Void Highlord'],eventAvailable:false,eventDetail:'No bot',goals:[{Id:'vhl',Item:'Void Highlord',Detail:'Owned class',Action:'Owned · Bank',CanRun:false}]});
  assert.equal(elements.get('quest-goals').children[0].children[1].disabled,true);
  assert.match(elements.get('quest-equipped').textContent,/Void Highlord/);
  assert.equal(elements.get('quest-event-go').disabled,true);
  context.window.receiveHostMessages([{type:'quest-catalog',total:19065,matches:1,page:0,bankLoaded:true,items:[{id:100,name:'Sword',category:'Sword',ownership:'Bank',detail:'Reward',description:'',availability:'Unverified',canFind:false}]}]);
  assert.equal(elements.get('catalog-items').children[0].children[1].disabled,true);
  assert.equal(elements.get('catalog-next').disabled,true);
  const catalogCommands=[];context.window.skua.command=(...args)=>catalogCommands.push(args);
  receive({type:'quest-catalog',total:2,matches:1,page:0,bankLoaded:true,items:[{id:200,name:'Ore',category:'Item',ownership:'Missing',detail:'',description:'',availability:'Unverified',canFind:true}]});
  const find=elements.get('catalog-items').children[0].children[1];
  assert.equal(find.textContent,'Find farming plan');
  find.onclick();
  assert.equal(catalogCommands[0][0],'catalog-farm','Catalog farming stays in quests instead of opening Inspect gear');
  assert.deepEqual(JSON.parse(catalogCommands[0][1]),{name:'Ore',id:200});
  receive({type:'catalog-plan',key:'cat',root:{Name:'Ore',Kind:'drop',Map:'mine',Monster:'Slime',Quantity:1,Children:[]},code:'farm',note:'Ready'});
  assert.equal(elements.get('catalog-plan-view').hidden,false);
  assert.equal(elements.get('catalog-go').disabled,false);
  elements.get('catalog-go').onclick();
  assert.equal(catalogCommands.at(-1)[0],'catalog-go');
  receive({type:'catalog-starting',path:'/tmp/farm.cs'});
  receive({type:'quest-catalog',total:2,matches:1,page:0,bankLoaded:true,items:[{id:100,name:'Sword',category:'Sword',ownership:'Missing',detail:'',description:'',availability:'Unverified',canFind:true,acceptedQuestId:42}]});
  const autodo=elements.get('catalog-items').children[0].children[1];
  assert.equal(autodo.textContent,'Auto-do this quest');
  autodo.onclick();
  assert.equal(catalogCommands.at(-1)[0],'catalog-farm');
  receive({type:'catalog-autodo',quest:{id:42,name:'Quest',ready:false,rewards:[]},reward:-1});
  assert.equal(catalogCommands.at(-1)[0],'active-quest-go');
  assert.deepEqual(JSON.parse(catalogCommands.at(-1)[1]),{id:42,reward:-1});
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
  receive({type:'app-update',message:'Update available: main is 2 commits ahead of this build (v0.2.0).',url:'https://github.com/Crimson341/qexec/releases/tag/v0.3.0',aheadBy:2});
  assert.equal(elements.get('app-update').hidden,false);
  assert.match(elements.get('app-update-message').textContent,/2 commits ahead/);
  const updateCommands=[];context.window.skua.command=(...args)=>updateCommands.push(args);
  elements.get('app-update-button').onclick();
  assert.deepEqual(updateCommands.pop(),['app-update-open']);
  elements.get('app-update-dismiss').onclick();
  assert.equal(elements.get('app-update').hidden,true);
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

test('theme accent applies immediately and is restored on reload', () => {
  const html = fs.readFileSync(path.join(__dirname,'../desktop/index.html'),'utf8');
  const store = {};
  const css = {};
  const boot = () => {
    const elements = new Map();
    const element = () => ({textContent:'', children:[], disabled:false, dataset:{},style:{}, value:'',
      classList:{toggle(){}}, setAttribute(){}, hasAttribute(){return false;},
      addEventListener(){}, append(...items){this.children.push(...items);},
      replaceChildren(){this.children=[];}, showModal(){this.open=true;}, close(){this.open=false;}});
    for (const match of html.matchAll(/id="([^"]+)"/g)) elements.set(match[1],element());
    const context = {document:{documentElement:{style:{setProperty(name,value){css[name]=value;},getPropertyValue(name){return css[name]||'';}}},
      getElementById:id=>{assert.ok(elements.has(id), `Missing ${id}`);return elements.get(id);},
      createElement:element, createTextNode:text=>text, body:{...element(),classList:{toggle(){},remove(){}}}, querySelectorAll:()=>[]},
      localStorage:{getItem(key){return store[key] ?? null;}, setItem(key,value){store[key]=value;}},
      window:{}, console:{log(){}}, Date, setInterval(){}, setTimeout(){}, clearTimeout(){}};
    vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../desktop/renderer.cjs'),'utf8'),context);
    return elements;
  };
  const first = boot();
  assert.equal(css['--sea'],'#b09add','Default accent is the current violet ledger color');
  first.get('theme-open').onclick();
  assert.equal(first.get('theme-dialog').open,true);
  first.get('theme-sea').onclick();
  assert.equal(css['--sea'],'#6ba0cc');
  assert.equal(first.get('theme-accent').value,'#6ba0cc');
  assert.equal(first.get('theme-accent-hex').textContent,'#6ba0cc');
  assert.equal(JSON.parse(store['qexec.theme']).accent,'#6ba0cc');
  first.get('theme-accent').value='#d88aa8';
  first.get('theme-accent').oninput({target:first.get('theme-accent')});
  assert.equal(css['--sea'],'#d88aa8','Custom color input applies immediately');
  for (const key of Object.keys(css)) delete css[key];
  const second = boot();
  assert.equal(css['--sea'],'#d88aa8','Stored accent survives a renderer reload');
  second.get('theme-reset').onclick();
  assert.equal(css['--sea'],'#b09add');
  assert.equal(JSON.parse(store['qexec.theme']).accent,'#b09add');
});

test('achievements panel shows images and earned state from host checks', () => {
  const html = fs.readFileSync(path.join(__dirname,'../desktop/index.html'),'utf8');
  const elements = new Map();
  const element = () => ({textContent:'', children:[], disabled:false, dataset:{},style:{}, hidden:true, className:'',
    classList:{toggle(){}, remove(){}}, setAttribute(){}, hasAttribute(){return false;},
    addEventListener(){}, append(...items){this.children.push(...items);},
    replaceChildren(){this.children=[];}, focus(){}, scrollIntoView(){}});
  for (const match of html.matchAll(/id="([^"]+)"/g)) elements.set(match[1],element());
  const commands=[];
  const context = {document:{documentElement:{style:{setProperty(){}}},
    getElementById:id=>{assert.ok(elements.has(id), `Missing ${id}`);return elements.get(id);},
    createElement:element, createTextNode:text=>text, body:{...element(),classList:{toggle(){},remove(){},add(){}}}, querySelectorAll:()=>[]},
    localStorage:{getItem(){return null;},setItem(){}},
    window:{}, console:{log(){}}, Date, setInterval(){}, setTimeout(){}, clearTimeout(){}};
  vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../desktop/renderer.cjs'),'utf8'),context);
  context.window.skua.command=(...args)=>commands.push(args);
  elements.get('nav-achievements').onclick();
  assert.equal(elements.get('achievements-view').hidden,false);
  assert.deepEqual(commands.pop(),['achievements']);
  context.window.receiveHostMessage({type:'achievements',character:'Scott',earned:1,total:2,note:'Inventory, bank, and story progress checked.',newlyEarned:['vhl'],
    items:[
      {Id:'vhl',Title:'Void Highlord',Detail:'Nation grind',Image:'vhl.png',Earned:true,Reason:'Inventory',Location:'Inventory',Script:'Nation/VHL/0VoidHighlord.cs',CanRun:false},
      {Id:'blod',Title:'Blinding Light of Destiny',Detail:'Good-path axe',Image:'blod.png',Earned:false,Reason:'Not earned yet',Location:'Missing',Script:'Good/BLoD/0TheBlindingLightofDestiny.cs',CanRun:true}
    ]});
  const cards=elements.get('achievements-grid').children;
  assert.equal(cards.length,2);
  assert.equal(cards[0].className,'achievement-card');
  assert.equal(cards[0].dataset.achievementId,'vhl');
  assert.equal(cards[0].children[0].src,'brand/achievements/vhl.png');
  assert.equal(cards[0].children[0].alt,'Void Highlord');
  assert.match(cards[0].children[1].textContent,/Earned · Inventory/);
  assert.equal(cards[0].children.length,4);
  assert.equal(cards[1].className,'achievement-card locked');
  assert.equal(cards[1].children[0].src,'brand/achievements/blod.png');
  assert.equal(cards[1].children[4].textContent,'Go');
  assert.match(elements.get('achievements-status').textContent,/Scott · 1\/2 earned/);
  cards[1].children[4].onclick();
  assert.deepEqual(commands.pop(),['achievements-go','blod']);
  context.window.receiveHostMessage({type:'achievements-started',running:true,id:'blod'});
  assert.match(elements.get('achievements-status').textContent,/Mapped bot started/);
  assert.equal(vm.runInNewContext("achievementImage('vhl.png')",context),'brand/achievements/vhl.png');
  assert.equal(vm.runInNewContext("achievementImage('https://evil.test/x.png')",context),'');
  const art=path.join(__dirname,'../desktop/brand/achievements');
  const badges=['vhl','order','paladin','revenant','dragon','nsod','awe','blade-awe','chaos-avenger','archmage','lightcaster','scarlet','blood-sorceress','lightmage','dragon-shinobi','dragonslayer','dsg','frost-spirit','kings-echo','lich','martial-artist','necromancer','proto','sentinel','storms','vdk','arcana','arachnomancer','bard','chaos-slayer','deathknight','dracomancer','inversionist','evolved-shaman','glacial','horc','chunin','lycan','master-ranger','battle-mage','shaman','stonecrusher','thief-hours','troll-spellsmith','good-paladin','silver-paladin','cryomancer','pyromancer','shadowscythe','ynr','swordmaster','ildc','rustbucket','mecha','blaze-binder','blod','slod','armor-awe','helm-awe','awescended','sdka','soh'];
  for (const name of badges) {
    assert.ok(fs.existsSync(path.join(art,name+'.png')), 'Missing achievement image '+name);
  }
});
