using System;
using System.Threading;
using Skua.Core.Interfaces;
using Newtonsoft.Json.Linq;
public class BankDiagnostic
{
    public void ScriptMain(IScriptInterface bot)
    {
        bot.Log("Bank diagnostic: logged in="+bot.Player.LoggedIn+", loaded="+bot.Bank.Loaded);
        try { bot.Log("Inventory typed count="+bot.Inventory.Items.Count); } catch(Exception ex) { bot.Log("Inventory error="+ex.Message); }
        try {
            bot.Bank.Load(false);
            for(int i=0;i<40 && !bot.Bank.Loaded;i++) Thread.Sleep(200);
            bot.Log("Bank diagnostic: loaded after request="+bot.Bank.Loaded);
            var raw=JToken.Parse(bot.Flash.GetGameObject("world.bankinfo.items") ?? "null");
            bot.Log("Bank raw type="+raw.Type+", count="+(raw is JArray a ? a.Count : -1));
            bot.Log("Bank typed count="+bot.Bank.Items.Count);
        } catch(Exception ex) { bot.Log("Bank diagnostic error="+ex.Message); }
    }
}
