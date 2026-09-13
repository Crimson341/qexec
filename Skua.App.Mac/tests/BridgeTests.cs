using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
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
Console.WriteLine("PASS: Adaptive combat health thresholds, recovery hysteresis, encounter estimate, and class profile selection.");

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
    var catalogScripts = Path.Combine(Path.GetTempPath(),"catalog-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(catalogScripts);
    try {
        var catalog = new QuestCatalog(catalogFixture,new GearFinder(catalogScripts));
        var storedSword = new[]{new Skua.Core.Models.Items.InventoryItem{ID=100,Name="Sword"}};
        var missing = JObject.FromObject(catalog.Query("{\"filter\":\"missing\"}",emptyItems,storedSword,true));
        Assert((int?)missing["matches"]==0,"Banked reward and temporary material excluded from missing gear.");
        var unknown = JObject.FromObject(catalog.Query("{}",emptyItems,emptyItems,false));
        Assert((bool?)unknown["items"]![0]!["canFind"]==false,"Unknown bank blocks catalog farms.");
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
Assert(autoQuest.Contains("bot.TempInv.Contains(321,5)") && autoQuest.Contains("EnsureComplete(42,100)"),"Auto quest uses exact objective quantity and chosen reward.");
Assert(!autoQuest.Contains("EnsureAccept") && !autoQuest.Contains("RegisterQuests") && !autoQuest.Contains("while ("),"Never accept another quest, register loops, or repeat selected quest.");
Assert(autoQuest.Contains("Quest was abandoned") && autoQuest.Contains("finally {core.SetOptions(false);}"),"Abort abandoned quest and restore settings.");
try {ActiveQuestMaker.Generate(42,-1,new[]{requirement},Array.Empty<GearDrop>(),(_,_)=>null);throw new Exception("Accepted unknown objective.");} catch(InvalidOperationException ex) {Assert(ex.Message.Contains("Quest Fang"),"Explain exact unresolved objective.");}
var readyQuest=ActiveQuestMaker.Generate(42,-1,Array.Empty<Skua.Core.Models.Items.ItemBase>(),Array.Empty<GearDrop>(),(_,_)=>null);
Assert(!readyQuest.Contains("HuntMonster") && !readyQuest.Contains("Bank.Load"),"Ready quest only turns in, without bank load or farming.");
var bankedQuest=ActiveQuestMaker.Generate(42,-1,new[]{new Skua.Core.Models.Items.ItemBase{ID=456,Name="Banked Item",Quantity=1}},Array.Empty<GearDrop>(),(_,_)=>null,new HashSet<int>{456});
Assert(bankedQuest.Contains("core.Unbank(456)") && !bankedQuest.Contains("HuntMonster"),"Use banked materials without farming duplicates.");
Console.WriteLine("PASS: Single accepted quest, exact objectives/reward, abandonment, ready-only turn-in, banked requirements, unknown-route rejection.");

sealed class MessageWriter : StringWriter
{
    public BlockingCollection<string> Messages { get; } = new();
    public override void WriteLine(string? value) => Messages.Add(value!);
}
