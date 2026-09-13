/* Renderer has no filesystem, process, or Node access. */
const post = message => console.log('__SKUA_UI__' + JSON.stringify(message));
window.skua = {
  command: (command,value) => post({type:'command',command,value}),
  event: (name,args) => post({type:'event',name,args}),
  reply: (id,value,error) => post({type:'reply',id,value,error}),
  onMessage: callback => {window.receiveHostMessage = callback;}
};
const byId = id => document.getElementById(id);
let autoEnabled = false;
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
setInterval(() => {updateElapsed(); pollActiveQuests();}, 1000);
for (const [id, target] of [['nav-game','game'], ['nav-script','choose'], ['nav-activity','logs']]) {
  byId(id).onclick = () => {
    byId('quest-view').hidden = true; document.body.classList.remove('quest-open');
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
for (const [id, name] of [['choose','choose'],['run','start'],['stop','stop']]) byId(id).onclick = () => window.skua.command(name);
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
window.skuaNextFlashCommands = () => JSON.stringify(flashCommands.splice(0));
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
function renderLedger(quests, note) {
  byId('ledger-list').replaceChildren();
  byId('ledger-status').textContent=note || (quests.length ? quests.length+' accepted · choose a quest to auto-do' : 'Accept a quest in the game to get started.');
  for (const quest of quests) {
    const row=document.createElement('button');row.className='ledger-quest';
    const name=document.createElement('strong');name.textContent=quest.name;
    const state=document.createElement('span');state.textContent='#'+quest.id+' · '+(quest.ready?'Ready to turn in':'In progress');
    row.append(name,state);
    row.onclick=()=>{byId('active-quest-dialog').showModal();byId('active-quest-status').textContent='Choose '+quest.name+' below, then select Auto-do.';};
    byId('ledger-list').append(row);
  }
}
byId('ledger-refresh').onclick=()=>pollActiveQuests(true);
window.skua.onMessage(message => {
  switch (message.type) {
    case 'active-quests-error':
      renderLedger([], 'Quest list unavailable. Use Refresh to retry.');
      activeQuestPending=false; activeQuestSignature=''; byId('active-quest-list').replaceChildren(); byId('active-quest-status').textContent='Could not read accepted quests: '+message.message; break;
    case 'active-quest-error':
      byId('active-quest-status').textContent=message.message; byId('active-quest-dialog').showModal(); break;
    case 'active-quest-starting':
      byId('active-quest-status').textContent='Generated '+message.path; byId('active-quest-dialog').close(); break;
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
        const title=document.createElement('p'); title.textContent=quest.name+' · #'+quest.id+(quest.ready?' · Ready to turn in':'');
        const reward=document.createElement('select'); reward.setAttribute('aria-label','Reward for '+quest.name);
        if(quest.rewards.length>1) {const option=document.createElement('option');option.value='';option.textContent='Choose your reward';reward.append(option);}
        for(const item of quest.rewards) {const option=document.createElement('option');option.value=String(item.id);option.textContent=item.name;reward.append(option);}
        reward.hidden=quest.rewards.length===0;
        const go=document.createElement('button');go.textContent=quest.ready?'Auto-do — turn in once':'Auto-do this quest';
        go.onclick=()=>{
          if(quest.rewards.length && !reward.value) {byId('active-quest-status').textContent='Select the reward you want first.';return;}
          byId('active-quest-status').textContent='Generating a script for '+quest.name+'…';
          window.skua.command('active-quest-go',JSON.stringify({id:quest.id,reward:quest.rewards.length?Number(reward.value):-1}));
        };
        row.append(title,reward,go);byId('active-quest-list').append(row);
      }
      break;
    }
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
        const more = document.createElement('details'); const summary = document.createElement('summary'); summary.textContent = 'Item information'; const info = document.createElement('p'); info.textContent = (item.description || '') + ' ' + item.availability; more.append(summary,info); body.append(title,detail,more);
        const find = document.createElement('button'); find.textContent = item.canFind ? 'Find farming plan' : (item.routeNote || item.ownership); find.disabled = !item.canFind;
        find.onclick = () => { byId('gear-dialog').showModal(); byId('gear-items').replaceChildren(); byId('gear-sources').replaceChildren(); byId('gear-status').textContent = 'Resolving an item-specific route for ' + item.name + '…'; window.skua.command('gear-find',JSON.stringify({name:item.name,id:Number(item.id)||0})); };
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
        body.append(title,detail);
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
        row.append(label,find); byId('gear-items').append(row);
      }
      break;
    }
    case 'gear-starting':
      byId('gear-status').textContent = message.message; byId('gear-dialog').close(); byId('quest-view').hidden = true; document.body.classList.remove('quest-open'); break;
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
        const preview = document.createElement('details'); const summary = document.createElement('summary'); summary.textContent = 'Generated script'; const code = document.createElement('pre'); code.textContent = source.Code; preview.append(summary,code); row.append(title,detail,preview,go); byId('gear-sources').append(row);
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
    case 'game-ready': gameReady = true; byId('game-status').textContent = 'Game loaded'; log('Game bridge connected. Log in to play.'); break;
    case 'game-error': gameReady = false; byId('game-status').textContent = 'Game could not load'; log(message.message,'Error'); break;
    case 'setup-error': byId('game-status').textContent = 'Setup required'; if (byId('setup')) byId('setup').textContent = message.message; log(message.message, 'Error'); break;
    case 'engine-exit':
      renderLedger([], 'Engine stopped. Reopen the app to reconnect.');
      activeQuestPending=false; activeQuestSignature=''; byId('active-quest-open').hidden=true; byId('active-quest-list').replaceChildren(); autoEnabled = false; byId('auto-toggle').textContent = 'Enable'; byId('auto-toggle').setAttribute('aria-pressed','false'); byId('auto-detail').textContent = 'Engine stopped.'; engineReady = false; running = false; byId('engine').textContent = 'Engine stopped'; log('C# engine exited (' + message.code + '). Reopen the app to retry.', 'Error'); break;
    case 'selected': selected = true; byId('selected').textContent = message.path.split('/').pop(); byId('selected').title = message.path; byId('script-hint').textContent = 'Ready to run when the game is connected.'; log('Selected ' + byId('selected').textContent); break;
    case 'status': running = message.running; byId('engine').textContent = running ? 'Script running' : 'Script idle'; break;
    case 'log': log(message.message, message.kind); break;
    case 'request':
      if (message.kind === 'options') { showOptions(message); break; }
      try {
        window.invokeFlash(message.data).then(value => window.skua.reply(message.id,value),error => window.skua.reply(message.id,null,error.message));
      } catch (error) { window.skua.reply(message.id, null, error.message); }
      break;
  }
  controls();
});
controls();
