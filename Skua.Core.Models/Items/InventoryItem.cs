using Newtonsoft.Json;
using Skua.Core.Models.Converters;

namespace Skua.Core.Models.Items;

public class InventoryItem : ItemBase
{
    /// <summary>
    /// The character (instance) ID of this item.
    /// </summary>
    [JsonProperty("CharItemID")]
    public int CharItemID { get; set; }

    /// <summary>
    /// Indicates if the item is equipped.
    /// </summary>
    [JsonProperty("bEquip")]
    [JsonConverter(typeof(StringBoolConverter))]
    public bool Equipped { get; set; }

    /// <summary>
    /// The level of the item.
    /// </summary>
    [JsonProperty("iLvl")]
    public int Level { get; set; }

    /// <summary>
    /// The enhancement level of the item.
    /// </summary>
    [JsonProperty("EnhLvl")]
    public virtual int EnhancementLevel { get; set; }

    /// <summary>
    /// The enhancement pattern ID of the item, this identifies the current enhancement type of the item.
    /// <br> 1: Adventurer </br>
    /// <br> 2: Fighter </br>
    /// <br> 3: Thief </br>
    /// <br> 4: Armsman </br>
    /// <br> 5: Hybrid </br>
    /// <br> 6: Wizard </br>
    /// <br> 7: Healer </br>
    /// <br> 8: Spellbreaker </br>
    /// <br> 9: Lucky </br>
    /// <br> 23: Depths </br>
    /// <br> 10: Forge </br>
    /// <br> 11: Absolution </br>
    /// <br> 12: Avarice </br>
    /// <br> 24: Vainglory </br>
    /// <br> 25: Vim </br>
    /// <br> 26: Examen </br>
    /// <br> 27: Pneuma </br>
    /// <br> 28: Anima </br>
    /// <br> 29: Penitence </br>
    /// <br> 30: Lament </br>
    /// <br> 32: Hearty </br>
    /// </summary>
    [JsonProperty("InvEnhPatternID")]
    public override int EnhancementPatternID { get; set; }

    /// <summary>
    /// The ProcID of the item. This identifies the current special enhancement (E.G. Forge and AWE enhancements).
    /// Note: Inventory items use InvEnhProcID instead of ProcID.
    /// </summary>
    [JsonProperty("InvEnhProcID")]
    public override int ProcID { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is InventoryItem item && item.ID == ID && item.CharItemID == CharItemID;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ID, CharItemID);
    }
}