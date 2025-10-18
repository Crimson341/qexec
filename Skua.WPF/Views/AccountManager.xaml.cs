using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Core.ViewModels.Manager;
using System.Windows;
using System.Windows.Controls;

namespace Skua.WPF.Views;

/// <summary>
/// Interaction logic for AccountManager.xaml
/// </summary>
public sealed partial class AccountManagerView : UserControl
{
    public AccountManagerView()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<AccountManagerViewModel>();
        StrongReferenceMessenger.Default.Register<AccountManagerView, ClearPasswordBoxMessage>(this, ClearPasswordBox);
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StrongReferenceMessenger.Default.UnregisterAll(this);
        Unloaded -= OnUnloaded;
    }

    private void ClearPasswordBox(AccountManagerView recipient, ClearPasswordBoxMessage message)
    {
        PswBox?.Clear();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ((AccountManagerViewModel)DataContext).PasswordInput = ((PasswordBox)sender).Password;
    }
}
