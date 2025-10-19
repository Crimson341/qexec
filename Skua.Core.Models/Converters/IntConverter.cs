using Newtonsoft.Json;

namespace Skua.Core.Models.Converters;

public class IntConverter : JsonConverter<int>
{
    public override void WriteJson(JsonWriter writer, int value, JsonSerializer serializer)
    {
        writer.WriteValue(value);
    }

    public override int ReadJson(JsonReader reader, Type objectType, int existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.Value == null)
            return 0;
        if (int.TryParse(reader.Value.ToString(), out int result))
            return result;
        return 0;
    }
}