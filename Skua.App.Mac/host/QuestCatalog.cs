using Newtonsoft.Json.Linq;
using Skua.Core.Models.Items;

namespace Skua.Mac;

public sealed class CatalogItem
{
    public int Id;
    public string Name = "", Category = "", Description = "";
    public bool Temporary;
    public HashSet<string> NeededFor = new();
    public HashSet<string> RewardsFrom = new();
    public HashSet<string> DropsFrom = new();
    public string SearchText = "";
}

// Local evidence index, not a claim that historical rewards are still obtainable.
public sealed class QuestCatalog(string questsFile, GearFinder finder)
{
    private readonly Lazy<(List<CatalogItem> Items, HashSet<string> Ambiguous)> index = new(() => {
        var items = Build(questsFile, finder.Drops);
        foreach (var item in items)
            item.SearchText = item.Name+" "+item.Category+" "+item.Description+" "+string.Join(" ",item.NeededFor);
        var ambiguous = items.GroupBy(i=>i.Name,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (items, ambiguous);
    });
    public static List<CatalogItem> Build(string questsFile, IEnumerable<GearDrop> drops)
    {
        var items = new Dictionary<int,CatalogItem>();
        foreach (var quest in QuestDataCache.Load(questsFile).OfType<JObject>())
        foreach (string field in new[]{"Rewards","SimpleRewards","Requirements","AcceptRequirements"})
        foreach (var raw in (quest[field] as JArray ?? new()).OfType<JObject>())
        {
            int id = (int?)raw["ItemID"] ?? 0; string name = (string?)raw["sName"] ?? "";
            if (id <= 0 || string.IsNullOrWhiteSpace(name)) continue;
            if (!items.TryGetValue(id,out var item)) {
                item = new CatalogItem { Id=id,Name=name,Category=(string?)raw["Category"] ?? (string?)raw["sType"] ?? "Item",Description=(string?)raw["sDesc"] ?? "",Temporary=(string?)raw["bTemp"] == "1" };
                items[id] = item;
            }
            string evidence = ((string?)quest["Name"] ?? "Quest") + " (#" + quest["ID"] + ")";
            if (field is "Rewards" or "SimpleRewards") item.RewardsFrom.Add(evidence); else item.NeededFor.Add(evidence);
        }
        var all = items.Values.ToList();
        var byName = all.GroupBy(i => i.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,g => g.ToList(),StringComparer.OrdinalIgnoreCase);
        foreach (var drop in drops.Where(d => !d.Temporary)) {
            if (!byName.TryGetValue(drop.Item,out var matches)) {
                var item = new CatalogItem { Name=drop.Item, Category="Script drop" }; all.Add(item); matches=[item]; byName[drop.Item]=matches;
            }
            // Names shared by multiple IDs cannot establish a unique item source.
            if (matches.Count == 1) matches[0].DropsFrom.Add(drop.Monster+" · /join "+drop.Map);
        }
        return all;
    }
    public object Query(string request, IEnumerable<InventoryItem> inventory, IEnumerable<InventoryItem> bank, bool bankLoaded)
    {
        JObject options = string.IsNullOrEmpty(request) ? new() : JObject.Parse(request);
        string search = ((string?)options["search"] ?? "").Trim(); string filter = (string?)options["filter"] ?? "all";
        int page = Math.Max(0,(int?)options["page"] ?? 0);
        var inv = inventory.ToList(); var stored = bank.ToList();
        var invIds = inv.Select(i=>i.ID).ToHashSet(); var bankIds=stored.Select(i=>i.ID).ToHashSet();
        var invNames=inv.Select(i=>i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase); var bankNames=stored.Select(i=>i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Location(CatalogItem i) => (i.Id>0 ? invIds.Contains(i.Id) : invNames.Contains(i.Name)) ? "Inventory" : !bankLoaded ? "Unknown" : (i.Id>0 ? bankIds.Contains(i.Id) : bankNames.Contains(i.Name)) ? "Bank" : "Missing";
        var catalog = index.Value;
        var results = catalog.Items.Where(i => (search.Length == 0 || i.SearchText.Contains(search,StringComparison.OrdinalIgnoreCase)) && (filter switch {
            "missing" => !i.Temporary && Location(i)=="Missing",
            "owned" => Location(i) is "Inventory" or "Bank",
            "materials" => i.NeededFor.Count>0,
            "drops" => i.DropsFrom.Count>0,
            "gear" => !i.Temporary && i.Category is not "Item" and not "Script drop",
            _ => !i.Temporary
        })).OrderByDescending(i=>i.NeededFor.Count).ThenBy(i=>i.Name,StringComparer.OrdinalIgnoreCase).ToList();
        page=Math.Min(page,Math.Max(0,(results.Count-1)/50));
        return new { type="quest-catalog", total=catalog.Items.Count, matches=results.Count,page, bankLoaded,
            items=results.Skip(page*50).Take(50).Select(i=>new { id=i.Id,name=i.Name,category=i.Category,ownership=Location(i),temporary=i.Temporary,
                detail=(i.NeededFor.Count>0 ? "Required by "+i.NeededFor.Count+" quests: "+string.Join(", ",i.NeededFor.Take(3))+". " : "")+
                    (i.RewardsFrom.Count>0 ? "Reward: "+string.Join(", ",i.RewardsFrom.Take(2))+". " : "")+
                    (i.DropsFrom.Count>0 ? "Drop evidence: "+string.Join(", ",i.DropsFrom.Take(2))+". " : ""),
                description=i.Description == "x" ? "" : i.Description,
                canFind=!i.Temporary && !catalog.Ambiguous.Contains(i.Name) && Location(i)=="Missing", routeNote=catalog.Ambiguous.Contains(i.Name) ? "Ambiguous item name" : i.Temporary ? "Quest material" : Location(i), availability="Rarity / current availability unverified" }).ToArray() };
    }
}
