using Skua.Core.Interfaces;
using System;
using System.IO;
using Newtonsoft.Json;
public class CombatDiagnostic
{
    public void ScriptMain(IScriptInterface bot)
    {
        using var output = new StreamWriter("/tmp/skua-combat-diagnostic.txt");
        void Read(string name, Func<object> read) { try { var line = name + " = " + JsonConvert.SerializeObject(read()); output.WriteLine(line); bot.Log(line); } catch(Exception ex) { output.WriteLine(name + " ERROR " + ex); } }
        Read("Player", () => new { bot.Player.LoggedIn, bot.Player.Loaded, bot.Player.Alive, bot.Player.Cell, bot.Player.CurrentClassRank });
        Read("Monsters", () => bot.Monsters.CurrentMonsters);
        Read("Target", () => bot.Player.Target);
        Read("Skills", () => bot.Player.Skills);
        for(int i=0;i<5;i++){int skill=i; Read("CanUse"+i, () => bot.Skills.CanUseSkill(skill));}
        Read("Combat", () => new { bot.Combat.StopAttacking, bot.Skills.TimerRunning });
    }
}
