using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using System.IO;

namespace Skua.Core.ViewModels.Manager;

public partial class ScriptCreatorViewModel : BotControlViewModelBase
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string scriptName = "MyScript";

    [ObservableProperty]
    private string scriptDescription = "";

    [ObservableProperty]
    private bool useCoreBots = true;

    [ObservableProperty]
    private bool useCoreStory = false;

    [ObservableProperty]
    private bool useCoreAdvanced = false;

    [ObservableProperty]
    private bool useCoreFarms = false;

    [ObservableProperty]
    private bool useCoreDailies = false;

    [ObservableProperty]
    private RangedObservableCollection<HuntMethodModel> huntMethods = new();

    [ObservableProperty]
    private RangedObservableCollection<KillMethodModel> killMethods = new();

    [ObservableProperty]
    private RangedObservableCollection<QuestMethodModel> questMethods = new();

    [ObservableProperty]
    private RangedObservableCollection<StoryMethodModel> storyMethods = new();

    [ObservableProperty]
    private string generatedCode = "";

    [ObservableProperty]
    private string statusMessage = "";

    public ScriptCreatorViewModel(ISettingsService settingsService)
        : base("Script Creator")
    {
        _settingsService = settingsService;
    }

    [RelayCommand]
    private void GenerateScript()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ScriptName))
            {
                StatusMessage = "Script name cannot be empty.";
                return;
            }

            var template = GenerateScriptTemplate();
            GeneratedCode = template;
            StatusMessage = "Script generated successfully!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveScript()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ScriptName))
            {
                StatusMessage = "Script name cannot be empty.";
                return;
            }

            if (string.IsNullOrWhiteSpace(GeneratedCode))
            {
                StatusMessage = "Please generate a script first.";
                return;
            }

            string scriptsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Skua", "Scripts"
            );

            if (!Directory.Exists(scriptsPath))
            {
                Directory.CreateDirectory(scriptsPath);
            }

            string fileName = $"{ScriptName.Replace(" ", "_")}.cs";
            string filePath = Path.Combine(scriptsPath, fileName);

            await File.WriteAllTextAsync(filePath, GeneratedCode);
            StatusMessage = $"Script saved to: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving script: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddHuntMethod()
    {
        HuntMethods.Add(new HuntMethodModel());
    }

    [RelayCommand]
    private void AddKillMethod()
    {
        KillMethods.Add(new KillMethodModel());
    }

    [RelayCommand]
    private void AddQuestMethod()
    {
        QuestMethods.Add(new QuestMethodModel());
    }

    [RelayCommand]
    private void AddStoryMethod()
    {
        StoryMethods.Add(new StoryMethodModel());
    }

    [RelayCommand]
    private void RemoveHuntMethod(HuntMethodModel method)
    {
        HuntMethods.Remove(method);
    }

    [RelayCommand]
    private void RemoveKillMethod(KillMethodModel method)
    {
        KillMethods.Remove(method);
    }

    [RelayCommand]
    private void RemoveQuestMethod(QuestMethodModel method)
    {
        QuestMethods.Remove(method);
    }

    [RelayCommand]
    private void RemoveStoryMethod(StoryMethodModel method)
    {
        StoryMethods.Remove(method);
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (string.IsNullOrWhiteSpace(GeneratedCode))
        {
            StatusMessage = "No script to copy.";
            return;
        }
        StatusMessage = "Script ready to copy (use View)";
    }

    private string GenerateScriptTemplate()
    {
        var includes = new List<string> { "Scripts/CoreBots.cs" };

        if (UseCoreStory)
            includes.Add("Scripts/CoreStory.cs");
        if (UseCoreAdvanced)
            includes.Add("Scripts/CoreAdvanced.cs");
        if (UseCoreFarms)
            includes.Add("Scripts/CoreFarms.cs");
        if (UseCoreDailies)
            includes.Add("Scripts/CoreDailies.cs");

        var includeLines = includes.Select(inc => $"//cs_include {inc}").ToList();

        var includes_section = string.Join(Environment.NewLine, includeLines);
        var property_declarations = GeneratePropertyDeclarations();
        var example_method = GenerateExampleMethod();

        return $@"/*
name: {ScriptName}
description: {ScriptDescription}
tags: null
*/
{includes_section}
using Skua.Core.Interfaces;
using Skua.Core.Models.Items;
using Skua.Core.Models.Monsters;
using Skua.Core.Models.Quests;

public class {SanitizeClassName(ScriptName)}
{{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
{property_declarations}
    public void ScriptMain(IScriptInterface Bot)
    {{
        Core.SetOptions(disableClassSwap: true);

        Example();

        Core.SetOptions(false);
    }}

{example_method}
}}
";
    }

    private string GeneratePropertyDeclarations()
    {
        var properties = new List<string> 
        { 
            "    private static CoreAdvanced Adv { get => _Adv ??= new CoreAdvanced(); set => _Adv = value; }" + Environment.NewLine + "    private static CoreAdvanced _Adv;" 
        };

        if (UseCoreStory)
        {
            properties.Add("    private static CoreStory Story { get => _Story ??= new CoreStory(); set => _Story = value; }" + Environment.NewLine + "    private static CoreStory _Story;");
        }

        if (UseCoreAdvanced)
        {
            properties.Add("    private static CoreAdvanced Advanced { get => _Advanced ??= new CoreAdvanced(); set => _Advanced = value; }" + Environment.NewLine + "    private static CoreAdvanced _Advanced;");
        }

        if (UseCoreFarms)
        {
            properties.Add("    private static CoreFarms Farm { get => _Farm ??= new CoreFarms(); set => _Farm = value; }" + Environment.NewLine + "    private static CoreFarms _Farm;");
        }

        if (UseCoreDailies)
        {
            properties.Add("    private static CoreDailies Daily { get => _Daily ??= new CoreDailies(); set => _Daily = value; }" + Environment.NewLine + "    private static CoreDailies _Daily;");
        }

        return Environment.NewLine + string.Join(Environment.NewLine + "    ", properties);
    }

    private string GenerateExampleMethod()
    {
        var exampleLines = new List<string>();
        exampleLines.Add("    public void Example()");
        exampleLines.Add("    {");

        if (HuntMethods.Count > 0 || KillMethods.Count > 0 || QuestMethods.Count > 0 || StoryMethods.Count > 0)
        {
            foreach (var hunt in HuntMethods)
            {
                exampleLines.Add($"        Core.HuntMonster(\"{hunt.MapName}\", \"{hunt.MonsterName}\", \"{hunt.ItemName}\", {hunt.Quantity});");
            }

            foreach (var kill in KillMethods)
            {
                exampleLines.Add($"        Core.KillMonster(\"{kill.MapName}\", \"{kill.MonsterName}\", {kill.Quantity});");
            }

            foreach (var quest in QuestMethods)
            {
                exampleLines.Add($"        Core.RegisterQuests({quest.QuestId});");
                exampleLines.Add("        // Complete quest logic here");
                exampleLines.Add("        Core.CancelRegisteredQuests();");
            }

            foreach (var story in StoryMethods)
            {
                if (story.MethodType == "KillQuest")
                {
                    exampleLines.Add($"        Story.KillQuest({story.QuestId}, \"{story.MapName}\", \"{story.MonsterName}\");");
                }
                else if (story.MethodType == "MapItemQuest")
                {
                    exampleLines.Add($"        Story.MapItemQuest({story.QuestId}, \"{story.MapName}\", new[] {{ {story.ItemIds} }});");
                }
                else if (story.MethodType == "ChainQuest")
                {
                    exampleLines.Add($"        Story.ChainQuest({story.QuestId});");
                }
            }
        }
        else
        {
            exampleLines.Add("        // INSERT YOUR SCRIPT LOGIC HERE");
        }

        exampleLines.Add("    }");
        return string.Join(Environment.NewLine, exampleLines);
    }

    private static string SanitizeClassName(string name)
    {
        var sanitized = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (sanitized.Length == 0)
            return "GeneratedScript";
        if (char.IsDigit(sanitized[0]))
            sanitized = "_" + sanitized;
        return sanitized;
    }
}
