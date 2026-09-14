using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace Skua.Mac;

internal sealed class ParsedScript
{
    public string RelativePath = "";
    public string DisplayName = "";
    public List<GearDrop> Drops = [];
    public List<ShopInvocation> Shops = [];
    public List<(string Class, string Title)> FullFarms = [];
    public List<GearQuestRecipe> Recipes = [];
    public List<(string OptionId, string OptionName)> BoolOptions = [];
}

internal readonly record struct ShopInvocation(string Map, int ShopId, int Line, string Method, string? ItemName, int ItemId);

// One Roslyn walk per script file; later Find/IndexShops/catalog calls reuse the facts.
internal static class ScriptEvidence
{
    static readonly ConcurrentDictionary<string, (DateTime Written, long Length, ParsedScript Parsed)> Cache = new(StringComparer.Ordinal);

    public static IEnumerable<ParsedScript> Scan(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (Skip(file)) continue;
            yield return Load(root, file);
        }
    }

    static bool Skip(string file)
    {
        try { return file.Contains("Generated-", StringComparison.Ordinal) || new FileInfo(file).Length > 2_000_000; }
        catch (IOException) { return true; }
    }

    static ParsedScript Load(string root, string file)
    {
        DateTime written;
        long length;
        try
        {
            var info = new FileInfo(file);
            written = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch (IOException) { return new ParsedScript(); }

        if (Cache.TryGetValue(file, out var hit) && hit.Written == written && hit.Length == length)
            return hit.Parsed;

        var parsed = Parse(root, file);
        Cache[file] = (written, length, parsed);
        if (Cache.Count > 8000)
            foreach (var key in Cache.Keys.Take(1000))
                Cache.TryRemove(key, out _);
        return parsed;
    }

    static ParsedScript Parse(string root, string file)
    {
        string text;
        try { text = File.ReadAllText(file); }
        catch (IOException) { return new ParsedScript(); }

        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var parsed = new ParsedScript
        {
            RelativePath = relative,
            DisplayName = Regex.Replace(Path.GetFileNameWithoutExtension(file), "([a-z])([A-Z])", "$1 $2")
        };

        var metadata = Regex.Match(text, @"\A\s*/\*(.*?)\*/", RegexOptions.Singleline);
        var title = Regex.Match(metadata.Groups[1].Value, @"(?m)^name:\s*([^\r\n]+)");
        string farmTitle = title.Success ? title.Groups[1].Value.Trim() : "";

        if (!text.Contains("HuntMonster(") && !text.Contains("BuyItem") && !text.Contains("StartBuyAllMerge")
            && !text.Contains("OptionsStorage") && farmTitle.Length == 0)
            return parsed;

        var tree = CSharpSyntaxTree.ParseText(text).GetRoot();
        string? Str(ExpressionSyntax? e) => e is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression) ? l.Token.ValueText : null;
        int Num(ExpressionSyntax? e) => e is LiteralExpressionSyntax l && int.TryParse(l.Token.ValueText, out int n) ? n : 0;
        string? MapOf(ExpressionSyntax? e) => e is LiteralExpressionSyntax l && l.Token.Value is string route ? route : Str(e);
        int ShopOf(ExpressionSyntax? e) => e is LiteralExpressionSyntax l && l.Token.Value is int shop ? shop : Num(e);

        foreach (var call in tree.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax member) continue;
            string method = member.Name.Identifier.ValueText;
            var args = call.ArgumentList.Arguments;
            ExpressionSyntax? Arg(string name, int index) => args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name)?.Expression
                ?? (args.Count > index && args[index].NameColon == null ? args[index].Expression : null);

            if (method == "HuntMonster")
            {
                string? map = Str(Arg("map", 0)), monster = Str(Arg("monster", 1)), drop = Str(Arg("item", 2));
                var temp = Arg("isTemp", 4);
                if (map == null || monster == null || drop == null || (temp != null && temp is not LiteralExpressionSyntax)) continue;
                if (temp != null && !temp.IsKind(SyntaxKind.TrueLiteralExpression) && !temp.IsKind(SyntaxKind.FalseLiteralExpression)) continue;
                bool temporary = temp == null || temp.IsKind(SyntaxKind.TrueLiteralExpression);
                int line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                parsed.Drops.Add(new(map, monster, drop, temporary, relative + ":" + line));
            }

            if (method is "BuyItem" or "StartBuyAllMerge")
            {
                string? map = MapOf(Arg("map", 0));
                int shop = ShopOf(Arg("shopID", 1));
                if (map == null || !Regex.IsMatch(map, @"^[a-zA-Z0-9_-]+$") || shop <= 0) continue;
                int line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                parsed.Shops.Add(new(map, shop, line, method, Str(Arg("name", 2)), Num(Arg("itemID", 2))));
            }
        }

        foreach (var created in tree.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var optionArgs = created.ArgumentList?.Arguments;
            if (created.Type.ToString() == "Option<bool>" && optionArgs is { Count: >= 2 })
            {
                string? id = Str(optionArgs.Value[0].Expression), name = Str(optionArgs.Value[1].Expression);
                if (id != null && name != null) parsed.BoolOptions.Add((id, name));
            }
        }

        if (farmTitle.Length > 0)
        {
            foreach (var type in tree.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (type.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword)) || type.Members.OfType<ConstructorDeclarationSyntax>().Any()) continue;
                if (!type.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ValueText == "ScriptMain" && m.ParameterList.Parameters.Count == 1 && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)))) continue;
                parsed.FullFarms.Add((type.Identifier.ValueText, farmTitle));
            }
        }

        foreach (var cls in tree.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (cls.TypeParameterList != null || cls.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword) || m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.AbstractKeyword)) || cls.Members.OfType<ConstructorDeclarationSyntax>().Any()) continue;
            if (!cls.Members.OfType<FieldDeclarationSyntax>().Any(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) && f.Declaration.Variables.Any(v => v.Identifier.ValueText == "OptionsStorage"))) continue;
            foreach (var en in cls.Members.OfType<EnumDeclarationSyntax>())
            {
                var itemIds = en.Members
                    .Select(member => member.EqualsValue?.Value is LiteralExpressionSyntax literal && int.TryParse(literal.Token.ValueText, out int id) ? id : 0)
                    .Where(id => id > 0).ToArray();
                if (itemIds.Length == 0) continue;
                var option = cls.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault(o => o.Type is GenericNameSyntax g && g.Identifier.ValueText == "Option" && g.TypeArgumentList.Arguments.Count == 1 && g.TypeArgumentList.Arguments[0].ToString() == en.Identifier.ValueText);
                if (option?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax key || !key.IsKind(SyntaxKind.StringLiteralExpression)) continue;
                foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) || method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) continue;
                    var parameters = method.ParameterList.Parameters;
                    if (parameters.Count == 0 || parameters[0].Type?.ToString() != en.Identifier.ValueText || parameters.Skip(1).Any(p => p.Default == null)) continue;
                    var questIds = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Where(call => call.Expression is MemberAccessExpressionSyntax mem && mem.Name.Identifier.ValueText is "EnsureAccept" or "Accept"
                            && call.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax questLit
                            && int.TryParse(questLit.Token.ValueText, out _))
                        .Select(call => int.Parse(((LiteralExpressionSyntax)call.ArgumentList.Arguments[0].Expression).Token.ValueText))
                        .Distinct().ToArray();
                    foreach (int questId in questIds)
                        foreach (int itemId in itemIds)
                            parsed.Recipes.Add(new(relative, cls.Identifier.ValueText, method.Identifier.ValueText, en.Identifier.ValueText, key.Token.ValueText, questId, itemId));
                }
            }
        }

        return parsed;
    }
}
