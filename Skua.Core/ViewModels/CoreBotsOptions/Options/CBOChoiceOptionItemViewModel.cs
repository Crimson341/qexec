using System.Numerics;

namespace Skua.Core.ViewModels;

public partial class CBOChoiceOptionItemViewModel : DisplayOptionItemViewModel<int>
{
    public CBOChoiceOptionItemViewModel(string optionTitle, string description, string tag, List<string> options)
        : base(optionTitle, description, tag)
    {
        Options = options;
        Value = 0;
    }

    public CBOChoiceOptionItemViewModel(string optionTitle, string description, string tag, List<string> options, int value)
        : base(optionTitle, description, tag)
    {
        Options = options;
        Value = ParseValue(value);
    }

    private static int ParseValue(int? parse)
    {
        return parse is not null && int.TryParse(parse.ToString(), result: out int integer) ? integer : 10000;
    }

    public List<string> Options { get; }
}