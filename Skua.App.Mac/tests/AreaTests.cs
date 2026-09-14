using HtmlAgilityPack;
using Skua.Mac;

static class AreaTests
{
    static void Assert(bool ok,string detail){if(!ok)throw new Exception(detail);}
    public static async Task Run() {
        var questIDs=AreaActivities.MatchingQuestIDs("Skeletal Sabotage",new[]{("Skeletal Sabotage ",375),("skeletal sabotage",375),("Skeletal Sabotage",999),("Skeletal Sabotage II",376)});
        Assert(questIDs.SequenceEqual(new[]{375,999}),"Quest matching trims catalog whitespace, preserves distinct variants and rejects partial names.");
        Assert(AreaActivities.MatchingQuestIDs("Unknown",new[]{("Known",1)}).Length==0,"Unknown quest IDs are never guessed.");
        var imagePage=new HtmlDocument();imagePage.LoadHtml("<div id='page-title'>Example Sword</div><div id='page-content'><img src='http://aqwwiki.wdfiles.com/local--files/image-tags/aclarge.png'><div class='image-container floatright'><img src='http://i.imgur.com/sword.png'><img src='https://evil.test/wrong.png'></div></div>");
        Assert(ItemPreview.Parse(imagePage.DocumentNode,"Example Sword").SequenceEqual(new[]{"https://i.imgur.com/sword.png"}),"Use item artwork, upgrade HTTPS, exclude untrusted images and wiki tags.");
        Assert(ItemPreview.Parse(imagePage.DocumentNode,"Other Sword").Length==0,"Never display artwork from a mismatched item page.");
        Assert(ItemPreview.SafeURL("https://i.imgur.com.evil.test/x")==null && ItemPreview.SafeURL("file:///etc/passwd")==null,"Reject unrelated hosts and local paths.");
        var realDisambiguation=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures","ItemDisambiguation.html"));
        var realArtwork=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures","ItemArtwork.html"));
        var liveFixture=new ItemPreview(path=>Task.FromResult(path=="/necrotic-sword-of-doom"?realDisambiguation:path=="/necrotic-sword-of-doom-sword"?realArtwork:throw new Exception("Unexpected item page: "+path)));
        var actualPicture=await liveFixture.Get("Necrotic Sword of Doom");
        Assert(actualPicture.Images.SequenceEqual(new[]{"https://i.imgur.com/9OzLTIJ.png"}),"Real wiki markup follows Sword instead of Shop and extracts direct-child artwork without AC badge.");
        Assert(!ItemPreview.MatchesTitle("Necrotic Sword of Doom (Shop)","Necrotic Sword of Doom"),"Do not preview a shop as an item.");
        int previewLoads=0;
        var previews=new ItemPreview(async _=>{Interlocked.Increment(ref previewLoads);await Task.Delay(20);return imagePage.DocumentNode.OuterHtml;});
        await Task.WhenAll(Enumerable.Range(0,10).Select(_=>previews.Get("Example Sword")));
        await previews.Get("Example Sword");
        Assert(previewLoads==1,"Concurrent and repeated item previews share a cached lookup.");
        Assert(BecomeOP.Consumables("Void Highlord").Contains("Potent Battle Elixir"),"Physical class setup selects attack-power consumable.");
        Assert(BecomeOP.Consumables("Legion Revenant").Contains("Potent Malevolence Elixir"),"Caster setup selects spell-power consumable.");
        Assert(BecomeOP.Consumables("Unknown").Length==0,"Unknown classes do not receive guessed consumables.");
        var opScript=BecomeOP.Generate("Void Highlord");
        Assert(opScript.Contains("adv.SmartEnhance(expected)") && opScript.Contains("bot.Inventory.IsEquipped(name)") && opScript.Contains("finally"),"Use class enhancement library, verify consumable equip, restore options.");
        File.WriteAllText("/tmp/qexec-op-generated.cs",opScript);
        var plant=new Skua.Core.Models.Items.ItemBase{ID=30997,Name="Plant Found",Quantity=3,Temp=true};
        string mixed="class T {void Run(){Story.MapItemQuest(4500,\"lostruins\",3694,3);Story.KillQuest(4500,\"lostruins\",\"Underworld Hound\");}}";
        Assert(AcceptedQuestRoutes.Parse(mixed,4500,new[]{plant},"fixture").Count==0,"Mixed quest calls must not misclassify plants as monster drops.");
        var aranx=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures","AranxQuests.html"));
        var mixedResolver=new QuestWikiResolver(path=>Task.FromResult(path=="/aranx-s-quests"?aranx:path=="/underworld-hound-1"?File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures","UnderworldHound.html")):path=="/lost-ruins"?"<p><strong>Map Name:</strong> lostruins<br></p>":path=="/celestial-realm"?"<p><strong>Map Name:</strong> celestialrealm<br></p>":path=="/lost-ruins-war"?"<p><strong>Map Name:</strong> lostruinswar<br></p>":throw new Exception("Unexpected mixed quest lookup: "+path)));
        var hound=new Skua.Core.Models.Items.ItemBase{ID=30998,Name="Underworld Hound Defeated",Quantity=5,Temp=true};
        var mixedSplit=AcceptedQuestRoutes.ParseMixed(mixed,4500,new[]{plant,hound},"fixture");
        Assert(mixedSplit.Pickups.Single().MapItemID==3694 && mixedSplit.Drops.Single().Item=="Underworld Hound Defeated" && mixedSplit.Drops.Single().Monster=="Underworld Hound","Literals assign MapItemQuest qty to the unique temp, then leftover temps to KillQuest.");
        Assert(AcceptedQuestRoutes.Parse(mixed,4500,new[]{plant,hound},"fixture").Single().Item=="Underworld Hound Defeated","KillQuest no longer claims the pickup already assigned by MapItemQuest.");
        var offlineMixed=AcceptedQuestRoutes.Verified(4500,new[]{plant,hound});
        Assert(offlineMixed.Pickups.Single().MapItemID==3694 && offlineMixed.Drops.Single().Monster=="Underworld Hound","Both verified objective routes resolve without any network lookup.");
        Assert(AcceptedQuestRoutes.Verified(4501,new[]{plant,hound}).Drops.Count==0,"Never reuse the recipe for a different quest.");
        var fullMixedCode=ActiveQuestMaker.Generate(4500,-1,new[]{plant,hound},offlineMixed.Drops,(_,_)=>null,pickups:offlineMixed.Pickups,returnTo:new QuestReturnPoint("lostruins","Enter","Spawn"));
        Assert(fullMixedCode.Contains("AcquireKnown(bot,4500,30997,\"Plant Found\",3,true,3694)") && fullMixedCode.Contains("QuestHunt.Monster(bot,4500,\"Underworld Hound\",30998,\"Underworld Hound Defeated\",5,true)"),"The full quest gets both the collection and combat steps.");
        File.WriteAllText("/tmp/qexec-full-ruins.cs",fullMixedCode);
        var bothWiki=await mixedResolver.ResolvePlan("Investigate the Ruins",[],new[]{plant,hound},questSources:new[]{"/aranx-s-quests"},pickupMap:"lostruins");
        Assert(bothWiki.Pickups.Count==1 && bothWiki.Drops.Any(d=>d.Monster=="Underworld Hound"),"Real wiki fixtures resolve both objectives, not only the plant.");
        var mixedPlan=await mixedResolver.ResolvePlan("Investigate the Ruins",[],new[]{plant},questSources:new[]{"/aranx-s-quests"},pickupMap:"lostruins");
        Assert(mixedPlan.Drops.Count==0 && mixedPlan.Pickups.Single().Map=="lostruins","Real quest markup identifies Plant Found as a pickup without internet search.");
        var knownPlant=AcceptedQuestRoutes.ParsePickups(mixed,4500,new[]{plant},"fixture");
        var plantCode=ActiveQuestMaker.Generate(4500,-1,new[]{plant},[],(_,_)=>null,pickups:knownPlant);
        Assert(plantCode.Contains("AcquireKnown(bot,4500,30997,\"Plant Found\",3,true,3694)") && !plantCode.Contains("HuntMonster"),"Generate collection for the exact three plants, never hunting them.");
        Assert(plantCode.Contains("Quest step: pickup Plant Found"),"Pickup steps emit the same ledger log prefix as hunt and turn-in.");
        File.WriteAllText("/tmp/qexec-plant-generated.cs",plantCode);
        int pageLoads=0,searches=0;
        var cachedWiki=new AreaDiscovery(_=>{pageLoads++;return Task.FromResult("<p><strong>Map Name:</strong> river<br></p>");},_=>{searches++;return Task.FromResult<IReadOnlyList<string>>([]);});
        await cachedWiki.Map("river",CancellationToken.None);
        await cachedWiki.Map("river",CancellationToken.None);
        await cachedWiki.Page("/river",CancellationToken.None);
        Assert(pageLoads==1 && searches==0,"Repeated area visits reuse verified map and page data instead of repeating requests.");
        using(var stopCache=new CancellationTokenSource()){stopCache.Cancel();bool canceledCache=false;try{await cachedWiki.Map("river",stopCache.Token);}catch(OperationCanceledException){canceledCache=true;}Assert(canceledCache,"Cache hits still honor cancellation.");}
        var portal=new Skua.Core.Models.Items.ItemBase{ID=30996,Name="Portal Revealed",Temp=true,Quantity=1};
        string portalSource="class T {void Run(){Story.MapItemQuest(4499,\"celestialrealm\",3693);}}";
        var portalRoutes=AcceptedQuestRoutes.ParsePickups(portalSource,4499,new[]{portal},"fixture");
        Assert(portalRoutes.Single().MapItemID==3693,"Discover the exact portal pickup call.");
        Assert(AcceptedQuestRoutes.ParsePickups(portalSource,4500,new[]{portal},"fixture").Count==0 && AcceptedQuestRoutes.ParsePickups(portalSource,4499,new[]{portal,portal},"fixture").Count==0,"Never assign another quest's pickup or guess among multiple objectives.");
        portal.Quantity=2;Assert(AcceptedQuestRoutes.ParsePickups(portalSource,4499,new[]{portal},"fixture").Count==0,"Reject pickup amount mismatch.");portal.Quantity=1;
        var portalCode=ActiveQuestMaker.Generate(4499,-1,new[]{portal},Array.Empty<GearDrop>(),(_,_)=>null,pickups:portalRoutes);
        Assert(portalCode.Contains("AcquireKnown(bot,4499,30996,\"Portal Revealed\",1,true,3693)")&&portalCode.Contains("bot.TempInv.Contains(30996,1)")&&portalCode.Contains("EnsureComplete(4499,-1)"),"Generate exact pickup acquisition with item verification and selected quest turn-in.");
        var returnCode=ActiveQuestMaker.Generate(4499,-1,new[]{portal},Array.Empty<GearDrop>(),(_,_)=>null,pickups:portalRoutes,returnTo:new QuestReturnPoint("celestialrealm","r2","Left"));
        Assert(returnCode.IndexOf("AcquireKnown")<returnCode.IndexOf("Quest step: return") && returnCode.IndexOf("Quest step: return")<returnCode.IndexOf("EnsureComplete(4499"),"Return after collecting objectives and before turning in, so completion monitoring cannot interrupt travel.");
        Assert(returnCode.Contains("core.Join(\"celestialrealm\",\"r2\",\"Left\")") && returnCode.Contains("bot.Player.Cell!=\"r2\""),"Return to and verify the recorded cell, with stop guards.");
        File.WriteAllText("/tmp/qexec-portal-generated-test.cs",returnCode);
        var swf=new byte[70000];swf[0]=(byte)'F';swf[1]=(byte)'W';swf[2]=(byte)'S';swf[3]=15;
        int chunks=0,releases=0;
        string? Capture(string name,object[] args) {
            if(name=="beginMapBytes")return "{\"token\":1,\"length\":70000,\"map\":\"crashsite\",\"url\":\"https://game.aq.com/maps/map_crashsite.swf\"}";
            if(name=="endMapBytes"){releases++;return null;}
            chunks++;return Convert.ToHexString(swf.AsSpan((int)args[1],(int)args[2]));
        }
        var captured=await Skua.Core.Scripts.QuestMapPickup.CaptureMapBytes(Capture,"crashsite","map_crashsite.swf",CancellationToken.None);
        Assert(captured.SequenceEqual(swf)&&chunks==2&&releases==1,"Read loaded SWF in bounded chunks and release the snapshot without a network download.");
        bool wrong=false;try{await Skua.Core.Scripts.QuestMapPickup.CaptureMapBytes(Capture,"river","map_crashsite.swf",CancellationToken.None);}catch(InvalidOperationException){wrong=true;}
        Assert(wrong&&releases==2&&chunks==2,"Reject mismatched map before copying bytes; always release snapshot.");
        bool truncated=false;try{await Skua.Core.Scripts.QuestMapPickup.CaptureMapBytes((name,args)=>name=="readMapBytes"?"00":Capture(name,args),"crashsite","map_crashsite.swf",CancellationToken.None);}catch(InvalidOperationException){truncated=true;}
        Assert(truncated&&releases==3,"Reject truncated byte transfer and release snapshot.");
        using(var stop=new CancellationTokenSource()) {stop.Cancel();bool canceled=false;try{await Skua.Core.Scripts.QuestMapPickup.CaptureMapBytes(Capture,"crashsite","map_crashsite.swf",stop.Token);}catch(OperationCanceledException){canceled=true;}Assert(canceled&&releases==4,"Cancellation frees captured map bytes.");}
        var doc=new HtmlDocument();doc.LoadHtml("<div id='page-content'><p><strong>Map Name:</strong> river<br></p><p><strong>Monsters:</strong></p><ul><li><a href='/kuro'>Kuro</a> x1</li></ul><p><strong>Shops:</strong></p><ul><li><a href='/chest-shop'>Chest Shop</a></li><li><a href='https://evil.example/shop'>Bad</a></li></ul><p><strong>Quests:</strong></p><ul><li><a href='/pete-noggle-s-quests'>Pete Noggle's Quests</a></li></ul></div>");
        var map=AreaDiscovery.ParseMap(doc.DocumentNode,"river","/river");
        Assert(map?.Shops.Count==1 && map.Monsters.Single().Name=="Kuro" && map.Quests.Count==1,"Map discovery must keep categorized same-origin source links.");
        Assert(AreaDiscovery.ParseMap(doc.DocumentNode,"battleon","/river")==null,"Reject a search result for a different map.");
        var ids=AreaDiscovery.MapShopIds(new[]{"function a(){root.loadShop(123);root.world.sendLoadShopRequest(456);trace(\"loadShop(999)\"); /* loadShop(998) */ // loadShop(997)\n loadShop(variable);loadShop(123);}"});
        Assert(ids.SequenceEqual(new[]{123,456}),"Map shops use literal calls, never comments, strings, duplicates or variable IDs.");
        string root=Path.Combine(Path.GetTempPath(),"qexec-area-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            File.WriteAllText(Path.Combine(root,"RiverMerge.cs"),"class T {void X(){ merge.StartBuyAllMerge(\"river\",123); core.BuyItem(map:\"river\",shopID:456,name:\"Sword\"); core.BuyItem(\"other\",789,\"Sword\");}} ");
            var shops=AreaDiscovery.IndexShops(root,"river");Assert(shops.Select(s=>s.ID).Order().SequenceEqual(new[]{123,456}),"Index only exact map and literal shop IDs, including named arguments.");
        }finally{Directory.Delete(root,true);}
        var wiki=new AreaDiscovery(_=>Task.FromResult("<div id='page-title'>Kuro</div><div id='page-content'><p><strong>Items Dropped:</strong></p><ul><li><a href='/sword'>Sword</a></li></ul><p><strong>Temporary Items Dropped:</strong></p><ul><li>Chest (Dropped during the '<a href='/twilly-s-quests'>Chest Thumping</a>' quest)</li></ul></div>"));
        var drops=await wiki.Drops("/kuro","Kuro",CancellationToken.None);
        Assert(drops.Count==2 && drops.Single(d=>d.Temporary).Quest=="Chest Thumping" && drops.Single(d=>!d.Temporary).Name=="Sword","Separate permanent drops from quest-only objectives.");
        var stock=new AreaFarmStock();
        Assert(stock.Missing(1,3,2)==1 && stock.Missing(1,2,2)==2,"Partial stocks consumed by one merge branch must not be reused as owned by another.");
        Assert(stock.Missing(2,2,5)==0 && stock.Missing(2,3,5)==0 && stock.Missing(2,1,5)==1,"Shared stock is reserved only once across nested recipes.");
        var plan=new AreaFarmNode(100,"Selected blade","shop","river",Shop:123,ShopItem:1001,Cost:25,Children:new[]{
            new AreaFarmNode(200,"Merged token","shop","river",Shop:123,ShopItem:1002,Quantity:2,Children:new[]{new AreaFarmNode(300,"Metal","drop","river","Kuro",Quantity:3)}),
            new AreaFarmNode(400,"Quest token","quest",Quest:446,Reward:400,Quantity:5,Children:new[]{new AreaFarmNode(2570,"Muck Covered Chest","drop","river","Kuro",Temporary:true,Quantity:1)})});
        string code=AreaFarmPlan.Generate(plan);
        Assert(code.Contains("item.Cost!=25")&&code.Contains("200:2;400:5")&&code.Contains("bot.Shops.BuyItem(item.ID,item.ShopItemID,1)")&&code.Contains("EnsureComplete(446,400)"),"Generated plan validates live merge recipe and cost, farms nested materials and chooses the exact quest reward.");
        Assert(code.Contains("bot.ShouldExit")&&code.Contains("attempt<1000")&&code.Contains("if(!bot.Bank.Loaded)")&&code.Contains("item.Coins && item.Cost>0"),"Plans retain stop, bounded quest retries, bank and premium-currency guards.");
        File.WriteAllText("/tmp/qexec-area-generated-test.cs",code);
        File.WriteAllText("/tmp/qexec-area-drop-generated-test.cs",AreaFarmPlan.Generate(new(0,"Sword","drop","river","Kuro",Quantity:2)));
        Console.WriteLine("PASS: Area discovery, map shop ID extraction, nested merge/quest generation and acquisition guards.");
    }
}
