using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Skua.Mac;

public record GearQuestRecipe(string File,string Class,string Method,string Enum,string Option,int Quest,int Item);
public static class GearQuestRecipes
{
    public static List<GearQuestRecipe> Find(string root,int questId,int itemId)
        => ScriptEvidence.Scan(root).SelectMany(script => script.Recipes)
            .Where(recipe => recipe.Quest==questId && recipe.Item==itemId)
            .Distinct().Take(3).ToList();
    public static string Generate(GearQuestRecipe recipe)
    {
        string Q(string s)=>SymbolDisplay.FormatLiteral(s,true);
        return "// Generated for one quest reward; reuses the installed quest/prerequisite implementation.\n//cs_include Scripts/"+recipe.File+"\nusing System;\nusing Skua.Core.Interfaces;\npublic class GeneratedQuestReward : "+recipe.Class+" {\n"+
            "public GeneratedQuestReward() { OptionsStorage = "+Q("GeneratedQuest-"+recipe.Quest+"-"+recipe.Item)+"; }\n"+
            "public new void ScriptMain(IScriptInterface bot) { var core=CoreBots.Instance;\n"+
            "if(bot.Inventory.Contains("+recipe.Item+")) return; if(!bot.Bank.Loaded) bot.Bank.Load(); if(!bot.Bank.Loaded) throw new InvalidOperationException(\"Bank verification failed.\"); if(bot.Bank.Contains("+recipe.Item+")) return;\n"+
            "core.SetOptions(); try { if(bot.Config==null) throw new InvalidOperationException(\"Quest options unavailable.\");\n"+
            "bot.Config.Set("+Q(recipe.Option)+", ("+recipe.Class+"."+recipe.Enum+")"+recipe.Item+");\n"+
            "bot.Log("+Q("Farming quest #"+recipe.Quest+" for item #"+recipe.Item+", including the installed prerequisite and shop steps.")+");\n"+
            recipe.Method+"(("+recipe.Class+"."+recipe.Enum+")"+recipe.Item+");\n"+
            "if(bot.ShouldExit) return; if(!bot.Inventory.Contains("+recipe.Item+") && !bot.Bank.Contains("+recipe.Item+")) throw new InvalidOperationException(\"Selected reward was not acquired. Check quest access, membership, purchases, and the activity log.\"); bot.Log(\"Selected quest reward acquired.\");\n"+
            "} finally { core.SetOptions(false); } } }";
    }
}
