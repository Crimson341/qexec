using Newtonsoft.Json.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;

namespace Skua.Mac;

public sealed class GearOwnership(IScriptInterface bot, IFlashUtil? flash = null)
{
    public static string Locate(int id, string name, IEnumerable<InventoryItem> inventory, IEnumerable<InventoryItem> bank, bool bankLoaded)
    {
        bool Matches(InventoryItem item) => id > 0 ? item.ID == id : !string.IsNullOrWhiteSpace(name) && string.Equals(item.Name,name,StringComparison.OrdinalIgnoreCase);
        if (inventory.Any(Matches)) return "Inventory";
        if (!bankLoaded) return "Unknown";
        if (bank.Any(Matches)) return "Bank";
        return "Missing";
    }
    public async Task<bool> LoadBank()
    {
        if (!bot.Player.LoggedIn) return false;
        if (bot.Bank.Loaded) return true;
        bot.Bank.Load(false);
        for (int i=0; i<40 && !bot.Bank.Loaded; i++) await Task.Delay(200);
        return bot.Player.LoggedIn && bot.Bank.Loaded;
    }
    public static List<InventoryItem> ParseOwnershipItems(string json)
    {
        if (JToken.Parse(json) is not JArray array) throw new InvalidDataException("Item list has not loaded.");
        return array.OfType<JObject>().Select(item => new InventoryItem {
            ID=(int?)item["ItemID"] ?? 0, Name=(string?)item["sName"] ?? "",
            Quantity=(int?)item["iQty"] ?? 1,
            Equipped=(string?)item["bEquip"] is "1" or "true"
        }).ToList();
    }
    public List<InventoryItem> ReadInventory() => flash == null ? bot.Inventory.Items : ParseOwnershipItems(flash.GetGameObject("world.myAvatar.items") ?? "null");
    public List<InventoryItem> ReadBank() => flash == null ? bot.Bank.Items : ParseOwnershipItems(flash.GetGameObject("world.bankinfo.items") ?? "null");
    public async Task Annotate(JObject data)
    {
        if (data["items"] is not JArray items) return;
        List<InventoryItem> inventory;
        try { inventory = ReadInventory(); }
        catch (Exception ex) {
            data["ownershipNote"] = "Inventory read failed: " + ex.GetBaseException().Message;
            foreach (var item in items.OfType<JObject>()) item["ownership"] = "Unknown";
            return;
        }
        bool loaded = false; var bank = new List<InventoryItem>();
        try {
            loaded = await LoadBank();
            if (loaded) bank = ReadBank();
            else data["ownershipNote"] = "Inventory checked; bank response not received. You can find sources, but farming requires a successful bank check.";
        } catch (Exception ex) {
            loaded = false;
            data["ownershipNote"] = "Inventory checked; bank read failed: " + ex.GetBaseException().Message;
        }
        foreach (var item in items.OfType<JObject>()) {
            int id=(int?)item["id"] ?? 0;
            item["ownership"] = Locate(id,(string?)item["name"] ?? "",inventory,bank,loaded);
            var owned = inventory.Concat(bank).FirstOrDefault(i=>id>0 && i.ID==id && !string.IsNullOrWhiteSpace(i.Name));
            if (owned != null) { item["name"]=owned.Name; item["identitySource"]="Owned item ID"; }
        }
    }
    public async Task RequireMissing(string name, int itemId = 0)
    {
        bool loaded = await LoadBank();
        string location = Locate(itemId,name,ReadInventory(),loaded ? ReadBank() : new List<InventoryItem>(),loaded);
        if (location == "Unknown") throw new InvalidOperationException("Bank contents could not be checked. Retry before farming.");
        if (location != "Missing") throw new InvalidOperationException("Already owned in " + location.ToLowerInvariant() + ": " + name + ". No farm started.");
    }
}
