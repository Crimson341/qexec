using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.ViewModels.Manager;
using System.Windows;
using System.Windows.Controls;

namespace Skua.WPF.Views;

public partial class ScriptCreatorView : UserControl
{
    public ScriptCreatorView()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<ScriptCreatorViewModel>();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as ScriptCreatorViewModel;
        if (viewModel != null && !string.IsNullOrWhiteSpace(viewModel.GeneratedCode))
        {
            Clipboard.SetText(viewModel.GeneratedCode);
            viewModel.StatusMessage = "Script copied to clipboard!";
        }
    }
}
