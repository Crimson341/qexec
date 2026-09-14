using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Skua.Core;
using Skua.Core.Scripts;
using Skua.Mac;

static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
Assert(FlashXml.Encode(1.25).Value == "1.25", "Numbers must not use the system's decimal separator.");
Assert((double)FlashXml.Decode(XElement.Parse("<number>1.25</number>"))! == 1.25, "Decode numbers invariantly.");
var text = "quotes \" & < > '";
Assert((string)FlashXml.Decode(FlashXml.Encode(text))! == text, "Preserve escaped strings.");
Assert((bool)FlashXml.Decode(XElement.Parse("<true/>"))!, "Decode empty true elements.");
Assert(FlashXml.Decode(XElement.Parse("<undefined/>")) is null, "Decode undefined.");
var array = (object?[])FlashXml.Decode(XElement.Parse("<array><property id='2'><string>x</string></property><property id='0'><false/></property></array>"))!;
Assert(array.Length == 3 && array[1] is null && (string)array[2]! == "x", "Preserve sparse array indexes.");
try { FlashXml.Decode(XElement.Parse("<array><property id='-1'><null/></property></array>")); throw new Exception("Accepted a negative array index."); }
catch (FormatException) { }
var invocation = XElement.Parse(FlashXml.Invoke("a\"b", new object[] {text, true, 1.25}));
Assert((string)invocation.Attribute("name")! == "a\"b", "Escape function names.");

var output = new MessageWriter();
using var rpc = new Rpc(output);
var first = Task.Run(() => rpc.Request("first", new { }));
var message1 = JObject.Parse(output.Messages.Take());
var second = Task.Run(() => rpc.Request("second", new { }));
var message2 = JObject.Parse(output.Messages.Take());
rpc.Reply(new JObject { ["id"] = message2["id"], ["value"] = "second" });
rpc.Reply(new JObject { ["id"] = message1["id"], ["value"] = "first" });
Assert((string)(await first)! == "first" && (string)(await second)! == "second", "Route out-of-order replies by id.");
try { rpc.Request("timeout", new { }, TimeSpan.FromMilliseconds(30)); throw new Exception("Missing timeout."); }
catch (TimeoutException) { }
output.Messages.Take();
var disconnect = Task.Run(() => rpc.Request("disconnect", new { }));
output.Messages.Take();
rpc.Dispose();
try { await disconnect; throw new Exception("Disconnect did not release the waiting script."); }
catch (IOException) { }
Console.WriteLine("PASS: Flash XML values, escaping, sparse arrays, concurrent replies, timeout, and disconnect.");

Assert(CombatPolicy.NeedsDefense(39,100,false), "Enter defense below 40%.");
Assert(!CombatPolicy.NeedsDefense(50,100,false), "Do not enter defense at 50%.");
Assert(CombatPolicy.NeedsDefense(50,100,true), "Keep defense until recovery to avoid oscillation.");
Assert(!CombatPolicy.NeedsDefense(65,100,true), "Leave defense at 65%.");
Assert(!CombatPolicy.IsBoss(99999,3000), "Normal targets should not use boss mode.");
Assert(CombatPolicy.IsBoss(200000,3000), "High-health targets use boss estimate.");
var profiles = new[] {
    new Skua.Core.Models.Skills.AdvancedSkill("Mage","1",classUseMode:"Farm"),
    new Skua.Core.Models.Skills.AdvancedSkill("Mage","2",classUseMode:"Solo"),
    new Skua.Core.Models.Skills.AdvancedSkill("Mage","3",classUseMode:"Def") };
Assert(CombatPolicy.Select(profiles,"mage",false,false)?.Skills == "1", "Choose farm combo case-insensitively.");
Assert(CombatPolicy.Select(profiles,"Mage",false,true)?.Skills == "2", "Choose solo for boss estimate.");
Assert(CombatPolicy.Select(profiles,"Mage",true,true)?.Skills == "3", "Defense takes precedence over boss mode.");
Assert(CombatPolicy.Select(profiles,"Unknown",false,false) == null, "Never use another class's combo.");
Assert(QuestHunt.Owned(1,4,2,false)==5 && QuestHunt.Owned(1,4,2,true)==2,"Permanent counts include bank; temp items use temp inventory.");
var stallStart=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
Assert(QuestHunt.Stalled(2,5,2,stallStart,stallStart.AddMinutes(3),TimeSpan.FromMinutes(3)),"Unchanged quantity for the stall window is stuck.");
Assert(!QuestHunt.Stalled(3,5,2,stallStart,stallStart.AddMinutes(3),TimeSpan.FromMinutes(3)),"A quantity gain is not a stall.");
Assert(QuestHunt.HasMonster(new[]{new Skua.Core.Models.Monsters.Monster{Name=" Infernal Mage "}},"Infernal Mage"),"Monster names match after trim.");
Console.WriteLine("PASS: Adaptive combat health thresholds, recovery hysteresis, encounter estimate, and class profile selection.");

var heroSouls = new[] {new Skua.Core.Models.Items.ItemBase { ID=13949, Name="Hero Souls", Quantity=5, Temp=true }};
try { ActiveQuestMaker.ThrowIfManualObjective(2423,heroSouls); throw new Exception("PvP objective was treated as an ordinary drop."); }
catch(InvalidOperationException ex) { Assert(ex.Message.Contains("player kills") && ex.Message.Contains("/doomarena"),"Give actionable PvP guidance."); }
ActiveQuestMaker.ThrowIfManualObjective(2423,[]);
ActiveQuestMaker.ThrowIfManualObjective(9999,heroSouls);
Assert(ActiveQuestMaker.Generate(2423,-1,[],[],(_,_)=>null).Contains("EnsureComplete(2423,-1)"),"Completed PvP quests can still be turned in.");
Console.WriteLine("PASS: PvP objective guidance, exact quest scope, and completed quest turn-in.");

var gearRoot = Path.Combine(Path.GetTempPath(), "skua-gear-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(gearRoot);
try {
    File.WriteAllText(Path.Combine(gearRoot,"Farm.cs"), "public void ScriptMain() { Core.HuntMonster(\"battleon\", \"Slime\", \"Example Sword\", 1, false); }");
    File.WriteAllText(Path.Combine(gearRoot,"Misleading.cs"), "public void ScriptMain() { Get(\"Example Sword\"); }");
    var finder = new GearFinder(gearRoot,Path.Combine(gearRoot,"missing.json"));
    var found = finder.Find("Example Sword");
    Assert(found.Count == 1 && found[0].Description.Contains("Slime"), "Extract literal farming source, not arbitrary item mentions.");
    var generated = finder.Resolve(found[0].Id);
    Assert(generated.Contains("Generated-Gear") && generated != Path.Combine(gearRoot,"Farm.cs"), "Create a new dedicated script.");
    var code = File.ReadAllText(generated);
    Assert(code.Contains("bot.Inventory.Contains(\"Example Sword\")") && code.Contains("bot.Bank.Contains(\"Example Sword\")") && code.Contains("HuntMonster(\"battleon\", \"Slime\", \"Example Sword\", 1, false)"), "Generate exact goal and source.");
    Assert(!Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code).GetDiagnostics().Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), "Generated C# parses.");
    Assert(finder.Find("Example Sword").Count == 1, "Ignore generated files as evidence.");
    Assert(finder.Find("Example").Count == 0, "Avoid partial-name matches.");
    File.WriteAllText(Path.Combine(gearRoot,"QuestDrops.cs"), "public void Farm() { Core.HuntMonster(\"forest\", \"Wolf\", \"Fang\", 5); }");
    var questFile = Path.Combine(gearRoot,"quests.json");
    File.WriteAllText(questFile, "[{\"ID\":42,\"Name\":\"Sword Quest\",\"Requirements\":[{\"sName\":\"Fang\",\"iQty\":5,\"bTemp\":\"1\"}],\"Rewards\":[{\"sName\":\"Quest Sword\",\"ItemID\":123}]}]");
    var questFinder = new GearFinder(gearRoot,questFile);
    var questPlans = questFinder.Find("Quest Sword");
    Assert(questPlans.Count == 1 && questPlans[0].Code.Contains("EnsureAccept(42)") && questPlans[0].Code.Contains("EnsureComplete(42, 123)"), "Generate only the selected quest reward with resolved objectives.");
    Assert(questPlans[0].Code.Contains("HuntMonster(\"forest\", \"Wolf\", \"Fang\", 5, true)"), "Preserve temporary quest requirements and quantities.");
    File.WriteAllText(questFile, File.ReadAllText(questFile).Replace("Fang", "Unknown objective"));
    Assert(questFinder.Find("Quest Sword").Count == 0, "Block unresolved prerequisites instead of inventing sources.");

    try { finder.Resolve(found[0].Id); throw new Exception("Accepted stale source."); } catch (ArgumentException) { }
    try { finder.Resolve("../../script.cs"); throw new Exception("Accepted arbitrary path."); } catch (ArgumentException) { }
} finally { Directory.Delete(gearRoot,true); }
var cacheRoot=Path.Combine(Path.GetTempPath(),"skua-script-cache-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(cacheRoot);
try {
    var cacheFile=Path.Combine(cacheRoot,"Farm.cs");
    File.WriteAllText(cacheFile,"public void ScriptMain() { Core.HuntMonster(\"battleon\", \"Slime\", \"Cache Sword\", 1, false); }");
    Assert(new GearFinder(cacheRoot).Drops.Single().Monster=="Slime","Index first script contents.");
    File.WriteAllText(cacheFile,"public void ScriptMain() { Core.HuntMonster(\"battleon\", \"Wolf\", \"Cache Sword\", 1, false); }");
    File.SetLastWriteTimeUtc(cacheFile,File.GetLastWriteTimeUtc(cacheFile).AddSeconds(2));
    Assert(new GearFinder(cacheRoot).Drops.Single().Monster=="Wolf","Edited script files invalidate the shared evidence cache.");
} finally { Directory.Delete(cacheRoot,true); }
Console.WriteLine("PASS: Gear source matching, helper exclusion, and candidate validation.");

var characterHtml = "<h1>Chibisaur</h1><label>Class:</label><a>Blaze Binder</a><label>Weapon:</label><a>Dark Wizard&#39;s Disparage</a><label>Armor:</label><a>Chaos Doom Robe</a><label>Helm:</label><a>Diabolical Witch Hat + Blindfold</a><label>Cape:</label><a>Cape of Awe</a><label>Pet:</label><a>Gravelyn Bank Buddy</a>";
var gearNames = GearIdentity.ParsePage(characterHtml,"chibisaur");
Assert(gearNames.Count == 6 && gearNames["Weapon"] == "Dark Wizard's Disparage", "Resolve all six character-page names and decode entities.");
Assert(GearIdentity.SlotLabel("ar") == "Class" && GearIdentity.SlotLabel("co") == "Armor", "Keep class and cosmetic armor distinct.");
try { GearIdentity.ParsePage(characterHtml,"SomeoneElse"); throw new Exception("Accepted mismatched character."); } catch (InvalidDataException) { }
var identityFile = Path.GetTempFileName();
try {
    File.WriteAllText(identityFile,"[{\"Rewards\":[{\"ItemID\":19144,\"sName\":\"Chaos Doom Robe\"}]}]");
    var identity = new GearIdentity(identityFile);
    var inspected = JObject.Parse("{\"items\":[{\"slot\":\"co\",\"id\":19144,\"name\":\"\"},{\"slot\":\"pe\",\"id\":85445,\"name\":\"\"}]}");
    identity.ResolveLocal(inspected);
    Assert((string?)inspected["items"]![0]!["name"] == "Chaos Doom Robe", "Resolve missing names by exact item ID.");
    Assert((string?)inspected["items"]![1]!["name"] == "", "Do not guess unknown item IDs.");
} finally { File.Delete(identityFile); }
Console.WriteLine("PASS: Equipment ID resolution, six character-page slots, HTML decoding, and mismatched-player rejection.");

var ownedItem = new Skua.Core.Models.Items.InventoryItem { ID = 19144, Name = "Chaos Doom Robe" };
var emptyItems = Array.Empty<Skua.Core.Models.Items.InventoryItem>();
Assert(GearOwnership.Locate(19144,"",new[]{ownedItem},emptyItems,false) == "Inventory", "Inventory ownership needs no bank lookup.");
Assert(GearOwnership.Locate(19144,"",emptyItems,new[]{ownedItem},true) == "Bank", "Recognize bank ownership by ID.");
Assert(GearOwnership.Locate(0,"chaos doom robe",emptyItems,new[]{ownedItem},true) == "Bank", "Recheck named plans case-insensitively.");
Assert(GearOwnership.Locate(999,"Chaos Doom Robe",emptyItems,new[]{ownedItem},true) == "Missing", "Different IDs must not be conflated by name.");
Assert(GearOwnership.Locate(19144,"",emptyItems,emptyItems,false) == "Unknown", "Unloaded bank is not proof of absence.");
Console.WriteLine("PASS: Inventory and bank ownership, ID precedence, and unavailable bank handling.");

var vhl = new Skua.Core.Models.Items.InventoryItem { Name = "Void Highlord", Equipped = true };
var questGoals = QuestPlanner.Recommend(new[]{vhl},emptyItems,true,100,_ => true);
Assert(!questGoals.Single(g => g.Id == "vhl").CanRun, "Do not offer owned class farming.");
Assert(questGoals.Single(g => g.Id == "dragon").Priority > questGoals.Single(g => g.Id == "revenant").Priority, "Prefer missing roles over duplicate roles.");
Assert(!QuestPlanner.Recommend(emptyItems,new[]{vhl},true,100,_ => true).Single(g => g.Id == "vhl").CanRun, "Banked goals are owned.");
Assert(QuestPlanner.Recommend(emptyItems,emptyItems,false,100,_ => true).All(g => !g.CanRun), "Unknown bank blocks missing goals.");
Assert(QuestPlanner.Recommend(emptyItems,emptyItems,true,1,_ => true).All(g => !g.CanRun), "Respect suggested level gates.");
Assert(QuestPlanner.Recommend(emptyItems,emptyItems,true,100,_ => false).All(g => !g.CanRun), "Missing bots cannot launch.");
Assert(!QuestPlanner.PirateVerified(new DateTime(2026,9,19)), "Expired event review disables its mapping.");
var newsPosts = QuestPlanner.ParseNews("<p class=\"date\">September 11, 2026</p><h2><a href=\"/gamedesignnotes/example\">Bosses &amp; rewards</a></h2>");
Assert(newsPosts.Count == 1 && newsPosts[0].Title == "Bosses & rewards" && newsPosts[0].Date == "2026-09-11", "Read dated official announcements.");
Assert(QuestPlanner.ParseNews("<p class=\"date\">September 11, 2026</p><h2><a href=\"https://evil.test\">Fake</a></h2>").Count == 0, "Only accept official news paths.");
Console.WriteLine("PASS: Quest ownership, role ranking, levels, bot availability, dated news and event expiry.");

var emptyAchievements = new Dictionary<string, AchievementAward>();
var vhlOwned = Achievements.Evaluate(new[]{vhl},emptyItems,true,_ => false,emptyAchievements);
Assert(vhlOwned.Single(a => a.Id == "vhl").Earned && vhlOwned.Single(a => a.Id == "vhl").Reason == "Inventory", "VHL in inventory earns the milestone.");
Assert(vhlOwned.Count(a => a.Earned) == 1, "Owning VHL does not grant unrelated milestones.");
Assert(!Achievements.Evaluate(emptyItems,emptyItems,false,_ => false,emptyAchievements).Single(a => a.Id == "vhl").Earned, "Unknown bank does not invent a VHL award.");
var looStory = Achievements.Evaluate(emptyItems,emptyItems,true,id => id == 7165,emptyAchievements);
Assert(looStory.Single(a => a.Id == "order").Earned && looStory.Single(a => a.Id == "order").Reason == "Story", "Lord of Order story quest 7165 awards the milestone.");
var bankedNsod = new Skua.Core.Models.Items.InventoryItem { ID = 30629, Name = "Necrotic Sword of Doom" };
Assert(Achievements.Evaluate(emptyItems,new[]{bankedNsod},true,_ => false,emptyAchievements).Single(a => a.Id == "nsod").Reason == "Bank", "Banked NSoD is owned.");
var storeFile = Path.GetTempFileName();
try {
    var detected = Achievements.Evaluate(new[]{vhl},emptyItems,true,_ => false,emptyAchievements);
    var merged = Achievements.MergeAwards(emptyAchievements,detected,1_700_000_000_000);
    Assert(merged["vhl"].Reason == "Inventory" && merged["vhl"].EarnedAt == 1_700_000_000_000, "First detection persists an award timestamp.");
    var store = Achievements.LoadStore(storeFile);
    Achievements.WriteAwards(store, Achievements.CharacterKey("Scott"), merged);
    Achievements.SaveStore(storeFile, store);
    var reloaded = Achievements.ReadAwards(Achievements.LoadStore(storeFile), "scott");
    var stillEarned = Achievements.Evaluate(emptyItems,emptyItems,true,_ => false,reloaded);
    Assert(stillEarned.Single(a => a.Id == "vhl").Earned && stillEarned.Single(a => a.Id == "vhl").EarnedAt == 1_700_000_000_000, "Persisted awards survive a later missing inventory.");
    var again = Achievements.MergeAwards(reloaded, stillEarned, 1_800_000_000_000);
    Assert(again["vhl"].EarnedAt == 1_700_000_000_000, "Re-checks do not rewrite the original award time.");
} finally { File.Delete(storeFile); if (File.Exists(storeFile+".tmp")) File.Delete(storeFile+".tmp"); }
Assert(Achievements.Catalog.Select(a => a.Id).Distinct().Count() == Achievements.Catalog.Length, "Achievement ids are unique.");
Assert(Achievements.Catalog.All(a => a.Image.EndsWith(".png")), "Every achievement ships an image name.");
Console.WriteLine("PASS: Achievement inventory, story, bank, and persisted awards.");

var bankProbe = new Skua.Core.Scripts.ScriptBank(null!,null!,null!,null!,null!,null!,null!,null!);
CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger.Default.Send<Skua.Core.Messaging.BankLoadedMessage,int>(new(),(int)Skua.Core.Messaging.MessageChannels.GameEvents);
Assert(bankProbe.Loaded,"GameEvents bank response marks bank loaded.");
CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger.Default.Send<Skua.Core.Messaging.LogoutMessage,int>(new(),(int)Skua.Core.Messaging.MessageChannels.GameEvents);
Assert(!bankProbe.Loaded,"Logout invalidates bank state on same channel.");
CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger.Default.UnregisterAll(bankProbe);
var catalogFixture = Path.GetTempFileName();
try {
    File.WriteAllText(catalogFixture,"[{\"ID\":42,\"Name\":\"Sword quest\",\"Rewards\":[{\"ItemID\":100,\"sName\":\"Sword\",\"Category\":\"Sword\"}],\"Requirements\":[{\"ItemID\":200,\"sName\":\"Ore\",\"bTemp\":\"1\"}]}]");
    var indexed = QuestCatalog.Build(catalogFixture,new[]{new GearDrop("mine","Slime","Sword",false,"fixture")});
    Assert(indexed.Count==2 && indexed.Single(i=>i.Id==200).NeededFor.Count==1,"Automatically index reward and material dependencies.");
    Assert(indexed.Single(i=>i.Id==100).DropsFrom.Count==1,"Attach exact script drop evidence.");
    Assert(indexed.Single(i=>i.Id==100).RewardQuests.Single().Id==42,"Remember which accepted quest can Auto-do a catalog reward.");
    var catalogScripts = Path.Combine(Path.GetTempPath(),"catalog-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(catalogScripts);
    try {
        var catalog = new QuestCatalog(catalogFixture,new GearFinder(catalogScripts));
        var storedSword = new[]{new Skua.Core.Models.Items.InventoryItem{ID=100,Name="Sword"}};
        var missing = JObject.FromObject(catalog.Query("{\"filter\":\"missing\"}",emptyItems,storedSword,true));
        Assert((int?)missing["matches"]==0,"Banked reward and temporary material excluded from missing gear.");
        var unknown = JObject.FromObject(catalog.Query("{}",emptyItems,emptyItems,false));
        Assert((bool?)unknown["items"]![0]!["canFind"]==false,"Unknown bank blocks catalog farms.");
        var acceptedReward = JObject.FromObject(catalog.Query("{\"search\":\"Sword\"}",emptyItems,emptyItems,true,new HashSet<int>{42}));
        Assert((int?)acceptedReward["items"]![0]!["acceptedQuestId"]==42,"Catalog marks a missing reward when that quest is currently accepted.");
        var idleReward = JObject.FromObject(catalog.Query("{\"search\":\"Sword\"}",emptyItems,emptyItems,true));
        Assert((int?)idleReward["items"]![0]!["acceptedQuestId"]==0,"No accepted-quest Auto-do when the reward quest is not accepted.");
        var material = JObject.FromObject(catalog.Query("{\"filter\":\"materials\",\"search\":\"Sword quest\"}",emptyItems,emptyItems,true));
        Assert((int?)material["matches"]==1 && (int?)material["items"]![0]!["id"]==200,"Search required materials by quest name.");
    } finally { Directory.Delete(catalogScripts,true); }
} finally { File.Delete(catalogFixture); }
Console.WriteLine("PASS: Bank event channel and logout reset; automatic catalog dependency and drop indexing.");

var sparseOwned = GearOwnership.ParseOwnershipItems("[{\"ItemID\":4884,\"sName\":\"Amulet\",\"bUpg\":null,\"iQty\":1}]");
Assert(sparseOwned.Count==1 && sparseOwned[0].ID==4884,"Ownership parser ignores unrelated nullable metadata.");
try { GearOwnership.ParseOwnershipItems("null"); throw new Exception("Accepted unloaded inventory."); } catch(InvalidDataException) { }
Assert(GearIdentity.SlotLabel("ho")=="House","Label house slot clearly.");
Console.WriteLine("PASS: Sparse inventory ownership and house slot naming.");

var routeRoot=Path.Combine(Path.GetTempPath(),"skua-routes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(routeRoot);
try {
    File.WriteAllText(Path.Combine(routeRoot,"Shop.cs"),"class Shop { void Run(){ Core.BuyItem(\"museum\",1994,\"Example Helm\"); Core.BuyItem(\"other\",1,\"Example Helm Replica\"); } }");
    File.WriteAllText(Path.Combine(routeRoot,"Merge.cs"),"class Merge { void Run(){ Adv.StartBuyAllMerge(\"underworld\",238,find,buy); } object option = new Option<bool>(\"34143\",\"Legion Castle\",\"Help\",false); }");
    Assert(GearRoutes.Shops(routeRoot,"Example Helm",123).Count==1,"Match exact shop item name, not substring.");
    Assert(GearRoutes.Shops(routeRoot,"Legion Castle",34143).Single().ShopId==238,"Resolve merge shop by exact option item ID.");
    Assert(GearRoutes.Shops(routeRoot,"Legion Castle",99999).Count==0,"Do not conflate merge item IDs.");
    var routeFinder=new GearFinder(routeRoot,Path.Combine(routeRoot,"missing.json"));
    var routes=routeFinder.Find("Example Helm",123);
    Assert(routes.Count==2 && routes[0].Code.Contains("i.ID == 123") && routes[0].Code.Contains("bot.Shops.Load(1994)"),"Generate exact-ID shop script.");
    Assert(!routes[0].RequiresMissing && routes[1].RequiresMissing,"Navigation independent of ownership; material farms require check.");
    Assert(!routes[0].Code.Contains("BuyItem("),"Shop inspection must not silently spend currency.");
    Assert(routes[1].Code.Contains("No material farming started") && routes[1].Code.Contains("finally {core.SetOptions(false);}"),"Preflight unresolved merge materials and restore options.");
    File.WriteAllText(Path.Combine(routeRoot,"FullFarm.cs"),"/*\nname: Exact Sword\ndescription: Farms Exact Sword\n*/\npublic class ExactSword {public void ScriptMain(IScriptInterface bot) {}} ");
    Assert(GearRoutes.FullFarms(routeRoot,"Exact Sword").Count==1 && GearRoutes.FullFarms(routeRoot,"Sword").Count==0,"Only reuse exact-title full farming recipes.");
    var full=routeFinder.Find("Exact Sword",456).Single();
    Assert(full.Code.Contains("bot.Inventory.Contains(456)") && full.Code.Contains("base.ScriptMain(bot)"),"Generated full recipe wrapper retains item ID and calls recipe.");
    foreach(var route in routes) Assert(!Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(route.Code).GetDiagnostics().Any(d=>d.Severity==Microsoft.CodeAnalysis.DiagnosticSeverity.Error),"Generated shop and merge scripts parse.");
} finally {Directory.Delete(routeRoot,true);}
Assert(GearWeb.Clean(GearWeb.Field("<strong>Map Name:</strong> fourharbingers<br>","Map Name"))=="fourharbingers","Extract verified map field.");
Console.WriteLine("PASS: Exact shop and merge matching, generated ID checks, material preflight, no automatic purchases, wiki field parsing.");

var recipeRoot=Path.Combine(Path.GetTempPath(),"quest-recipe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(recipeRoot);
try {
    File.WriteAllText(Path.Combine(recipeRoot,"CoreRecipe.cs"),"public class CoreRecipe { public string OptionsStorage = \"recipe\"; public enum Reward { Exact=74491,Other=74492,None } object o=new Option<Reward>(\"Pick\",\"Pick\",\"Help\",Reward.None); public void Acquire(Reward r=Reward.None,bool once=false){Core.EnsureAccept(9000);} }");
    var recipe=GearQuestRecipes.Find(recipeRoot,9000,74491).Single();
    Assert(recipe.Method=="Acquire" && recipe.Option=="Pick","Discover reward enum, quest acceptance and option together.");
    Assert(GearQuestRecipes.Find(recipeRoot,9001,74491).Count==0 && GearQuestRecipes.Find(recipeRoot,9000,99999).Count==0,"Reject mismatched quest or reward ID.");
    string generated=GearQuestRecipes.Generate(recipe);
    Assert(generated.Contains("bot.Config.Set(\"Pick\", (CoreRecipe.Reward)74491)") && generated.Contains("Acquire((CoreRecipe.Reward)74491)"),"Force selected reward instead of saved None or another reward.");
    Assert(generated.Contains("GeneratedQuest-9000-74491") && generated.Contains("finally { core.SetOptions(false); }"),"Isolate generated options and restore game settings.");
    Assert(!Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(generated).GetDiagnostics().Any(d=>d.Severity==Microsoft.CodeAnalysis.DiagnosticSeverity.Error),"Generated quest reward script parses.");
} finally {Directory.Delete(recipeRoot,true);}
Console.WriteLine("PASS: Automatic quest-recipe discovery, exact reward selection, isolated options and cleanup.");

var requirement=new Skua.Core.Models.Items.ItemBase{ID=321,Name="Quest Fang",Temp=true,Quantity=5};
var autoQuest=ActiveQuestMaker.Generate(42,100,new[]{requirement},new[]{new GearDrop("forest","Wolf","Quest Fang",true,"test")},(_,_)=>null);
Assert(autoQuest.Contains("bot.TempInv.Contains(321,5)") && autoQuest.Contains("EnsureComplete(42,100)") && autoQuest.Contains("CanCompleteFullCheck(42)"),"Auto quest uses exact objective quantity, item check, and chosen reward.");
Assert(autoQuest.Contains("QuestHunt.Monster(bot,42,\"Wolf\",321,\"Quest Fang\",5,true)") && autoQuest.Contains("core.Join(\"forest\")"),"Generated hunts use adaptive QuestHunt instead of a fixed Farm combo.");
Assert(autoQuest.Contains("Quest step: hunt Quest Fang") && autoQuest.Contains("Quest step: turn-in"),"Generated scripts emit structured hunt and turn-in logs for the ledger.");
Assert(!autoQuest.Contains("EnsureAccept") && !autoQuest.Contains("RegisterQuests") && !autoQuest.Contains("while ("),"Never accept another quest, register loops, or repeat selected quest.");
Assert(autoQuest.Contains("Quest was abandoned") && autoQuest.Contains("finally {core.SetOptions(false);}"),"Abort abandoned quest and restore settings.");
try {ActiveQuestMaker.Generate(42,-1,new[]{requirement},Array.Empty<GearDrop>(),(_,_)=>null);throw new Exception("Accepted unknown objective.");} catch(InvalidOperationException ex) {Assert(ex.Message.Contains("Quest Fang"),"Explain exact unresolved objective.");}
var readyQuest=ActiveQuestMaker.Generate(42,-1,Array.Empty<Skua.Core.Models.Items.ItemBase>(),Array.Empty<GearDrop>(),(_,_)=>null);
Assert(!readyQuest.Contains("HuntMonster") && !readyQuest.Contains("Bank.Load"),"Ready quest only turns in, without bank load or farming.");
var bankedQuest=ActiveQuestMaker.Generate(42,-1,new[]{new Skua.Core.Models.Items.ItemBase{ID=456,Name="Banked Item",Quantity=1}},Array.Empty<GearDrop>(),(_,_)=>null,new HashSet<int>{456});
Assert(bankedQuest.Contains("core.Unbank(456)") && !bankedQuest.Contains("HuntMonster"),"Use banked materials without farming duplicates.");
Assert(bankedQuest.Contains("Quest step: unbank Banked Item") && readyQuest.Contains("Quest step: turn-in"),"Banked and ready quests still emit unbank/turn-in step logs.");
var ledger=JObject.FromObject(ActiveQuestMaker.Describe(42,"Hunt",new[]{requirement},(id,temp)=>id==321&&temp?2:0,false,false,false,false,true,Array.Empty<object>()));
Assert((int?)ledger["objectives"]![0]!["have"]==2 && (int?)ledger["objectives"]![0]!["need"]==5 && (string?)ledger["objectives"]![0]!["name"]=="Quest Fang","Ledger rows include live objective counts.");
var dailyBlocked=JObject.FromObject(ActiveQuestMaker.Describe(1,"Daily",Array.Empty<Skua.Core.Models.Items.ItemBase>(),(_,_)=>0,false,true,false,false,true,Array.Empty<object>()));
Assert((bool?)dailyBlocked["blocked"]==true && (bool?)dailyBlocked["dailyDone"]==true,"Daily-done quests are not offered as runnable.");
var memberReady=JObject.FromObject(ActiveQuestMaker.Describe(2,"Cape",Array.Empty<Skua.Core.Models.Items.ItemBase>(),(_,_)=>0,true,false,false,true,false,Array.Empty<object>()));
Assert((bool?)memberReady["blocked"]==false && (bool?)memberReady["member"]==true,"Ready member quests can still turn in.");
Console.WriteLine("PASS: Single accepted quest, exact objectives/reward, abandonment, ready-only turn-in, banked requirements, unknown-route rejection.");

Assert(!autoQuest.Contains("Bank.Load") && !autoQuest.Contains("core.SetOptions();"), "Temporary objectives never trigger bank preflight or CoreBots bank startup.");
Assert(bankedQuest.Contains("Bank.Load"), "Permanent objectives retain ownership verification.");
var magusRequirement=new Skua.Core.Models.Items.ItemBase{ID=79629,Name="Mage Construct Defeated",Temp=true,Quantity=1};
var magusRoutes=AcceptedQuestRoutes.Parse("class StoryFixture { void Run() { Story.KillQuest(9356, \"infernalarena\", \"Infernal Mage\"); Story.KillQuest(9357, \"wrong\", \"Wrong Monster\"); } }",9356,new[]{magusRequirement},"fixture");
Assert(magusRoutes.Count==1 && magusRoutes[0].Map=="infernalarena" && magusRoutes[0].Monster=="Infernal Mage", "Resolve only the exact accepted quest's literal KillQuest route.");
var magusScript=ActiveQuestMaker.Generate(9356,-1,new[]{magusRequirement},magusRoutes,(_,_)=>null);
Assert(magusScript.Contains("QuestHunt.Monster(bot,9356,\"Infernal Mage\",79629,\"Mage Construct Defeated\",1,true)") && magusScript.Contains("core.Join(\"infernalarena\")") && !magusScript.Contains("Bank.Load"), "Maligned Magus farms its temporary drop without the bank.");
Assert(AcceptedQuestRoutes.Parse("// Story.KillQuest(9356, \"wrong\", \"wrong\");",9356,new[]{magusRequirement},"fixture").Count==0, "Ignore commented routes.");
Console.WriteLine("PASS: Temporary quest bank bypass and exact Maligned Magus route generation.");

// Reduced factual fixtures based on aqwwiki.wikidot.com/valencia-s-quests and linked monster/map pages.
var wikiItems=new[]{new Skua.Core.Models.Items.ItemBase{ID=101976,Name="Polearm Handle",Quantity=1,Temp=true},new Skua.Core.Models.Items.ItemBase{ID=101977,Name="Polearm Blade",Quantity=1,Temp=true},new Skua.Core.Models.Items.ItemBase{ID=101978,Name="Elemental Spirit Core",Quantity=1,Temp=true}};
const string wikiQuest="The Scythe That Stops The Screams";
var wikiPages=new Dictionary<string,string>{
["/demnra-s-deception-polearm"]="<div id='page-title'>Demnra's Deception Polearm</div><p><strong>Price:</strong> N/A (Reward from '<a href='/valencia-s-quests#Hunt'>The Scythe That Stops the Screams</a>' quest)<br></p>",
["/valencia-s-quests"]="<div class='yui-navset'><ul class='yui-nav'><li>Wrong Quest</li><li>The Scythe That Stops The Screams</li></ul><div class='yui-content'><div><p>Unrelated quest</p></div><div><p><strong>Items Required</strong></p><ul><li>Polearm Handle x1 (Stacks up to 2)<ul><li>Dropped by <a href='/chaosweaver-warrior'>ChaosWeaver Warrior</a></li></ul></li><li>Polearm Blade x1 (Stacks up to 2)<ul><li>Dropped by <a href='/chaosweaver-cleric-monster'>ChaosWeaver Cleric (Monster)</a></li></ul></li><li>Elemental Spirit Core x1 (Stacks up to 2)<ul><li>Dropped by <a href='/breken-the-vile'>Breken the Vile (Level 18)</a></li></ul></li></ul></div></div></div>",
["/twilight-s-edge"]="<p><strong>Map Name:</strong> twilightedge<br></p>",
["/chaos-web"]="<p><strong>Map Name:</strong> chaosweb<br></p>",
["/greenguard-west"]="<p><strong>Map Name:</strong> greenguardwest<br></p>"};
string MonsterFixture(string name,string map,string item)=>"<div id='page-title'>"+name+"</div><div id='page-content'><p><strong>Location:</strong> <a href='"+map+"'>Map</a><br></p><ul><li>"+item+" (Dropped during the '<a href='/valencia-s-quests#Hunt'>"+wikiQuest+"</a>' quest)</li></ul></div>";
wikiPages["/chaosweaver-warrior"]=MonsterFixture("ChaosWeaver Warrior","/twilight-s-edge","Polearm Handle");
wikiPages["/chaosweaver-cleric-monster"]=MonsterFixture("ChaosWeaver Cleric (Monster)","/chaos-web","Polearm Blade");
wikiPages["/breken-the-vile"]="<div id='page-title'>Breken the Vile</div><div id='page-content'><div class='yui-content'><div><p><strong>Location:</strong><a href='/wrong-map'>Rare old location</a><br></p></div><div><p><strong>Location:</strong><a href='/greenguard-west'>Greenguard West</a><br></p><ul><li>Elemental Spirit Core (Dropped during the '<a href='/valencia-s-quests#Hunt'>"+wikiQuest+"</a>' quest)</li></ul></div></div></div>";
var wikiResolver=new QuestWikiResolver(path=>Task.FromResult(wikiPages[path]));
var wikiRoutes=await wikiResolver.Resolve(wikiQuest,new[]{"Demnra's Deception Polearm"},wikiItems);
Assert(wikiRoutes.Count==3 && wikiRoutes.Any(r=>r.Monster=="ChaosWeaver Cleric" && r.Map=="chaosweb") && wikiRoutes.Any(r=>r.Monster=="Breken the Vile" && r.Map=="greenguardwest"),"Discover reward quest and all three monster routes, selecting the correct Breken variant.");
var wikiScript=ActiveQuestMaker.Generate(10799,-1,wikiItems,wikiRoutes,(_,_)=>null);
Assert(wikiScript.Contains("101978,1") && wikiScript.Contains("twilightedge") && !wikiScript.Contains("Bank.Load"),"Generate complete multi-map quest with exact objective checks and no unnecessary bank load.");
File.WriteAllText("/tmp/qexec-wiki-generated-test.cs",wikiScript);
Assert((await wikiResolver.Resolve("Other Quest",new[]{"Demnra's Deception Polearm"},wikiItems)).Count==0,"Never mix another quest's objective routes.");
wikiItems[0].Quantity=2;
Assert((await wikiResolver.Resolve(wikiQuest,new[]{"Demnra's Deception Polearm"},wikiItems)).Count==2,"Reject a mismatched live quantity.");
wikiItems[0].Quantity=1;
wikiPages["/chaos-web"]="<p>Map name unavailable</p>";
var partialWiki=await wikiResolver.Resolve(wikiQuest,new[]{"Demnra's Deception Polearm"},wikiItems);
bool partialRejected=false;try {ActiveQuestMaker.Generate(10799,-1,wikiItems,partialWiki,(_,_)=>null);}catch(InvalidOperationException){partialRejected=true;}
Assert(partialRejected,"Partial wiki evidence never generates a complete-looking script.");
bool externalRejected=false;try{QuestWikiResolver.WikiPath("https://example.com/monster");}catch(InvalidOperationException){externalRejected=true;}
Assert(externalRejected,"Reject off-origin wiki links.");
Console.WriteLine("PASS: Automatic wiki quest discovery, multi-map routes, variant selection, quantity mismatch, partial evidence and origin restrictions.");

// Script-independent discovery fixtures, reduced from the linked AQW Wiki pages.
var decoder=new Skua.Core.Models.Items.ItemBase{ID=4733,Name="Dwakel Decoder",Temp=false,Quantity=1};
var mapOfLore=new Skua.Core.Models.Items.ItemBase{ID=30995,Name="Map of Lore",Temp=true,Quantity=1};
var lotus=new Skua.Core.Models.Items.ItemBase{ID=10332,Name="Lotus Flower",Temp=true,Quantity=4};
string QuestFixture(string name,string items)=>"<div class='yui-navset'><ul class='yui-nav'><li>"+name+"</li></ul><div class='yui-content'><div><p><strong>Items Required:</strong></p><ul>"+items+"</ul></div></div></div>";
var autonomousPages=new Dictionary<string,string>{
["/dwakel-decoder"]="<div id='page-title'>Dwakel Decoder</div><div id='page-content'><p><strong>Location:</strong><a href='/dwakel-crash-site'>Dwakel Crash Site</a><br><strong>Price:</strong>N/A (Click on the red dot to receive the decoder in Screen 3)<br></p><ul><li>Used in the '<a href='/aranx-s-quests#1'>Find the Map</a>' quest.</li></ul></div>",
["/aranx-s-quests"]=QuestFixture("Find the Map","<li><a href='/dwakel-decoder'>Dwakel Decoder</a> x1</li><li>Map of Lore x1<ul><li>Dropped by <a href='/infernal-knight-monster-1'>Infernal Knight (Monster) (1) (Level 20)</a></li></ul></li>"),
["/dwakel-crash-site"]="<p><strong>Map Name:</strong> crashsite<br></p>",
["/infernal-knight-monster-1"]="<div id='page-title'>Infernal Knight (Monster) (1)</div><div id='page-content'><p><strong>Location:</strong><a href='/celestial-realm'>Celestial Realm</a><br></p><ul><li>Map of Lore (Dropped during the '<a href='/aranx-s-quests'>Find the Map</a>' quest)</li></ul></div>",
["/celestial-realm"]="<p><strong>Map Name:</strong> celestialrealm<br></p>",
["/fuchsia-dye"]="<div id='page-title'>Fuchsia Dye</div><div id='page-content'><p><strong>Price:</strong>N/A<br></p><ul><li><a href='/beleen-s-quests'>Flowers for the Pink Gal</a></li><li><a href='/beleen-s-quests'>Dyeing for Gemstones</a></li></ul></div>",
["/beleen-s-quests"]=QuestFixture("Flowers for the Pink Gal","<li>Lotus Flower x4<ul><li>Dropped by <a href='/lotus-spider'>Lotus Spider (Level 33)</a></li></ul></li>"),
["/lotus-spider"]="<div id='page-title'>Lotus Spider</div><div id='page-content'><div class='yui-content'><div><p><strong>Location:</strong><a href='/wrong-map'>Wrong</a><br></p></div><div><p><strong>Location:</strong><a href='/cave-of-wanders'>Cave of Wanders</a><br></p><ul><li>Lotus Flower (Dropped during the '<a href='/beleen-s-quests#1'>Flowers for the Pink Gal</a>' quest</li></ul></div></div></div>",
["/cave-of-wanders"]="<p><strong>Map Name:</strong> wanders<br></p>"};
var independentResolver=new QuestWikiResolver(path=>Task.FromResult(autonomousPages[path]));
var decoderPlan=await independentResolver.ResolvePlan("Find the Map",Array.Empty<string>(),new[]{decoder,mapOfLore});
Assert(decoderPlan.Pickups.Count==1 && decoderPlan.Pickups[0].Map=="crashsite" && decoderPlan.Drops.Single().Monster=="Infernal Knight","No-reward quest resolves through required item backlink, separately from its monster objective.");
var decoderScript=ActiveQuestMaker.Generate(4498,-1,new[]{decoder,mapOfLore},decoderPlan.Drops,(_,_)=>null,null,decoderPlan.Pickups,false);
Assert(decoderScript.Contains("QuestMapPickup.Acquire(bot,4498,4733") && decoderScript.Contains("core.Join(\"crashsite\")") && !decoderScript.Contains("Bank.Load()") && !decoderScript.Contains("core.SetOptions();"),"Free pickups and monster drops proceed without requiring a bank load.");
File.WriteAllText("/tmp/qexec-decoder-generated-test.cs",decoderScript);
var lotusPlan=await independentResolver.ResolvePlan("Flowers for the Pink Gal",new[]{"Fuchsia Dye"},new[]{lotus});
Assert(lotusPlan.Drops.Single().Map=="wanders","Reward quest links outside Price are followed, preserving monster variant.");
var searchResolver=new QuestWikiResolver(path=>Task.FromResult(autonomousPages[path]),_=>Task.FromResult<IReadOnlyList<string>>(new[]{"/beleen-s-quests"}));
Assert((await searchResolver.ResolvePlan("Flowers for the Pink Gal",Array.Empty<string>(),new[]{lotus})).Drops.Count==1,"Unknown quest without any rewards discovers its own source through search.");
Assert(AcceptedQuestRoutes.Parse("class T { void Run(){ Story.KillQuest(4498,\"celestialrealm\",\"Infernal Knight\"); }}",4498,new[]{decoder,mapOfLore},"fixture").All(r=>r.Temporary),"Generic KillQuest must never assign monster sources to permanent pickups.");
Assert(GearOwnership.IsCompleteBankSnapshot("[{\"ItemID\":4733}]","1") && !GearOwnership.IsCompleteBankSnapshot("[]","1") && !GearOwnership.IsCompleteBankSnapshot(null,"0"),"Only complete bank snapshots are adopted; unavailable is never empty.");
var parsedPickups=Skua.Core.Scripts.QuestMapPickup.Parse("package { class Map { function other():void { if(root.world.isQuestInProgress(100)) root.world.getMapItem(200); } function decoder():void { root.world.getMapItem(106); } } }");
Assert(Skua.Core.Scripts.QuestMapPickup.Select(parsedPickups,4498)==106,"Discover unbound pickup without mixing another quest handler.");
Assert(Skua.Core.Scripts.QuestMapPickup.Select(Skua.Core.Scripts.QuestMapPickup.Parse("function frame1():void { this.button.mapItem = 800; this.button.questNum = 4498; }"),4498)==800,"Discover timeline pickup metadata by matching the same instance.");
bool ambiguousPickup=false;try {Skua.Core.Scripts.QuestMapPickup.Select(new[]{new Skua.Core.Scripts.QuestMapPickup.PickupCall(1,0),new Skua.Core.Scripts.QuestMapPickup.PickupCall(2,0)},4498);}catch(InvalidOperationException){ambiguousPickup=true;}
Assert(ambiguousPickup,"Never brute-force multiple unverified map buttons.");
Console.WriteLine("PASS: No-script quest discovery, no-reward quests, permanent map pickups, complete bank snapshots and dynamic pickup ID extraction.");

Assert(Skua.Core.Scripts.QuestMapPickup.Select(Skua.Core.Scripts.QuestMapPickup.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures","PickupFixture.as.txt"))),4498)==106,"Parse pickup ID from an actual compiled and decompiled Flash fixture.");
Assert(Skua.Core.Scripts.QuestMapPickup.Parse("function sample():void { trace(\"getMapItem(999)\"); /* getMapItem(998); */ getMapItem(106); }").Single().ID==106,"Ignore strings and comments when discovering executable pickup calls.");
autonomousPages["/collection-quests"]="<p><strong>Quest Location:</strong><a href='/dwakel-crash-site'>Crash Site</a><br></p>"+QuestFixture("Collect Clues","<li>Clue x2<ul><li>Click on the blue arrows around the map.</li></ul></li>");
var collectionResolver=new QuestWikiResolver(path=>Task.FromResult(autonomousPages[path]),_=>Task.FromResult<IReadOnlyList<string>>(new[]{"/collection-quests"}));
var collectionPlan=await collectionResolver.ResolvePlan("Collect Clues",Array.Empty<string>(),new[]{new Skua.Core.Models.Items.ItemBase{ID=123,Name="Clue",Temp=true,Quantity=2}});
Assert(collectionPlan.Pickups.Single().Map=="crashsite","Discover collection objectives from the quest's location when the objective has no item page.");
using(var canceledLookup=new CancellationTokenSource()) {
    canceledLookup.Cancel();bool canceled=false;
    try {await independentResolver.ResolvePlan("Find the Map",Array.Empty<string>(),new[]{decoder},null,canceledLookup.Token);}catch(OperationCanceledException){canceled=true;}
    Assert(canceled,"Canceled planning stops before reading sources or writing a script.");
}
Console.WriteLine("PASS: Compiled map fixture, literal filtering, collection location discovery and planning cancellation.");

// Permanent material recovery: facts from Star Scrap Metal, Troblor and Dread Space wiki pages.
var scrap=new Skua.Core.Models.Items.ItemBase{ID=30018,Name="Star Scrap Metal",Temp=false,Quantity=10};
var materialPages=new Dictionary<string,string>{
["/star-scrap-metal"]="<div id='page-title'>Star Scrap Metal</div><div id='page-content'><p><strong>Location:</strong><a href='/dread-space-location'>Dread Space (Location)</a><br><strong>Price:</strong>N/A</p><ul><li>Dropped by <a href='/troblor'>Troblor</a></li></ul><ul><li>Used in the '<a href='/lezard-man-s-quests'>Blinded by the Black Light</a>' quest.</li></ul></div>",
["/lezard-man-s-quests"]=QuestFixture("Blinded by the Black Light","<li><a href='/star-scrap-metal'>Star Scrap Metal</a> x10</li>"),
["/troblor"]="<div id='page-title'>Troblor</div><div id='page-content'><p><strong>Location:</strong><a href='/dread-space-location'>Dread Space (Location)</a><br></p><p><strong>Temporary Items Dropped:</strong></p><ul><li>Unrelated Temporary Drop</li></ul><p><strong>Items Dropped:</strong></p><ul><li><a href='/star-scrap-metal'>Star Scrap Metal</a></li></ul></div>",
["/dread-space-location"]="<p><strong>Map Name:</strong> dreadspace<br></p>"};
int transientAttempts=0;var recoveryMessages=new List<string>();
var materialResolver=new QuestWikiResolver(path=>{if(path=="/star-scrap-metal" && transientAttempts++==0) throw new HttpRequestException("temporary",null,System.Net.HttpStatusCode.ServiceUnavailable);return Task.FromResult(materialPages[path]);});
var materialPlan=await materialResolver.ResolvePlan("Blinded by the Black Light",Array.Empty<string>(),new[]{scrap},recoveryMessages.Add);
Assert(materialPlan.Drops.Single() is {Map:"dreadspace",Monster:"Troblor",Temporary:false} && recoveryMessages.Any(m=>m.StartsWith("Retrying")),"Recover transient lookup and trace a permanent material's monster source without a quest-only drop backlink.");
var scrapScript=ActiveQuestMaker.Generate(9679,-1,new[]{scrap},materialPlan.Drops,(_,_)=>null,null,null,false);
Assert(scrapScript.Contains("QuestHunt.Monster(bot,9679,\"Troblor\",30018,\"Star Scrap Metal\",10,false)") && scrapScript.Contains("bot.Inventory.Contains(30018,10)"),"Farm permanent material with the exact live inventory ID and quantity.");
File.WriteAllText("/tmp/qexec-scrap-generated-test.cs",scrapScript);
var noQuestPageResolver=new QuestWikiResolver(path=>Task.FromResult(materialPages[path]));
Assert((await noQuestPageResolver.ResolvePlan("A Different Quest Using Scrap",Array.Empty<string>(),new[]{scrap})).Drops.Count==1,"Recover permanent material sources independently when the quest page cannot be found.");
var unavailableSearch=new QuestWikiResolver(path=>Task.FromResult(materialPages[path]),_=>throw new HttpRequestException("search unavailable"));
Assert((await unavailableSearch.ResolvePlan("A Different Quest Using Scrap",Array.Empty<string>(),new[]{scrap})).Drops.Count==1,"Search provider failure falls back to direct material discovery.");
materialPages["/troblor"]=materialPages["/troblor"].Replace("<strong>Items Dropped:</strong>","<strong>Unrelated Links:</strong>");
Assert((await noQuestPageResolver.ResolvePlan("Blinded by the Black Light",Array.Empty<string>(),new[]{scrap})).Drops.Count==0,"An unrelated item link is not proof of a monster drop.");
materialPages["/troblor"]=materialPages["/troblor"].Replace("<strong>Unrelated Links:</strong>","<strong>Items Dropped:</strong>").Replace("href='/star-scrap-metal'","href='/different-item'");
Assert((await noQuestPageResolver.ResolvePlan("Blinded by the Black Light",Array.Empty<string>(),new[]{scrap})).Drops.Count==0,"Require reciprocal exact item link, not just matching visible text.");
Console.WriteLine("PASS: Permanent material recovery, transient retries, absent quest-page fallback and reciprocal drop evidence.");

// Reduced fixtures from Twilly's Quests and Kuro: plural location list with a seasonal alternative.
var chest=new Skua.Core.Models.Items.ItemBase{ID=2570,Name="Muck Covered Chest",Temp=true,Quantity=1};
var chestPages=new Dictionary<string,string>{
["/twilly-s-quests"]=QuestFixture("Chest Thumping","<li>Muck Covered Chest x1<ul><li>Dropped by <a href='/kuro'>Kuro</a></li></ul></li>"),
["/kuro"]="<div id='page-title'>Kuro</div><div id='page-content'><p><strong>Locations:</strong></p>\n<ul><li><a href='/pollution'>Pollution</a><img src='seasonalsmall.png'></li><li><a href='/river'>River</a></li></ul><p><strong>Temporary Items Dropped:</strong></p><ul><li>Muck Covered Chest (Dropped during the '<a href='/twilly-s-quests'>Chest Thumping</a>' quest)</li></ul><p><strong>Notes:</strong></p><ul><li><a href='/unrelated'>Unrelated</a></li></ul></div>",
["/river"]="<p><strong>Map Name:</strong> river<br></p>"};
var chestResolver=new QuestWikiResolver(path=>Task.FromResult(chestPages[path]),_=>Task.FromResult<IReadOnlyList<string>>(new[]{"/twilly-s-quests"}));
var chestPlan=await chestResolver.ResolvePlan("Chest Thumping",Array.Empty<string>(),new[]{chest});
Assert(chestPlan.Drops.Single() is {Map:"river",Monster:"Kuro",Temporary:true},"Plural sibling location lists resolve Kuro via its unrestricted River map.");
var chestScript=ActiveQuestMaker.Generate(446,-1,new[]{chest},chestPlan.Drops,(_,_)=>null);
Assert(chestScript.Contains("QuestHunt.Monster(bot,446,\"Kuro\",2570,\"Muck Covered Chest\",1,true)") && chestScript.Contains("bot.TempInv.Contains(2570,1)") && chestScript.Contains("EnsureComplete(446,-1)"),"Chest script farms the exact objective and turns in only quest 446.");
File.WriteAllText("/tmp/qexec-chest-generated-test.cs",chestScript);
var kuroFixture=chestPages["/kuro"];
chestPages["/kuro"]=kuroFixture.Replace("href='/twilly-s-quests'","href='/other-quests'");
Assert((await chestResolver.ResolvePlan("Chest Thumping",Array.Empty<string>(),new[]{chest})).Drops.Count==0,"Plural map support still requires the exact quest backlink.");
chestPages["/kuro"]=kuroFixture.Replace("<p><strong>Locations:</strong></p>\n<ul><li><a href='/pollution'>Pollution</a><img src='seasonalsmall.png'></li><li><a href='/river'>River</a></li></ul>","<p><strong>Locations:</strong><a href='/river'>River</a><br></p>");
Assert((await chestResolver.ResolvePlan("Chest Thumping",Array.Empty<string>(),new[]{chest})).Drops.Single().Map=="river","Inline plural locations remain supported.");
chestPages["/kuro"]=kuroFixture.Replace("<strong>Locations:</strong>","<strong>Notes:</strong>");
Assert((await chestResolver.ResolvePlan("Chest Thumping",Array.Empty<string>(),new[]{chest})).Drops.Count==0,"Never interpret an unrelated sibling list as locations.");
Console.WriteLine("PASS: Chest Thumping plural location lists, restricted alternatives and exact generated objective.");

await AreaTests.Run();

sealed class MessageWriter : StringWriter
{
    public BlockingCollection<string> Messages { get; } = new();
    public override void WriteLine(string? value) => Messages.Add(value!);
}
