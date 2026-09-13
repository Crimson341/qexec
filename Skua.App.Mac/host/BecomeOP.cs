using Microsoft.CodeAnalysis.CSharp;

namespace Skua.Mac;
public static class BecomeOP
{
    public static string[] Consumables(string name)=>name.Trim().ToLowerInvariant() switch {
        "void highlord" or "void highlord (ioda)" or "chaos avenger" or "archpaladin" => ["Fate Tonic","Potent Battle Elixir"],
        "legion revenant" or "archmage" or "lightcaster" or "scarlet sorceress" => ["Sage Tonic","Potent Malevolence Elixir"],
        _=>[]
    };
    public static string Generate(string className) {
        if(string.IsNullOrWhiteSpace(className))throw new InvalidOperationException("Equip a class first.");
        string Q(string s)=>SymbolDisplay.FormatLiteral(s,true);
        string pots=string.Join(",",Consumables(className).Select(Q));
        return "//cs_include Scripts/CoreBots.cs\n//cs_include Scripts/CoreFarms.cs\n//cs_include Scripts/CoreAdvanced.cs\nusing System; using System.Linq; using Skua.Core.Interfaces;\npublic class BecomeOPSetup { public void ScriptMain(IScriptInterface bot) { var core=CoreBots.Instance;var adv=new CoreAdvanced(); string expected="+Q(className)+"; string map=bot.Map.Name,cell=bot.Player.Cell,pad=bot.Player.Pad;\nvoid Check(){if(bot.ShouldExit)throw new OperationCanceledException();if(!bot.Player.LoggedIn || bot.Player.CurrentClass?.Name!=expected)throw new InvalidOperationException(\"Class changed or disconnected. Run Become OP again.\");}\nCheck();if(core.CBOBool(\"DisableAutoEnhance\",out bool disabled) && disabled)throw new InvalidOperationException(\"Enable AutoEnhance in CoreBots options before applying this setup.\");\ncore.SetOptions(disableClassSwap:true);try { bot.Log(\"Become OP: applying the installed class enhancement profile for \"+expected);adv.SmartEnhance(expected);Check();core.Join(map,cell,pad);Check();if(!bot.Bank.Loaded)bot.Bank.Load();int missing=0;\nforeach(string name in new string[]{"+pots+"}){Check();core.Unbank(name);if(!bot.Inventory.Contains(name)){bot.Log(\"Missing setup consumable: \"+name+\". Farm or buy it, then run Become OP again.\");missing++;continue;}bot.Inventory.EquipUsableItem(name);System.Threading.Thread.Sleep(500);Check();if(!bot.Inventory.IsEquipped(name)){missing++;bot.Log(\"Could not equip \"+name);continue;}int before=bot.Inventory.GetQuantity(name);core.UsePotion();for(int n=0;n<30 && bot.Inventory.GetQuantity(name)>=before;n++){Check();System.Threading.Thread.Sleep(100);}if(bot.Inventory.GetQuantity(name)>=before){missing++;bot.Log(\"Could not verify activation of \"+name);}else bot.Log(\"Activated \"+name);}\n"+
        (Consumables(className).Length==0?"bot.Log(\"No verified consumable preset for this class yet; only enhancements were requested.\");":"bot.Log(missing==0?\"Class setup applied; temporary buffs will expire.\":\"Enhancement routine finished; some consumables remain missing or inactive.\");")+
        "}finally{core.SetOptions(false);} } }";
    }
}
