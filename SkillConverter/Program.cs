using Skua.Core.Skills;
using System.Text.Json;

string textFile = @"C:\Users\bowli\AppData\Roaming\Skua\UserAdvancedSkills.txt";
string jsonFile = @"C:\Users\bowli\AppData\Roaming\Skua\UserAdvancedSkills.json";

if (File.Exists(textFile))
{
    var content = File.ReadAllText(textFile);
    var jsonConfig = AdvancedSkillsParser.ParseTextToJson(content);
    
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    var json = JsonSerializer.Serialize(jsonConfig, options);
    File.WriteAllText(jsonFile, json);
    
    Console.WriteLine($"Converted {textFile} to {jsonFile}");
}
else
{
    Console.WriteLine($"File not found: {textFile}");
}
