/* Renderer has no filesystem, process, or Node access. */
const post = message => console.log('__SKUA_UI__' + JSON.stringify(message));
window.skua = {
  command: (command,value) => post({type:'command',command,value}),
  event: (name,args) => post({type:'event',name,args}),
  reply: (id,value,error) => post({type:'reply',id,value,error}),
  onMessage: callback => {window.receiveHostMessage = callback;}
};
const byId = id => document.getElementById(id);
const itemPictures=new Map(),picturePending=new Set();
function requestPicture(name){if(!picturePending.has(name)){picturePending.add(name);window.skua.command('item-preview',name);}}
const pictureObserver=typeof IntersectionObserver==='function'?new IntersectionObserver(entries=>{
  for(const entry of entries)if(entry.isIntersecting){pictureObserver.unobserve(entry.target);requestPicture(entry.target.dataset.itemName);}
},{rootMargin:'120px'}):null;
function safePicture(url){return typeof url==='string' && /^https:\/\/(?:i\.imgur\.com|aqwwiki\.wikidot\.com|aqwwiki\.wdfiles\.com)\//.test(url);}
function paintPicture(button,picture){
  const images=(picture.Images||[]).filter(safePicture);button.replaceChildren();
  if(images.length){const img=document.createElement('img');img.alt=button.dataset.itemName;img.loading='lazy';img.referrerPolicy='no-referrer';img.src=images[0];img.onerror=()=>{button.textContent='Preview unavailable';};button.append(img);button.title='Enlarge '+button.dataset.itemName;}
  else {button.textContent='Preview unavailable';button.title=picture.Note||'No image available';}
}
function itemPicture(parent,name){
  if(!name)return;
  const button=document.createElement('button');button.className='item-picture';button.dataset.itemName=name;button.textContent='Preview';button.setAttribute('aria-label','Preview '+name);
  button.onclick=()=>{
    const picture=itemPictures.get(name),images=(picture?.Images||[]).filter(safePicture);
    if(!images.length){button.textContent='Loading preview…';requestPicture(name);return;}
    byId('item-image-title').textContent=name;byId('item-image-gallery').replaceChildren();
    for(const url of images){const img=document.createElement('img');img.src=url;img.alt=name;img.referrerPolicy='no-referrer';byId('item-image-gallery').append(img);}
    byId('item-image-dialog').showModal();
  };
  parent.append(button);
  if(itemPictures.has(name))paintPicture(button,itemPictures.get(name));else if(pictureObserver)pictureObserver.observe(button);
}
function rememberPicture(name,picture){
  if(itemPictures.has(name))itemPictures.delete(name);
  itemPictures.set(name,picture);
  while(itemPictures.size>500)itemPictures.delete(itemPictures.keys().next().value);
}
byId('item-image-close').onclick=()=>byId('item-image-dialog').close();
byId('app-update-button').onclick=()=>window.skua.command('app-update-open');
byId('app-update-dismiss').onclick=()=>{byId('app-update').hidden=true;};
byId('become-op').onclick=()=>{window.skua.command('become-op');byId('nav-activity').onclick();};
let autoEnabled = false;
let stopping = false;
let startedAt = null, elapsedMs = 0;
let game, engineReady = false, gameReady = false, selected = false, running = false, optionRequest;
const log = (message, kind = '') => {
  const entry = document.createElement('div'); entry.className = 'entry' + (kind === 'Error' ? ' error' : '');
  const time = document.createElement('time'); time.textContent = new Date().toLocaleTimeString();
  entry.append(time, document.createTextNode(message)); byId('logs').append(entry);
  while (byId('logs').childElementCount > 400) byId('logs').firstElementChild.remove();
  byId('logs').scrollTop = byId('logs').scrollHeight;
  byId('log-count').textContent = byId('logs').childElementCount;
};
const controls = () => {
  byId('become-op').disabled=!engineReady||!gameReady||running||autoEnabled;
  byId('choose').disabled = !engineReady || running;
  byId('run').disabled = !engineReady || !gameReady || !selected || running || autoEnabled;
  byId('stop').disabled = !running;
  byId('auto-toggle').disabled = !autoEnabled && (!engineReady || !gameReady || running);
  document.body.classList.toggle('game-ready', gameReady);
  document.body.classList.toggle('script-running', running);
  byId('vibe-questing').hidden = !(running && engineReady && gameReady);
  byId('script-state').textContent = running ? 'Running' : 'Idle';
  byId('footer-script').textContent = running ? 'Script running' : 'Script idle';
  byId('bridge-state').textContent = gameReady ? 'Connected' : 'Not connected';
  byId('engine-state').textContent = engineReady ? 'Connected' : 'Not connected';
  byId('app-status').textContent = engineReady && gameReady ? 'Ready' : 'Waiting for connection';
  if (running && startedAt === null) { startedAt = Date.now(); elapsedMs = 0; }
  if (!running && startedAt !== null) { elapsedMs = Date.now() - startedAt; startedAt = null; }
  updateElapsed();
};
function updateElapsed() {
  const seconds = Math.floor((startedAt === null ? elapsedMs : Date.now() - startedAt) / 1000);
  byId('elapsed').textContent = [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60].map(value => String(value).padStart(2, '0')).join(':');
}
function hideWorkspaceViews() {
  byId('area-view').hidden=true;byId('quest-view').hidden=true;byId('achievements-view').hidden=true;
  document.body.classList.remove('area-open','quest-open','achievements-open');
}
setInterval(() => {updateElapsed(); pollActiveQuests();}, 1000);
for (const [id, target] of [['nav-game','game'], ['nav-script','choose'], ['nav-activity','logs']]) {
  byId(id).onclick = () => {
    hideWorkspaceViews();
    for (const item of document.querySelectorAll('.nav-item')) {
      item.classList.toggle('active', item.id === id);
      if (item.id === id) item.setAttribute('aria-current', 'location');
      else item.removeAttribute('aria-current');
    }
    const element = target === 'choose' && byId('choose').disabled ? byId('selected') : byId(target);
    if (!element.hasAttribute('tabindex')) element.setAttribute('tabindex', '-1');
    element.focus();
    element.scrollIntoView({block:'nearest'});
  };
}
for (const [id, name] of [['choose','choose'],['run','start'],['stop','stop']]) byId(id).onclick = () => {
  if (name === 'stop') { stopping = true; restoreGameRendering(); }
  window.skua.command(name);
};
byId('auto-toggle').onclick = () => window.skua.command(autoEnabled ? 'auto-stop' : 'auto-start');
byId('clear').onclick = () => { byId('logs').replaceChildren(); byId('log-count').textContent = '0'; };
for (const name of ['requestLoadGame','loaded','debug','pext','packet','pre-load','game-error']) window[name] = (...args) => {
  window.skua.event(name, args.length === 1 && Array.isArray(args[0]) ? args[0] : args);
};
// ActiveX accepts arbitrary event names; browser ExternalInterface requires a JS identifier.
window.skuaFlashEvent = (name, args) => window.skua.event(name, args);
let flashSequence = 0;
const flashCommands = [], flashReplies = new Map();
window.invokeFlash = message => new Promise((resolve,reject) => {
  const id = ++flashSequence;
  const timer = setTimeout(() => {
    flashReplies.delete(id);
    const index = flashCommands.findIndex(command => command.id === id);
    if (index >= 0) flashCommands.splice(index,1);
    reject(new Error('Flash command timed out: '+message.function));
  },12000);
  flashReplies.set(id,{resolve,reject,timer});
  flashCommands.push({id,function:message.function,args:message.args});
});
// Recheck at dispatch: a queued timer/script call may outlive the run that created it.
window.skuaNextFlashCommands = () => JSON.stringify(flashCommands.splice(0).map(command =>
  command.function === 'killLag' && (!running || stopping || !engineReady)
    ? {...command,args:[false]} : command));
function restoreGameRendering() {
  if (!gameReady) return;
  window.invokeFlash({function:'killLag',args:[false]}).catch(error => log('Could not restore game rendering: '+error.message,'Error'));
}
window.skuaFlashReply = (id,value,error) => {
  const pending = flashReplies.get(id);
  if (!pending) return;
  clearTimeout(pending.timer); flashReplies.delete(id);
  if (error) pending.reject(new Error(error)); else pending.resolve(value);
};
function showOptions(message) {
  optionRequest = message.id; byId('fields').replaceChildren(); byId('option-error').textContent = '';
  for (const field of message.data.fields) {
    const label = document.createElement('label'); label.className = 'field';
    const title = document.createElement('span'); title.textContent = field.name;
    const choices = field.choices || (field.boolean ? ['True','False'] : null);
    const input = document.createElement(choices ? 'select' : 'input'); input.dataset.id = field.id;
    if (choices) for (const choice of choices) { const option = document.createElement('option'); option.value = choice; option.textContent = choice; input.append(option); }
    input.value = field.value || ''; const help = document.createElement('small'); help.textContent = field.description;
    label.append(title, input, help); byId('fields').append(label);
  }
  byId('options').showModal();
}
function cancelOptions() { window.skua.reply(optionRequest, null); byId('options').close(); }
byId('cancel-options').onclick = cancelOptions;
byId('options').addEventListener('cancel', event => { event.preventDefault(); cancelOptions(); });
byId('option-form').onsubmit = event => {
  event.preventDefault();
  window.skua.reply(optionRequest, [...byId('fields').querySelectorAll('input,select')].map(input => ({id:Number(input.dataset.id),value:input.value})));
  byId('options').close();
};
let activeQuestPending = false, activeQuestPolled = -3000, activeQuestSignature = '';
function pollActiveQuests(force = false) {
  if (!engineReady || !gameReady || activeQuestPending || (!force && Date.now()-activeQuestPolled<3000)) return;
  activeQuestPending = true; activeQuestPolled = Date.now(); window.skua.command('active-quests');
}
byId('active-quest-open').onclick = () => {byId('active-quest-dialog').showModal(); pollActiveQuests(true);};
byId('active-quest-close').onclick = () => byId('active-quest-dialog').close();
byId('active-quest-refresh').onclick = () => pollActiveQuests(true);
let catalogPage = 0;
function loadCatalog(page = 0) {
  catalogPage = page;
  byId('catalog-status').textContent = 'Indexing quest data and checking ownership… First scan may take a moment.';
  byId('catalog-search-button').disabled = true; byId('catalog-prev').disabled = true; byId('catalog-next').disabled = true;
  window.skua.command('quest-catalog', JSON.stringify({search:byId('catalog-search').value || '',filter:byId('catalog-filter').value || 'all',page}));
}
byId('catalog-form').onsubmit = event => { event.preventDefault(); loadCatalog(); };
byId('catalog-prev').onclick = () => loadCatalog(catalogPage-1);
byId('catalog-next').onclick = () => loadCatalog(catalogPage+1);
let catalogBusy=false,catalogPlanKey='';
function catalogFinished(message) {
  catalogBusy=false;byId('catalog-cancel').hidden=true;byId('catalog-go').disabled=!catalogPlanKey;
  if(message)byId('catalog-status').textContent=message;
}
byId('catalog-go').onclick=()=>{if(!catalogPlanKey||catalogBusy)return;catalogBusy=true;byId('catalog-go').disabled=true;byId('catalog-cancel').hidden=false;byId('catalog-status').textContent='Starting the generated item farm…';window.skua.command('catalog-go',catalogPlanKey);};
byId('catalog-cancel').onclick=()=>window.skua.command('area-cancel');
function refreshQuests() {
  byId('quest-refresh').disabled = true;
  byId('quest-status').textContent = 'Checking inventory, equipped gear and bank…';
  byId('quest-goals').replaceChildren();
  byId('quest-equipped').textContent = '';
  byId('quest-event-detail').textContent = '';
  byId('quest-event-go').disabled = true;
  byId('quest-news').replaceChildren();
  byId('quest-news-status').textContent = 'Fetching official announcements…';
  window.skua.command('quest-refresh');
}
byId('nav-quest').onclick = () => {
  hideWorkspaceViews();
  byId('quest-view').hidden = false; document.body.classList.add('quest-open');
  for (const item of document.querySelectorAll('.nav-item')) {
    item.classList.toggle('active', item.id === 'nav-quest');
    if (item.id === 'nav-quest') item.setAttribute('aria-current','page'); else item.removeAttribute('aria-current');
  }
  refreshQuests();
};
byId('quest-refresh').onclick = refreshQuests;
function launchQuest(id) {
  byId('quest-status').textContent = 'Rechecking ownership and starting bot…';
  window.skua.command('quest-go',id);
}
byId('quest-event-go').onclick = () => launchQuest('event-pirate');
byId('gear-open').onclick = () => { byId('gear-dialog').showModal(); };
byId('gear-close').onclick = () => byId('gear-dialog').close();
byId('gear-refresh').onclick = () => {
  byId('gear-items').replaceChildren(); byId('gear-sources').replaceChildren();
  byId('gear-refresh').disabled = true; byId('gear-status').textContent = 'Reading gear and resolving item names…'; window.skua.command('gear-inspect');
};
function questBadges(quest) {
  const badges=[];
  if(quest.ready) badges.push('Ready to turn in'); else badges.push('In progress');
  if(quest.farmable) badges.push('Farm / repeatable');
  if(quest.dailyDone) badges.push('Daily done');
  if(quest.member) badges.push('Member');
  if(quest.locked) badges.push('Locked');
  return '#'+quest.id+' · '+badges.join(' · ');
}
function questObjectiveLine(quest) {
  return (quest.objectives||[]).map(o=>o.name+' '+o.have+'/'+o.need).join(' · ');
}
function renderLedger(quests, note) {
  byId('ledger-list').replaceChildren();
  byId('ledger-status').textContent=note || (quests.length ? quests.length+' accepted · Auto-do a quest, or Open it in game' : 'Accept a quest in the game to get started.');
  for (const quest of quests) {
    const group=document.createElement('div');group.className='ledger-row';
    const row=document.createElement('button');row.className='ledger-quest';
    const name=document.createElement('strong');name.textContent=quest.name;
    const state=document.createElement('span');state.textContent=questBadges(quest);
    row.append(name,state);
    const objectives=questObjectiveLine(quest);
    if(objectives){const counts=document.createElement('span');counts.className='ledger-objectives';counts.textContent=objectives;row.append(counts);}
    row.disabled=!!quest.blocked;
    const open=document.createElement('button');open.className='ledger-open';open.textContent='Open';
    open.setAttribute('aria-label','Open '+quest.name+' in game');
    open.onclick=()=>window.skua.command('active-quest-open',String(quest.id));
    const start=()=>{
      if(quest.blocked){byId('ledger-status').textContent=quest.dailyDone?'This daily quest is already completed today.':quest.locked?'This quest is locked.':quest.member?'This quest requires membership.':'This quest cannot be auto-done yet.';return;}
      startAcceptedQuest(quest,quest.rewards.length?quest.rewards[0].id:-1,true);
    };
    if(quest.rewards.length>1){
      const choice=document.createElement('select');choice.setAttribute('aria-label','Reward for '+quest.name);
      const placeholder=document.createElement('option');placeholder.value='';placeholder.textContent='Choose reward';choice.append(placeholder);
      for(const item of quest.rewards){const option=document.createElement('option');option.value=String(item.id);option.textContent=item.name;choice.append(option);}
      row.onclick=()=>{if(quest.blocked){start();return;}if(!choice.value){byId('ledger-status').textContent='Choose a reward for '+quest.name+' below.';choice.focus();return;}startAcceptedQuest(quest,Number(choice.value),true);};
      group.append(row,open,choice);
    }else {
      row.onclick=start;
      group.append(row,open);
    }
    byId('ledger-list').append(group);
  }
}
byId('ledger-refresh').onclick=()=>pollActiveQuests(true);
let activeQuestGenerating=false,questFromLedger=false,repeatOffer=null;
function hideRepeatPrompt(){
  repeatOffer=null;
  byId('ledger-repeat').hidden=true;byId('active-quest-repeat').hidden=true;
  byId('ledger-repeat-note').textContent='';byId('active-quest-repeat-note').textContent='';
}
function showRepeatPrompt(offer){
  repeatOffer=offer;
  const note=offer.message || ((offer.name||'This quest')+' is a farming quest. Do the same route again?');
  byId('ledger-repeat-note').textContent=note;byId('active-quest-repeat-note').textContent=note;
  byId('ledger-repeat').hidden=false;
  if(byId('active-quest-dialog').open)byId('active-quest-repeat').hidden=false;
}
function startAcceptedQuest(quest,reward,fromLedger=false,repeat=false){
  if(activeQuestGenerating)return;
  hideRepeatPrompt();
  activeQuestGenerating=true;questFromLedger=fromLedger;
  byId('active-quest-cancel').hidden=false;byId('ledger-cancel').hidden=!fromLedger;
  const message=(repeat?'Repeating ':'Generating a script for ')+quest.name+'…';
  byId('active-quest-status').textContent=message;if(fromLedger)byId('ledger-status').textContent=message;
  const payload={id:quest.id,reward}; if(repeat) payload.repeat=true;
  window.skua.command('active-quest-go',JSON.stringify(payload));
}
function acceptRepeat(){
  if(!repeatOffer || activeQuestGenerating)return;
  startAcceptedQuest({id:repeatOffer.id,name:repeatOffer.name||'quest',rewards:[],blocked:false},repeatOffer.reward??-1,true,true);
}
byId('ledger-repeat-yes').onclick=acceptRepeat;
byId('active-quest-repeat-yes').onclick=acceptRepeat;
byId('ledger-repeat-no').onclick=hideRepeatPrompt;
byId('active-quest-repeat-no').onclick=hideRepeatPrompt;
byId('ledger-cancel').onclick=()=>window.skua.command('cancel-active-quest');
byId('active-quest-cancel').onclick=()=>window.skua.command('cancel-active-quest');
let areaBusy=false, areaPlanKey='', areaMap='';
setInterval(()=>{if(engineReady&&gameReady&&!byId('area-view').hidden)window.skua.command('area-location');},5000);
function areaRequest(command,value) {
  if(areaBusy)return;
  areaBusy=true;for(const b of document.querySelectorAll('#area-view button'))b.disabled=true;byId('area-quantity').disabled=true;byId('area-refresh').disabled=true;byId('area-cancel').disabled=false;byId('area-cancel').hidden=false;
  byId('area-status').textContent=(command==='area-plan'||command==='area-acquire')?'Resolving the item and generating its farm…':'Reading area sources…';
  byId('area-go').disabled=true;
  window.skua.command(command,value);
}
function areaFinished(message) {areaBusy=false;for(const b of document.querySelectorAll('#area-view button'))b.disabled=b.id==='area-go'||b.dataset.blocked==='true';byId('area-quantity').disabled=false;byId('area-refresh').disabled=false;byId('area-cancel').hidden=true;byId('area-status').textContent=message||'Ready to explore.';}
function areaButton(label,action) {const b=document.createElement('button');b.textContent=label;b.onclick=()=>{if(!areaBusy)action();};return b;}
function areaList(id,entries,label,action) {
  byId(id).replaceChildren();
  if(!entries.length){const p=document.createElement('p');p.className='hint';p.textContent='None discovered.';byId(id).append(p);}
  for(const entry of entries)byId(id).append(areaButton(label(entry),()=>action(entry)));
}
function areaPlanItem(key) {
  const quantity=Number(byId('area-quantity').value);
  if(!Number.isInteger(quantity)||quantity<1||quantity>999){byId('area-status').textContent='Enter a target quantity from 1 to 999.';return;}
  areaPlanKey='';byId('area-plan-view').hidden=true;areaRequest('area-acquire',JSON.stringify({key,quantity}));
}
function areaOwned(owned) {return (owned?owned.inventory:0)+' in inventory · '+(!owned||owned.bank===null?'bank unknown':owned.bank+' in bank');}
function areaRows(rows) {
  byId('area-items').replaceChildren();byId('area-plan-view').hidden=true;areaPlanKey='';
  if(!rows.length){const p=document.createElement('p');p.className='hint';p.textContent='No items returned for this source.';byId('area-items').append(p);}
  for(const item of rows) {
    const row=document.createElement('div');row.className='area-item';
    const text=document.createElement('div'),name=document.createElement('strong'),detail=document.createElement('p');
    name.textContent=item.name;detail.className='hint';
    detail.textContent=item.temporary?'Quest drop · '+item.quest:(item.id?'#'+item.id+' · ':'')+(item.owned?areaOwned(item.owned):'Permanent monster drop');
    text.append(name,detail);itemPicture(text,item.name);
    if(item.cost!==undefined) {const price=document.createElement('p');price.className='hint';price.textContent=item.cost+' '+(item.coins?'ACs':'gold')+(item.member?' · Membership':'');text.append(price);}
    for(const req of item.requirements||[]) {const ingredient=document.createElement('p');ingredient.className='area-ingredient';ingredient.textContent=req.name+' ×'+req.quantity+' · '+areaOwned(req.owned);itemPicture(ingredient,req.name);text.append(ingredient);}
    const action=areaButton(item.temporary?'Open accepted quests':item.requirements&&item.requirements.length?'Farm & merge':item.cost!==undefined?'Buy item':'Farm item',()=> {
      if(item.temporary){byId('active-quest-dialog').showModal();pollActiveQuests(true);}else areaPlanItem(item.key);
    });
    if(item.coins&&item.cost>0){action.textContent='Open shop in game';action.onclick=()=>areaRequest('area-item-shop',item.key);}
    row.append(text,action);byId('area-items').append(row);
  }
}
function areaTree(node,depth=0,parent) {
  parent=parent||byId('area-plan-tree');
  const row=document.createElement('p');row.className='area-plan-step';row.style.paddingLeft=(depth*16)+'px';
  const descriptions={owned:'Already owned',drop:'Farm '+node.Monster+' in /'+node.Map,shop:'Shop #'+node.Shop+' in /'+node.Map+' · '+node.Cost+' gold per purchase',quest:'Complete quest #'+node.Quest,pickup:'Collect in /'+node.Map};
  row.textContent=node.Name+' ×'+node.Quantity+' — '+(descriptions[node.Kind]||node.Kind);itemPicture(row,node.Name);parent.append(row);
  for(const child of node.Children||[])areaTree(child,depth+1,parent);
}
const ACHIEVEMENT_IMAGES=/^[a-z0-9-]+\.png$/;
function achievementImage(name){return ACHIEVEMENT_IMAGES.test(name)?'brand/achievements/'+name:'';}
function renderAchievements(items,note){
  byId('achievements-status').textContent=note||'';
  byId('achievements-grid').replaceChildren();
  for(const item of items||[]){
    const card=document.createElement('article');card.className='achievement-card'+(item.Earned?'':' locked');
    card.dataset.achievementId=item.Id;
    const img=document.createElement('img');img.alt=item.Title;img.src=achievementImage(item.Image);img.width=196;img.height=196;
    const state=document.createElement('span');state.className='achievement-state';
    state.textContent=item.Earned?(item.Reason==='Story'?'Earned · story complete':'Earned · '+(item.Reason||item.Location||'owned')):(item.Reason||'Not earned yet');
    const title=document.createElement('strong');title.textContent=item.Title;
    const detail=document.createElement('p');detail.className='hint';detail.textContent=item.Detail;
    card.append(img,state,title,detail);
    if(item.CanRun){
      const go=document.createElement('button');go.type='button';go.className='achievement-go';go.textContent='Go';
      go.onclick=()=>{go.disabled=true;byId('achievements-status').textContent='Starting the mapped farm for '+item.Title+'…';window.skua.command('achievements-go',item.Id);};
      card.append(go);
    }
    byId('achievements-grid').append(card);
  }
}
function loadAchievements(){
  window.skua.command('achievements');
}
function refreshAchievements(){
  byId('achievements-refresh').disabled=true;
  byId('achievements-status').textContent='Checking inventory, bank, and story quests…';
  window.skua.command('achievements-refresh');
}
byId('nav-achievements').onclick=()=>{
  hideWorkspaceViews();
  byId('achievements-view').hidden=false;document.body.classList.add('achievements-open');
  for(const item of document.querySelectorAll('.nav-item')){
    item.classList.toggle('active',item.id==='nav-achievements');
    if(item.id==='nav-achievements')item.setAttribute('aria-current','page');else item.removeAttribute('aria-current');
  }
  if(!byId('achievements-grid').children.length) loadAchievements();
};
byId('achievements-refresh').onclick=refreshAchievements;
byId('nav-area').onclick=()=>{
  hideWorkspaceViews();
  byId('area-view').hidden=false;document.body.classList.add('area-open');
  for(const item of document.querySelectorAll('.nav-item')) {item.classList.toggle('active',item.id==='nav-area');if(item.id==='nav-area')item.setAttribute('aria-current','page');else item.removeAttribute('aria-current');}
  if(!byId('area-shops').childElementCount)areaRequest('area-scan');
};
byId('area-refresh').onclick=()=>{areaPlanKey='';byId('area-plan-view').hidden=true;areaRequest('area-scan');};
byId('area-deep').onclick=()=>areaRequest('area-deep');
byId('area-cancel').onclick=()=>window.skua.command('area-cancel');
byId('area-go').onclick=()=>{if(areaPlanKey)areaRequest('area-go',areaPlanKey);};
function receiveArea(message) {
  switch(message.type) {
    case 'area-location':
      if(message.map!==areaMap&&!areaBusy&&!running&&!autoEnabled&&!byId('area-view').hidden){areaMap=message.map;areaPlanKey='';byId('area-plan-view').hidden=true;if(message.map)areaRequest('area-scan');else areaFinished('Log in to discover an area.');}return true;
    case 'area-progress':byId('area-status').textContent=message.message;return true;
    case 'area-error':areaPlanKey='';byId('area-go').disabled=true;areaFinished(message.message);return true;
    case 'area-snapshot':
      areaMap=message.map;areaFinished(message.note);byId('area-title').textContent='What to do in /'+message.map;byId('area-items').replaceChildren();byId('area-plan-view').hidden=true;
      areaList('area-shops',message.shops,s=>s.Name+(s.ID?' · #'+s.ID:''),s=>areaRequest('area-shop',s.Key));
      areaList('area-monsters',message.monsters,m=>m.Name+' · '+m.HP+' HP',m=>areaRequest('area-monster',m.Key));
      areaList('area-quests',message.quests,q=>q.Name,q=>areaRequest('area-quests',q.Path));
      areaList('area-accepted',message.accepted,q=>q.name+(q.ready?' · Ready':''),()=>{byId('active-quest-dialog').showModal();pollActiveQuests(true);});return true;
    case 'area-shop':
      areaFinished();byId('area-detail-title').textContent=message.name+(message.id?' · #'+message.id:'');byId('area-detail-note').textContent=message.note;areaRows(message.items);
      for(const item of message.wikiItems||[]) {const p=document.createElement('p');p.textContent=item.Name;itemPicture(p,item.Name);byId('area-items').append(p);}return true;
    case 'area-drops':areaFinished();byId('area-detail-title').textContent=message.name+' drops';byId('area-detail-note').textContent=message.note;areaRows(message.items);return true;
    case 'area-quests':
      areaFinished();byId('area-detail-title').textContent='Area quests';byId('area-detail-note').textContent=message.note;byId('area-items').replaceChildren();byId('area-plan-view').hidden=true;areaPlanKey='';
      for(const quest of message.quests){
        const row=document.createElement('div');row.className='area-item';
        const title=document.createElement('p');title.textContent=quest.name+(quest.id?' · #'+quest.id:' · Quest ID unavailable');row.append(title);
        if(quest.id){
          row.append(areaButton('Open in game',()=>areaRequest('area-quest-open',quest.key)));
          const accept=areaButton(quest.accepted?'Accepted':'Accept quest',()=>areaRequest('area-quest-accept',quest.key));
          accept.dataset.questKey=quest.key;accept.disabled=quest.accepted;accept.dataset.blocked=String(quest.accepted);row.append(accept);
        }
        byId('area-items').append(row);
      }return true;
    case 'area-quest-action':
      areaFinished(message.message);
      if(message.accepted)for(const button of document.querySelectorAll('#area-items button'))if(button.dataset.questKey===message.key){button.textContent='Accepted';button.disabled=true;button.dataset.blocked='true';}
      pollActiveQuests(true);
      if(message.opened)byId('nav-game').onclick();
      return true;
    case 'area-plan':
      areaFinished('Farm plan ready. Review the ingredients below.');areaPlanKey=message.key;byId('area-plan-view').hidden=false;byId('area-plan-note').textContent=message.note;byId('area-plan-tree').replaceChildren();areaTree(message.root);byId('area-plan-code').textContent=message.code;byId('area-go').disabled=false;return true;
    case 'area-shop-opened':areaFinished(message.message);byId('nav-game').onclick();return true;
    case 'area-starting':areaPlanKey='';areaFinished('Generated script started. Follow its progress in Activity.');byId('area-go').disabled=true;byId('nav-game').onclick();return true;
    default:return false;
  }
}

function handleHostMessage(message) {
  if(message.type==='item-preview'){
    picturePending.delete(message.name);rememberPicture(message.name,message.picture);
    for(const button of document.querySelectorAll('.item-picture'))if(button.dataset.itemName===message.name)paintPicture(button,message.picture);
    return;
  }
  if(receiveArea(message))return;
  switch (message.type) {
    case 'active-quests-error':
      renderLedger([], 'Quest list unavailable. Use Refresh to retry.');
      activeQuestPending=false; activeQuestSignature=''; byId('active-quest-list').replaceChildren(); byId('active-quest-status').textContent='Could not read accepted quests: '+message.message; break;
    case 'active-quest-error':
      activeQuestGenerating=false; byId('active-quest-cancel').hidden=true;
      hideRepeatPrompt();
      byId('ledger-cancel').hidden=true;if(questFromLedger){byId('ledger-status').textContent=message.message;break;}
      byId('active-quest-status').textContent=message.message; if(!byId('active-quest-dialog').open) byId('active-quest-dialog').showModal(); break;
    case 'active-quest-progress':
      if(questFromLedger)byId('ledger-status').textContent=message.message;
      byId('active-quest-status').textContent=message.message; break;
    case 'active-quest-starting':
      byId('ledger-cancel').hidden=true;if(questFromLedger)byId('ledger-status').textContent='Quest script started.';
      activeQuestGenerating=false; byId('active-quest-cancel').hidden=true;
      byId('active-quest-status').textContent='Generated '+message.path; byId('active-quest-dialog').close(); break;
    case 'active-quest-opened':
      byId('ledger-status').textContent=message.message;
      if(message.opened)byId('nav-game').onclick();
      break;
    case 'active-quest-finished':
      activeQuestGenerating=false; byId('active-quest-cancel').hidden=true; byId('ledger-cancel').hidden=true;
      byId('active-quest-status').textContent=message.message;
      byId('ledger-status').textContent=message.message;
      if(message.farmable) showRepeatPrompt(message);
      else hideRepeatPrompt();
      pollActiveQuests(true);
      break;
    case 'active-quests': {
      activeQuestPending=false;
      byId('active-quest-open').hidden=message.quests.length===0;
      byId('active-quest-open').textContent='Auto-do quest · '+message.quests.length;
      const signature=JSON.stringify(message.quests);
      if(signature===activeQuestSignature) break;
      activeQuestSignature=signature; renderLedger(message.quests); byId('active-quest-list').replaceChildren();
      byId('active-quest-status').textContent=message.quests.length ? 'Live accepted quests. Select a reward where required.' : 'No accepted quests. Accept one in the game to enable Auto-do.';
      for(const quest of message.quests) {
        const row=document.createElement('div'); row.className='quest-source';
        const title=document.createElement('p'); title.textContent=quest.name+' · '+questBadges(quest);
        const counts=questObjectiveLine(quest);
        const objectives=document.createElement('p');objectives.className='hint';objectives.textContent=counts;
        const reward=document.createElement('select'); reward.setAttribute('aria-label','Reward for '+quest.name);
        if(quest.rewards.length>1) {const option=document.createElement('option');option.value='';option.textContent='Choose your reward';reward.append(option);}
        for(const item of quest.rewards) {const option=document.createElement('option');option.value=String(item.id);option.textContent=item.name;reward.append(option);}
        reward.hidden=quest.rewards.length===0;
        const go=document.createElement('button');
        go.textContent=quest.blocked?(quest.dailyDone?'Daily already done':quest.locked?'Quest locked':quest.member?'Membership required':'Cannot Auto-do'):quest.ready?'Auto-do — turn in once':'Auto-do this quest';
        go.disabled=!!quest.blocked;
        go.onclick=()=>{
          if(quest.blocked) return;
          if(quest.rewards.length && !reward.value) {byId('active-quest-status').textContent='Select the reward you want first.';return;}
          startAcceptedQuest(quest,quest.rewards.length?Number(reward.value):-1);
        };
        const rewardPreview=document.createElement('div');
        const updateRewardPreview=()=>{rewardPreview.replaceChildren();const chosen=quest.rewards.find(item=>String(item.id)===reward.value);if(chosen)itemPicture(rewardPreview,chosen.name);};
        reward.onchange=updateRewardPreview;updateRewardPreview();
        row.append(title);if(counts)row.append(objectives);row.append(reward,go,rewardPreview);byId('active-quest-list').append(row);
      }
      break;
    }
    case 'catalog-progress': byId('catalog-status').textContent=message.message; break;
    case 'catalog-error': catalogPlanKey='';byId('catalog-plan-view').hidden=true;catalogFinished(message.message); break;
    case 'catalog-plan':
      catalogPlanKey=message.key;byId('catalog-plan-view').hidden=false;byId('catalog-plan-note').textContent=message.note;
      byId('catalog-plan-tree').replaceChildren();areaTree(message.root,0,byId('catalog-plan-tree'));
      byId('catalog-plan-code').textContent=message.code;catalogFinished('Farm plan ready. Review the ingredients below.');
      byId('catalog-go').disabled=false; break;
    case 'catalog-autodo':
      catalogFinished('This item is a reward on an accepted quest. Starting Auto-do…');
      startAcceptedQuest(message.quest,message.reward,true); break;
    case 'catalog-starting':
      catalogPlanKey='';byId('catalog-plan-view').hidden=true;catalogFinished('Generated script started. Follow its progress in Activity.');
      byId('catalog-go').disabled=true;byId('nav-game').onclick(); break;
    case 'quest-catalog-error':
      byId('catalog-search-button').disabled = false; byId('catalog-status').textContent = message.message; break;
    case 'quest-catalog': {
      catalogPage = message.page; byId('catalog-search-button').disabled = false;
      byId('catalog-prev').disabled = catalogPage === 0; byId('catalog-next').disabled = (catalogPage+1)*50 >= message.matches;
      byId('catalog-status').textContent = message.total + ' indexed items · ' + message.matches + ' matches · Page ' + (catalogPage+1) + (message.bankLoaded ? ' · Bank checked' : ' · Bank unavailable; missing status unknown');
      byId('catalog-items').replaceChildren();
      for (const item of message.items) {
        const row = document.createElement('div'); row.className = 'quest-row';
        const body = document.createElement('div'); const title = document.createElement('strong'); title.textContent = item.name + (item.id ? ' · #' + item.id : '');
        const detail = document.createElement('p'); detail.className = 'hint'; detail.textContent = item.category + ' · ' + item.ownership + ' · ' + item.detail;
        const more = document.createElement('details'); const summary = document.createElement('summary'); summary.textContent = 'Item information'; const info = document.createElement('p'); info.textContent = (item.description || '') + ' ' + item.availability; more.append(summary,info); body.append(title,detail,more);itemPicture(body,item.name);
        const find = document.createElement('button'); find.textContent = item.acceptedQuestId ? 'Auto-do this quest' : item.canFind ? 'Find farming plan' : (item.routeNote || item.ownership); find.disabled = !item.canFind;
        find.onclick = () => {
          if(catalogBusy)return;
          catalogBusy=true;catalogPlanKey='';byId('catalog-plan-view').hidden=true;byId('catalog-go').disabled=true;byId('catalog-cancel').hidden=false;
          byId('catalog-status').textContent=(item.acceptedQuestId?'Checking accepted quests for ':'Planning a farm for ')+item.name+'…';
          window.skua.command('catalog-farm',JSON.stringify({name:item.name,id:Number(item.id)||0}));
        };
        row.append(body,find); byId('catalog-items').append(row);
      }
      break;
    }
    case 'quest-error':
      byId('quest-refresh').disabled = false; byId('quest-status').textContent = message.message; break;
    case 'quest-started':
      byId('quest-status').textContent = message.running ? 'Bot started. Use Stop in Script controls to stop it.' : 'Bot finished or was cancelled. Refresh to update your goals.'; break;
    case 'quest-plan': {
      byId('quest-refresh').disabled = false;
      loadCatalog();
      byId('quest-status').textContent = 'Level ' + message.level + ' · ' + (message.bankLoaded ? 'Inventory and bank checked.' : 'Bank unavailable. Missing-item farms are disabled until it can be checked.');
      byId('quest-equipped').textContent = 'Equipped: ' + (message.equipped.join(' · ') || 'No equipment data returned');
      byId('quest-goals').replaceChildren();
      for (const goal of message.goals) {
        const row = document.createElement('div'); row.className = 'quest-row';
        const body = document.createElement('div'); const title = document.createElement('strong'); title.textContent = goal.Item;
        const detail = document.createElement('p'); detail.className = 'hint'; detail.textContent = goal.Detail;
        body.append(title,detail);itemPicture(body,goal.Item);
        const go = document.createElement('button'); go.textContent = goal.Action; go.disabled = !goal.CanRun;
        go.onclick = () => launchQuest(goal.Id); row.append(body,go); byId('quest-goals').append(row);
      }
      byId('quest-event-detail').textContent = message.eventDetail; byId('quest-event-go').disabled = !message.eventAvailable;
      break;
    }
    case 'quest-news':
      byId('quest-news-status').textContent = message.message; byId('quest-news').replaceChildren();
      for (const news of message.posts) {
        const row = document.createElement('div'); row.className = 'quest-news-row';
        const title = document.createElement('strong'); title.textContent = news.Date + ' · ' + news.Title;
        const source = document.createElement('small'); source.textContent = news.Url;
        const read = document.createElement('button'); read.textContent = 'Read announcement'; read.onclick = () => window.skua.command('quest-news-open',news.Url);
        row.append(title,source,read); byId('quest-news').append(row);
      }
      break;
    case 'gear-error': byId('gear-refresh').disabled = false; byId('gear-status').textContent = message.message; break;
    case 'gear-player': {
      const data = message.data;
      byId('gear-refresh').disabled = false;
      byId('gear-items').replaceChildren();
      byId('gear-status').textContent = data.error || data.player + (data.identityNote ? ' — ' + data.identityNote : '') + (data.ownershipNote ? '\n' + data.ownershipNote : '');
      for (const item of data.items || []) {
        const row = document.createElement('div'); row.className = 'gear-row';
        const label = document.createElement('span'); label.textContent = (item.label || item.slot) + ' · ' + (item.name || 'Item #' + item.id + ' — unresolved') + (item.identitySource ? '\n' + item.identitySource + ' · #' + item.id : '');
        const find = document.createElement('button'); const owned = item.ownership === 'Inventory' || item.ownership === 'Bank'; find.textContent = owned ? 'Owned · ' + item.ownership : !item.name ? 'Name unresolved' : 'Find source'; find.disabled = !item.name || owned;
        find.onclick = () => { byId('gear-sources').replaceChildren(); byId('gear-status').textContent = 'Searching scripts for ' + item.name + '…'; window.skua.command('gear-find',JSON.stringify({name:item.name,id:Number(item.id)||0})); };
        itemPicture(label,item.name);row.append(label,find); byId('gear-items').append(row);
      }
      break;
    }
    case 'gear-starting':
      byId('gear-status').textContent = message.message; byId('gear-dialog').close(); hideWorkspaceViews(); break;
    case 'gear-sources': {
      byId('gear-sources').replaceChildren();
      byId('gear-status').textContent = message.message || (message.sources.length ? 'Generated item-specific routes. Review the action before Go.' : 'No verified route found for this item.');
      if (message.url) { const read=document.createElement('button'); read.textContent='Read item source in Chrome'; read.onclick=()=>window.skua.command('gear-wiki-open',message.url); byId('gear-sources').append(read); }
      for (const source of message.sources) {
        const row = document.createElement('div'); row.className = 'gear-source';
        const title = document.createElement('strong'); title.textContent = source.Name;
        const detail = document.createElement('p'); detail.className = 'hint'; detail.textContent = (source.Description || 'No source description available.') + '\n' + source.File;
        const go = document.createElement('button'); go.textContent = source.Action || 'Go — farm item';
        go.onclick = () => { byId('gear-status').textContent = 'Starting ' + source.Name + '…'; window.skua.command('gear-go',source.Id); };
        const preview = document.createElement('details'); const summary = document.createElement('summary'); summary.textContent = 'Generated script'; const code = document.createElement('pre'); code.textContent = source.Code; preview.append(summary,code); itemPicture(detail,source.Item);row.append(title,detail,preview,go); byId('gear-sources').append(row);
      }
      byId('gear-status').scrollIntoView?.({block:'start'});
      break;
    }
    case 'auto-status':
      autoEnabled = message.enabled;
      byId('auto-toggle').textContent = autoEnabled ? 'Disable' : 'Enable';
      byId('auto-toggle').setAttribute('aria-pressed', String(autoEnabled));
      byId('auto-detail').textContent = autoEnabled
        ? message.className + ' · ' + message.mode + '\nHP ' + message.health + ' / ' + message.maximum + '\n' + message.encounter + ': ' + message.target + (message.defense ? '\nLow health: defensive profile preferred' : '') + (!message.supported ? '\nNo matching combo profile; basic attack only.' : '')
        : 'Auto attack off. Follows your equipped class when enabled.';
      break;
    case 'ready': engineReady = true; byId('engine').textContent = 'C# engine · ' + message.architecture; log('Scripting engine connected.'); break;
    case 'load-game':
      game = document.createElement('embed'); game.type = 'application/x-shockwave-flash'; game.src = message.url;
      game.id = 'flash'; game.name = 'flash';
      game.setAttribute('flashvars','skuaMac=1');
      game.setAttribute('allowscriptaccess','always'); game.setAttribute('allowfullscreen','true'); game.setAttribute('wmode','opaque');
      if (byId('placeholder')) byId('placeholder').remove(); byId('game').append(game);
      setTimeout(() => {
        if (!gameReady) {
          let detail = '';
          try { detail = ' Loaded: ' + game.PercentLoaded() + '%. Version: ' + game.GetVariable('$version'); } catch (_) {}
          log(typeof game.CallFunction === 'function' ? 'Flash is running; waiting for the game bridge.' + detail : 'Flash did not initialize. Check the Mac Flash plugin and Rosetta.', 'Error');
        }
      }, 15000);
      break;
    case 'game-ready': gameReady = true; byId('game-status').textContent = 'Game loaded'; log('Game bridge connected. Log in to play.'); loadAchievements(); break;
    case 'achievements':
      byId('achievements-refresh').disabled = false;
      renderAchievements(message.items, (message.character ? message.character + ' · ' : '') + (message.earned||0) + '/' + (message.total||0) + ' earned. ' + (message.note||''));
      for (const id of message.newlyEarned || []) {
        const item = (message.items||[]).find(entry => entry.Id === id);
        log('Achievement earned: ' + (item ? item.Title : id));
      }
      break;
    case 'achievements-started':
      byId('achievements-refresh').disabled = false;
      byId('achievements-status').textContent = message.running ? 'Mapped bot started. Watch progress in Activity, or Stop in Script controls.' : 'Mapped bot finished or was cancelled. Recheck to update badges.';
      byId('nav-game').onclick();
      break;
    case 'achievements-error':
      byId('achievements-refresh').disabled = false;
      byId('achievements-status').textContent = message.message;
      break;
    case 'game-error': gameReady = false; byId('game-status').textContent = 'Game could not load'; log(message.message,'Error'); break;
    case 'setup-error': byId('game-status').textContent = 'Setup required'; if (byId('setup')) byId('setup').textContent = message.message; log(message.message, 'Error'); break;
    case 'app-identity':
      if (typeof message.label === 'string' && message.label) {
        byId('app-version').textContent = message.label;
        if (typeof message.tag === 'string' && message.tag) document.title = 'qexec ' + message.tag;
      }
      break;
    case 'app-update':
      if (!message.message) break;
      byId('app-update-message').textContent = message.message;
      byId('app-update').hidden = false;
      byId('app-update-button').disabled = !!message.applying;
      byId('app-update-dismiss').disabled = !!message.applying;
      byId('app-update-button').textContent = message.applying ? 'Updating…' : 'Update';
      log(message.message);
      break;
    case 'engine-exit':
      stopping = true; restoreGameRendering();
      renderLedger([], 'Engine stopped. Reopen the app to reconnect.');
      activeQuestPending=false; activeQuestSignature=''; byId('active-quest-open').hidden=true; byId('active-quest-list').replaceChildren(); autoEnabled = false; byId('auto-toggle').textContent = 'Enable'; byId('auto-toggle').setAttribute('aria-pressed','false'); byId('auto-detail').textContent = 'Engine stopped.'; engineReady = false; running = false; byId('engine').textContent = 'Engine stopped'; log('C# engine exited (' + message.code + '). Reopen the app to retry.', 'Error'); break;
    case 'selected': selected = true; byId('selected').textContent = message.path.split('/').pop(); byId('selected').title = message.path; byId('script-hint').textContent = 'Ready to run when the game is connected.'; log('Selected ' + byId('selected').textContent); break;
    case 'status': {
      const wasRunning = running;
      running = message.running;
      if (!running) restoreGameRendering();
      else if (!wasRunning) stopping = false;
      byId('engine').textContent = running ? 'Script running' : 'Script idle'; break;
    }
    case 'log': log(message.message, message.kind); break;
    case 'request':
      if (message.kind === 'options') { showOptions(message); break; }
      try {
        window.invokeFlash(message.data).then(value => window.skua.reply(message.id,value),error => window.skua.reply(message.id,null,error.message));
      } catch (error) { window.skua.reply(message.id, null, error.message); }
      break;
  }
}
window.skua.onMessage(message => { handleHostMessage(message); controls(); });
window.receiveHostMessages = messages => {
  if (!Array.isArray(messages)) return;
  for (const message of messages) handleHostMessage(message);
  controls();
};
const THEME_KEY = 'qexec.theme';
const DEFAULT_ACCENT = '#b09add';
const THEME_PALETTES = [
  {id:'violet', accent:'#b09add'},
  {id:'sea', accent:'#6ba0cc'},
  {id:'gold', accent:'#d7a67b'},
  {id:'moss', accent:'#77c985'},
  {id:'rose', accent:'#d88aa8'}
];
function parseAccent(value) {
  return typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value) ? value.toLowerCase() : null;
}
function hexParts(hex) {
  return {r:parseInt(hex.slice(1,3),16), g:parseInt(hex.slice(3,5),16), b:parseInt(hex.slice(5,7),16)};
}
function toHex(r,g,b) {
  return '#' + [r,g,b].map(n => Math.max(0, Math.min(255, Math.round(n))).toString(16).padStart(2,'0')).join('');
}
function mixHex(hex, other, amount) {
  const a = hexParts(hex), b = hexParts(other);
  return toHex(a.r+(b.r-a.r)*amount, a.g+(b.g-a.g)*amount, a.b+(b.b-a.b)*amount);
}
function themeTokens(accent) {
  const ink = '#19191e', paper = '#ffffff';
  const rgb = hexParts(accent);
  return {
    '--sea': accent,
    '--accent-fg': mixHex(accent, paper, 0.28),
    '--accent-soft': mixHex(ink, accent, 0.16),
    '--accent-run': mixHex(ink, accent, 0.22),
    '--accent-press': mixHex(ink, accent, 0.38),
    '--accent-border': mixHex(accent, ink, 0.42),
    '--accent-strong': mixHex(accent, ink, 0.12),
    '--accent-hover': mixHex(accent, paper, 0.42),
    '--vibe-ring': `rgba(${rgb.r},${rgb.g},${rgb.b},0.53)`,
    '--vibe-glow': `rgba(${rgb.r},${rgb.g},${rgb.b},0.33)`,
    '--vibe-label': mixHex(ink, accent, 0.2)
  };
}
function readStoredTheme() {
  try {
    const parsed = JSON.parse(localStorage.getItem(THEME_KEY) || 'null');
    return parseAccent(parsed && parsed.accent) || DEFAULT_ACCENT;
  } catch {
    return DEFAULT_ACCENT;
  }
}
function persistTheme(accent) {
  try { localStorage.setItem(THEME_KEY, JSON.stringify({accent})); } catch { /* private mode */ }
}
function applyTheme(accent) {
  const hex = parseAccent(accent) || DEFAULT_ACCENT;
  const tokens = themeTokens(hex);
  const root = document.documentElement;
  for (const name of Object.keys(tokens)) root.style.setProperty(name, tokens[name]);
  byId('theme-accent').value = hex;
  byId('theme-accent-hex').textContent = hex;
  for (const palette of THEME_PALETTES) {
    byId('theme-'+palette.id).setAttribute('aria-pressed', String(palette.accent === hex));
  }
  persistTheme(hex);
  return hex;
}
byId('theme-open').onclick = () => byId('theme-dialog').showModal();
byId('theme-close').onclick = () => byId('theme-dialog').close();
byId('theme-reset').onclick = () => applyTheme(DEFAULT_ACCENT);
byId('theme-accent').oninput = event => applyTheme(event.target.value);
for (const palette of THEME_PALETTES) {
  byId('theme-'+palette.id).onclick = () => applyTheme(palette.accent);
}
applyTheme(readStoredTheme());
controls();
