using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Models.Shops;

namespace Skua.Mac;

public sealed class AreaActivities(IScriptInterface bot,GearFinder finder,GearOwnership ownership,string scriptsRoot)
{
    readonly AreaDiscovery wiki=new();
    readonly Dictionary<string,(string Name,int ID)> areaQuests=new();
    readonly Dictionary<string,AreaShop> shops=new();
    readonly Dictionary<string,AreaMonster> monsters=new();
    readonly Dictionary<string,(AreaDrop Drop,AreaMonster Monster)> drops=new();
    readonly Dictionary<string,ShopItem> items=new();
    readonly Dictionary<int,(AreaShop Shop,ShopItem Item)> loadedItems=new();
    readonly Dictionary<string,(DateTime At,IReadOnlyList<AreaShop> Shops)> shopIndex=new();
    readonly Dictionary<string,(DateTime At,IReadOnlyList<int> IDs)> mapShopIDs=new();
    async Task<IReadOnlyList<AreaShop>> Indexed(string name,CancellationToken ct) {
        if(shopIndex.TryGetValue(name,out var cached) && DateTime.UtcNow-cached.At<TimeSpan.FromMinutes(5))return cached.Shops;
        var found=await Task.Run(()=>AreaDiscovery.IndexShops(scriptsRoot,name),ct);shopIndex[name]=(DateTime.UtcNow,found);return found;
    }
    async Task DiscoverShops(CancellationToken ct,string? selectedName=null) {
        string asset=map+"|"+bot.Map.FilePath;
        if(!mapShopIDs.TryGetValue(asset,out var cached) || DateTime.UtcNow-cached.At>TimeSpan.FromMinutes(5)) {
            cached=(DateTime.UtcNow,AreaDiscovery.MapShopIds(await Skua.Core.Scripts.QuestMapPickup.ReadMapScripts(bot,ct)));mapShopIDs[asset]=cached;
        }
        foreach(int id in cached.IDs) {
            ct.ThrowIfCancellationRequested();Check();
            var existing=shops.Values.FirstOrDefault(s=>s.ID==id);
            if(existing!=null && selectedName!=null && AreaDiscovery.Same(existing.Name,selectedName))return;
            bot.Shops.Load(id);if(!bot.Shops.IsLoaded || bot.Shops.ID!=id)continue;
            string name=bot.Shops.Name;var known=shops.Values.FirstOrDefault(s=>s.ID==id || AreaDiscovery.Same(s.Name,name));
            string key=known?.Key??id.ToString();shops[key]=new(key,name,id,map,known?.Path??"");
            if(selectedName!=null && AreaDiscovery.Same(name,selectedName))return;
        }
    }
    string map="",planKey="",code="";
    public string CurrentMap=>map;
    void Check() {if(!bot.Player.LoggedIn || bot.Map.Name!=map)throw new InvalidOperationException("The area changed. Scan the current area again.");}
    public async Task<object> Scan(Action<string> progress,CancellationToken ct,bool deep=false) {
        if(!bot.Player.LoggedIn)throw new InvalidOperationException("Log in, then discover the current area.");
        map=bot.Map.Name;areaQuests.Clear();shops.Clear();monsters.Clear();drops.Clear();items.Clear();loadedItems.Clear();planKey="";code="";
        var live=bot.Monsters.MapMonsters.ToArray();var accepted=bot.Quests.Active.Select(q=>new {id=q.ID,name=q.Name,ready=q.Status=="c"}).ToArray();
        progress("Discovering /join "+map+"…");
        var indexTask=Indexed(map,ct);
        using var wikiDeadline=CancellationTokenSource.CreateLinkedTokenSource(ct);wikiDeadline.CancelAfter(TimeSpan.FromSeconds(10));
        var mapTask=wiki.Map(map,wikiDeadline.Token);
        foreach(var shop in await indexTask)shops[shop.Key]=shop;
        if(bot.Shops.IsLoaded && bot.Shops.ID>0) {
            // Loaded shop data can belong to a previous map: include only indexed matches.
            string key=bot.Shops.ID.ToString();if(shops.ContainsKey(key))shops[key]=shops[key] with {Name=bot.Shops.Name};
        }
        AreaPage? page=null;string note="";
        try {page=await mapTask;}catch(Exception e) when(!ct.IsCancellationRequested && e is HttpRequestException or TimeoutException or OperationCanceledException){note="Wiki unavailable; showing live monsters and locally indexed shops. "+e.Message;}
        Check();ct.ThrowIfCancellationRequested();
        foreach(var monster in live.DistinctBy(m=>m.Name)) {
            var link=page?.Monsters.FirstOrDefault(m=>AreaDiscovery.Same(m.Name,monster.Name) || m.Name.StartsWith(monster.Name+" (",StringComparison.OrdinalIgnoreCase));
            var key=Guid.NewGuid().ToString("N");monsters[key]=new(key,monster.Name,link?.Path??AreaDiscovery.Slug(monster.Name),monster.MaxHP,monster.Cell);
        }
        foreach(var shop in page?.Shops??[]) {
            var match=shops.Values.FirstOrDefault(s=>AreaDiscovery.Same(s.Name,shop.Name));
            if(match!=null)shops[match.Key]=match with {Path=shop.Path};
            else {string key=Guid.NewGuid().ToString("N");shops[key]=new(key,shop.Name,0,map,shop.Path);}
        }
        if(deep) {
            progress("Discovering additional map shops…");
            try {await DiscoverShops(ct);}
            catch(Exception e) when(!ct.IsCancellationRequested && e is HttpRequestException or InvalidOperationException or System.ComponentModel.Win32Exception) {note+=" Map shop discovery unavailable: "+e.Message;}
        }
        Check();ct.ThrowIfCancellationRequested();
        return new {type="area-snapshot",map,shops=shops.Values,monsters=monsters.Values,quests=page?.Quests??[],accepted,note=note.Length>0?note:page==null?"Map page not verified. Live monsters and local shop routes are still available.":"Area sources found. Select a shop or monster to explore."};
    }
    public async Task<object> Shop(string key,CancellationToken ct) {
        Check();if(!shops.TryGetValue(key,out var shop))throw new InvalidOperationException("Shop selection expired. Refresh the area.");
        if(shop.ID==0) {
            try {await DiscoverShops(ct,shop.Name);}
            catch(Exception e) when(!ct.IsCancellationRequested && e is InvalidOperationException or System.ComponentModel.Win32Exception) { /* Show wiki items if map-byte discovery is unavailable. */ }
            shop=shops[key];
        }
        if(shop.ID==0) {
            // A matching loaded shop supplies its live ID when the player has opened an unindexed NPC shop.
            if(bot.Shops.IsLoaded && AreaDiscovery.Same(bot.Shops.Name,shop.Name)) {shop=shop with {ID=bot.Shops.ID};shops[key]=shop;}
            else {
                var page=await wiki.Page(shop.Path,ct);
                return new {type="area-shop",name=shop.Name,id=0,items=Array.Empty<object>(),wikiItems=AreaDiscovery.Links(AreaDiscovery.Field(page,"Items").Concat((page.SelectSingleNode("//*[@id='page-content']")??page).Descendants("tr").SelectMany(row=>row.Elements("td").Take(1)))),note="Shop ID is not verified yet. Open this shop from its NPC once, then select it here again to read its live items and merge recipes."};
            }
        }
        if(!bot.Shops.IsLoaded || bot.Shops.ID!=shop.ID)bot.Shops.Load(shop.ID);Check();ct.ThrowIfCancellationRequested();
        if(!bot.Shops.IsLoaded || bot.Shops.ID!=shop.ID)throw new InvalidOperationException("Shop did not load. Check NPC or story access.");
        // One bridge snapshot per collection, not a round trip for every item/ingredient.
        var inventory=bot.Inventory.Items.GroupBy(i=>i.ID).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Quantity));
        bool bankLoaded=bot.Bank.Loaded;
        var bank=bankLoaded?bot.Bank.Items.GroupBy(i=>i.ID).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Quantity)):new Dictionary<int,int>();
        object Ownership(ItemBase item)=>new {inventory=inventory.GetValueOrDefault(item.ID),bank=bankLoaded?(int?)bank.GetValueOrDefault(item.ID):null};
        Check();ct.ThrowIfCancellationRequested();items.Clear();
        var rows=new List<object>();
        foreach(var item in bot.Shops.Items) {
            string itemKey=Guid.NewGuid().ToString("N");items[itemKey]=item;loadedItems[item.ID]=(shop,item);
            rows.Add(new {key=itemKey,id=item.ID,name=item.Name,cost=item.Cost,coins=item.Coins,member=item.Upgrade,owned=Ownership(item),requirements=item.Requirements.Select(r=>new {id=r.ID,name=r.Name,quantity=r.Quantity,owned=Ownership(r)})});
        }
        return new {type="area-shop",name=bot.Shops.Name,id=shop.ID,items=rows,wikiItems=Array.Empty<object>(),note="Live shop data. Choose one reward to plan its missing materials. Bank "+(bankLoaded?"checked.":"not checked yet; verified when you plan a farm.")};
    }
    public async Task<object> Monster(string key,CancellationToken ct) {
        Check();if(!monsters.TryGetValue(key,out var monster))throw new InvalidOperationException("Monster selection expired.");
        var found=await wiki.Drops(monster.Path,monster.Name,ct);Check();
        var rows=found.Select(d=> {string id=Guid.NewGuid().ToString("N");drops[id]=(d,monster);return new {key=id,name=d.Name,temporary=d.Temporary,quest=d.Quest};}).ToArray();
        return new {type="area-drops",name=monster.Name,hp=monster.HP,cell=monster.Cell,items=rows,note="Documented drops for this monster. Temporary drops require their quest; permanent drops can be planned below."};
    }
    public async Task<object> QuestPage(string path,CancellationToken ct) {
        Check(); // Only follow a discovered quest page, not an arbitrary renderer URL.
        var page=await wiki.Map(map,ct);
        if(page==null || !page.Quests.Any(q=>q.Path==path))throw new InvalidOperationException("Quest source is no longer listed in this area.");
        var root=await wiki.Page(path,ct);Check();
        var names=root.Descendants("ul").Where(n=>n.GetAttributeValue("class","").Split(' ').Contains("yui-nav")).SelectMany(n=>n.Elements("li")).Select(AreaDiscovery.Text).Distinct().ToArray();
        bot.Quests.LoadCachedQuests();
        var catalog=bot.Quests.CachedDictionary.Values.Select(q=>(q.Name,q.ID)).Concat(bot.Quests.Tree.Select(q=>(q.Name,q.ID))).ToArray();
        areaQuests.Clear();
        var rows=names.SelectMany(name=> {
            var ids=MatchingQuestIDs(name,catalog);
            return (ids.Length>0 ? ids : new[]{0}).Select(id=> {
                string key=Guid.NewGuid().ToString("N");
                if(id>0)areaQuests[key]=(name,id);
                return new {key,name,id,accepted=id>0 && bot.Quests.IsInProgress(id)};
            });
        }).ToArray();
        return new {type="area-quests",quests=rows,note="Open a quest in game or accept it here, then use Accepted quests to auto-do it. Duplicate names show separate quest IDs."};
    }
    public static int[] MatchingQuestIDs(string name,IEnumerable<(string Name,int ID)> catalog)
        => catalog.Where(q=>q.ID>0 && AreaDiscovery.Same(q.Name,name)).Select(q=>q.ID).Distinct().Order().ToArray();
    public async Task<object> QuestAction(string key,bool accept,CancellationToken ct) {
        Check();ct.ThrowIfCancellationRequested();
        if(!areaQuests.TryGetValue(key,out var selected))throw new InvalidOperationException("Quest selection expired. Open the area's quest list again.");
        bot.Quests.Load(selected.ID);
        Skua.Core.Models.Quests.Quest? quest=null;
        for(int attempt=0;attempt<50;attempt++) {
            Check();ct.ThrowIfCancellationRequested();
            quest=bot.Quests.Tree.FirstOrDefault(q=>q.ID==selected.ID);
            if(quest!=null)break;
            await Task.Delay(100,ct);
        }
        if(quest==null)throw new InvalidOperationException("The game did not load this quest. Retry opening it.");
        if(!AreaDiscovery.Same(quest.Name,selected.Name))throw new InvalidOperationException("The game's quest name does not match the catalog. No quest was accepted.");
        Check();ct.ThrowIfCancellationRequested();
        bool accepted=bot.Quests.IsInProgress(selected.ID);
        if(accept && !accepted)accepted=bot.Quests.Accept(selected.ID);
        Check();ct.ThrowIfCancellationRequested();
        return new {type="area-quest-action",key,id=selected.ID,accepted,opened=!accept,
            message=accept ? accepted ? selected.Name+" accepted. Use Accepted quests to auto-do it." : "The game did not accept this quest. Check its prerequisites, membership requirements, or quest limit in the open game panel." : selected.Name+" opened in game."};
    }
    public async Task<object> Plan(string key,int quantity,Action<string> progress,CancellationToken ct) {
        Check();if(quantity<1 || quantity>999)throw new ArgumentException("Choose a quantity from 1 to 999.");
        planKey="";code="";
        progress("Checking inventory and bank before planning materials…");
        if(!await ownership.LoadBank() && items.ContainsKey(key))throw new InvalidOperationException("Bank could not be verified. Refresh and retry before planning merge materials.");
        var stock=new AreaFarmStock();int count=0;
        async Task<AreaFarmNode> Build(ItemBase target,int amount,HashSet<int> ancestors,AreaShop? explicitShop=null,ShopItem? explicitItem=null,AreaMonster? explicitMonster=null) {
            ct.ThrowIfCancellationRequested();Check();if(++count>80 || ancestors.Count>8)throw new InvalidOperationException("Material chain is too large to verify in one plan.");
            progress("Resolving "+target.Name+" ×"+amount+"…");
            int owned=target.ID>0?bot.Inventory.GetQuantity(target.ID)+(bot.Bank.Items.FirstOrDefault(i=>i.ID==target.ID)?.Quantity??0):0;
            int missing=stock.Missing(target.ID,amount,owned);
            if(missing==0)return new(target.ID,target.Name,"owned",Quantity:amount);
            if(target.ID>0 && !ancestors.Add(target.ID))throw new InvalidOperationException("Circular merge recipe for "+target.Name+".");
            var next=new HashSet<int>(ancestors);
            if(explicitMonster!=null)return new(target.ID,target.Name,"drop",map,explicitMonster.Name,Quantity:amount);
            if(explicitShop==null && loadedItems.TryGetValue(target.ID,out var cached)){explicitShop=cached.Shop;explicitItem=cached.Item;}
            if(explicitShop==null) {
                var route=GearRoutes.Shops(scriptsRoot,target.Name,target.ID).FirstOrDefault();
                if(route!=null) {
                    bot.Shops.Load(route.ShopId);ct.ThrowIfCancellationRequested();Check();
                    if(bot.Shops.IsLoaded && bot.Shops.ID==route.ShopId) {
                        explicitItem=bot.Shops.Items.FirstOrDefault(i=>i.ID==target.ID);
                        if(explicitItem!=null)explicitShop=new(route.ShopId.ToString(),bot.Shops.Name,route.ShopId,route.Map,"");
                    }
                }
            }
            if(explicitShop!=null && explicitItem!=null) {
                if(explicitItem.Coins && explicitItem.Cost>0)throw new InvalidOperationException(target.Name+" costs ACs. Acquire it manually, then replan.");
                if(explicitItem.Upgrade && !bot.Player.IsMember)throw new InvalidOperationException(target.Name+" requires membership.");
                var children=new List<AreaFarmNode>();
                foreach(var req in explicitItem.Requirements) {
                    if(req.ID<=0 || req.Quantity<=0)throw new InvalidOperationException("Incomplete merge requirement.");
                    var child=await Build(req,checked(req.Quantity*missing),new(next));children.Add(child with {Quantity=req.Quantity});
                }
                return new(target.ID,target.Name,"shop",explicitShop.Map,Shop:explicitShop.ID,ShopItem:explicitItem.ShopItemID,Cost:explicitItem.Cost,Quantity:amount,Children:children);
            }
            var drop=finder.Drops.FirstOrDefault(d=>!d.Temporary && AreaDiscovery.Same(d.Item,target.Name));
            if(drop==null) {
                var resolved=await new QuestWikiResolver().ResolvePlan("",Array.Empty<string>(),new[]{new ItemBase{ID=target.ID,Name=target.Name,Temp=false,Quantity=amount}},progress,ct);
                drop=resolved.Drops.FirstOrDefault();
            }
            if(drop!=null)return new(target.ID,target.Name,"drop",drop.Map,drop.Monster,Quantity:amount);
            var rewards=bot.Quests.Cached.Where(q=>q.Rewards.Any(r=>r.ID==target.ID)).Take(3).ToArray();
            foreach(var reward in rewards) {
                var quest=bot.Quests.EnsureLoad(reward.ID);if(quest==null || !quest.Rewards.Any(r=>r.ID==target.ID))continue;
                var routes=await new QuestWikiResolver().ResolvePlan(quest.Name,quest.Rewards.Select(r=>r.Name),quest.Requirements,progress,ct);
                var children=new List<AreaFarmNode>();
                foreach(var req in quest.Requirements) {
                    if(!req.Temp){children.Add((await Build(req,checked(req.Quantity*missing),new(next))) with {Quantity=req.Quantity});continue;}
                    var d=routes.Drops.FirstOrDefault(d=>d.Temporary && AreaDiscovery.Same(d.Item,req.Name));
                    var p=routes.Pickups.FirstOrDefault(p=>p.Temporary && AreaDiscovery.Same(p.Item,req.Name));
                    if(d!=null)children.Add(new(req.ID,req.Name,"drop",d.Map,d.Monster,Temporary:true,Quantity:req.Quantity));
                    else if(p!=null)children.Add(new(req.ID,req.Name,"pickup",p.Map,Quest:quest.ID,Temporary:true,Quantity:req.Quantity));
                    else throw new InvalidOperationException("Missing material quest source: "+req.Name+". No partial script was started.");
                }
                return new(target.ID,target.Name,"quest",Quest:quest.ID,Reward:target.ID,Quantity:amount,Children:children);
            }
            throw new InvalidOperationException("No verified acquisition source for "+target.Name+" (#"+target.ID+"). No partial farm was started.");
        }
        AreaFarmNode root;
        if(items.TryGetValue(key,out var item) && loadedItems.TryGetValue(item.ID,out var source)) {if(item.MaxStack>0 && quantity>item.MaxStack)throw new InvalidOperationException("Target exceeds this item’s stack limit of "+item.MaxStack+".");root=await Build(item,quantity,new(),source.Shop,item);}
        else if(drops.TryGetValue(key,out var selected)) {
            if(selected.Drop.Temporary)throw new InvalidOperationException("Use Auto-do for the drop's accepted quest.");
            int id=bot.Quests.Cached.SelectMany(q=>q.Rewards.Concat(q.Requirements)).FirstOrDefault(i=>AreaDiscovery.Same(i.Name,selected.Drop.Name))?.ID??0;
            root=await Build(new ItemBase{ID=id,Name=selected.Drop.Name},quantity,new(),explicitMonster:selected.Monster);
        } else throw new InvalidOperationException("Item selection expired. Select the shop or monster again.");
        ct.ThrowIfCancellationRequested();Check();code=AreaFarmPlan.Generate(root);planKey=Guid.NewGuid().ToString("N");
        return new {type="area-plan",key=planKey,root,code,note="Review the material tree, then start. Quantities are per purchase; inventory is rechecked at every step. Gold purchases and merge materials are consumed; AC purchases are blocked."};
    }
    public async Task<string> Acquire(string key,int quantity,Action<string> progress,CancellationToken ct) {
        await Plan(key,quantity,progress,ct);
        ct.ThrowIfCancellationRequested();Check();
        return Script(planKey);
    }
    public object OpenItemShop(string key) {
        Check();
        if(!items.TryGetValue(key,out var item) || !loadedItems.TryGetValue(item.ID,out var source))throw new InvalidOperationException("Shop item selection expired. Select the shop again.");
        bot.Shops.Load(source.Shop.ID);Check();
        if(!bot.Shops.IsLoaded || bot.Shops.ID!=source.Shop.ID)throw new InvalidOperationException("Shop did not open. Check access and retry.");
        return new {type="area-shop-opened",message=source.Shop.Name+" opened in game."};
    }
    public string Script(string key) {Check();if(key!=planKey || string.IsNullOrEmpty(code))throw new InvalidOperationException("Plan expired. Generate it again.");string dir=Path.Combine(scriptsRoot,"Generated-Area");Directory.CreateDirectory(dir);string path=Path.Combine(dir,"Area-"+Guid.NewGuid().ToString("N")+".cs");File.WriteAllText(path,code);planKey="";return path;}
}
