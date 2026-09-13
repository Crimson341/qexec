using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Skua.Mac;

public record GearQuestRecipe(string File,string Class,string Method,string Enum,string Option,int Quest,int Item);
public static class GearQuestRecipes
{
    public static List<GearQuestRecipe> Find(string root,int questId,int itemId)
    {
        var results=new List<GearQuestRecipe>();
        foreach(var file in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)) {
            if((file.Contains(Path.DirectorySeparatorChar+"Generated-Gear"+Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar+"Generated-Quests"+Path.DirectorySeparatorChar)) || new FileInfo(file).Length>2_000_000) continue;
            string text=File.ReadAllText(file);
            if(!text.Contains(itemId.ToString()) || !text.Contains(questId.ToString())) continue;
            var tree=CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach(var cls in tree.DescendantNodes().OfType<ClassDeclarationSyntax>()) {
                if(cls.TypeParameterList!=null || cls.Modifiers.Any(m=>m.IsKind(SyntaxKind.SealedKeyword) || m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.AbstractKeyword)) || cls.Members.OfType<ConstructorDeclarationSyntax>().Any()) continue;
                if(!cls.Members.OfType<FieldDeclarationSyntax>().Any(f=>f.Modifiers.Any(m=>m.IsKind(SyntaxKind.PublicKeyword)) && f.Declaration.Variables.Any(v=>v.Identifier.ValueText=="OptionsStorage"))) continue;
                foreach(var en in cls.Members.OfType<EnumDeclarationSyntax>()) {
                    if(!en.Members.Any(m=>m.EqualsValue?.Value is LiteralExpressionSyntax l && l.Token.ValueText==itemId.ToString())) continue;
                    var option=cls.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault(o=>o.Type is GenericNameSyntax g && g.Identifier.ValueText=="Option" && g.TypeArgumentList.Arguments.Count==1 && g.TypeArgumentList.Arguments[0].ToString()==en.Identifier.ValueText);
                    if(option?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax key || !key.IsKind(SyntaxKind.StringLiteralExpression)) continue;
                    foreach(var method in cls.Members.OfType<MethodDeclarationSyntax>()) {
                        if(!method.Modifiers.Any(m=>m.IsKind(SyntaxKind.PublicKeyword)) || method.Modifiers.Any(m=>m.IsKind(SyntaxKind.StaticKeyword))) continue;
                        var args=method.ParameterList.Parameters;
                        if(args.Count==0 || args[0].Type?.ToString()!=en.Identifier.ValueText || args.Skip(1).Any(p=>p.Default==null)) continue;
                        bool accepts=method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call=>call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText is "EnsureAccept" or "Accept" && call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax id && id.Token.ValueText==questId.ToString());
                        if(!accepts) continue;
                        results.Add(new(Path.GetRelativePath(root,file).Replace('\\','/'),cls.Identifier.ValueText,method.Identifier.ValueText,en.Identifier.ValueText,key.Token.ValueText,questId,itemId));
                    }
                }
            }
        }
        return results.Distinct().Take(3).ToList();
    }
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
