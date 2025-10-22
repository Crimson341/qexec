using CommunityToolkit.Mvvm.ComponentModel;

namespace Skua.Core.ViewModels.Manager;

public partial class HuntMethodModel : ObservableObject
{
    [ObservableProperty]
    private string mapName = "map";

    [ObservableProperty]
    private string monsterName = "monster";

    [ObservableProperty]
    private string itemName = "item";

    [ObservableProperty]
    private int quantity = 10;

    public override string ToString() => $"Hunt: {monsterName} x{quantity}";
}

public partial class KillMethodModel : ObservableObject
{
    [ObservableProperty]
    private string mapName = "map";

    [ObservableProperty]
    private string monsterName = "monster";

    [ObservableProperty]
    private int quantity = 10;

    public override string ToString() => $"Kill: {monsterName} x{quantity}";
}

public partial class QuestMethodModel : ObservableObject
{
    [ObservableProperty]
    private int questId = 0;

    public override string ToString() => $"Quest: ID {questId}";
}

public partial class StoryMethodModel : ObservableObject
{
    [ObservableProperty]
    private int questId = 0;

    [ObservableProperty]
    private string methodType = "KillQuest";

    [ObservableProperty]
    private string mapName = "mapname";

    [ObservableProperty]
    private string monsterName = "MonsterName";

    [ObservableProperty]
    private string itemIds = "1,2,3";

    public override string ToString() => $"Story.{methodType}(questId: {questId})";
}
