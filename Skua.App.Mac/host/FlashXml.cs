using System.Collections;
using System.Globalization;
using System.Xml.Linq;

namespace Skua.Mac;

public static class FlashXml
{
    public static XElement Encode(object? value) => value switch
    {
        null => new("null"),
        bool b => new(b ? "true" : "false"),
        string s => new("string", s),
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            => new("number", Convert.ToString(value, CultureInfo.InvariantCulture)),
        IDictionary<string, object> map => new("object", map.Select(p => new XElement("property", new XAttribute("id", p.Key), Encode(p.Value)))),
        IEnumerable items => new("array", items.Cast<object?>().Select((v, i) => new XElement("property", new XAttribute("id", i), Encode(v)))),
        _ => new("string", value.ToString())
    };
    public static object? Decode(XElement element) => element.Name.LocalName switch
    {
        "null" or "undefined" => null,
        "true" => true,
        "false" => false,
        "number" => double.Parse(element.Value, CultureInfo.InvariantCulture),
        "array" => DecodeArray(element),
        "object" => element.Elements("property").ToDictionary(p => (string)p.Attribute("id")!, p => Decode(p.Elements().Single())),
        "string" => element.Value,
        _ => throw new FormatException($"Unknown Flash XML value: {element.Name}")
    };
    private static object?[] DecodeArray(XElement element)
    {
        var values = element.Elements("property").Select(p =>
            (index: int.Parse((string)p.Attribute("id")!, CultureInfo.InvariantCulture), value: Decode(p.Elements().Single()))).ToArray();
        if (values.Any(p => p.index < 0 || p.index > 100_000)) throw new FormatException("Flash array index out of range.");
        var result = new object?[values.Length == 0 ? 0 : values.Max(p => p.index) + 1];
        foreach (var value in values) result[value.index] = value.value;
        return result;
    }
    public static string Invoke(string function, object[] args) =>
        new XElement("invoke", new XAttribute("name", function), new XAttribute("returntype", "xml"),
            new XElement("arguments", args.Select(Encode))).ToString(SaveOptions.DisableFormatting);
}
