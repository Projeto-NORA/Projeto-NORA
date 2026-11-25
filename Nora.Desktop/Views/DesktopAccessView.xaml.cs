using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class DesktopAccessView : UserControl
{
    public DesktopAccessView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(() => { if (IsVisible) Email.Focus(); });
            else LoginPassword.Clear();
        };
    }

    private async void OnLogin(object sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel { CanLogin: true } model) return;
        await model.LoginAsync(LoginPassword.Password);
        if (!model.NeedsLoginUnit || model.IsAuthenticated) LoginPassword.Clear();
    }

    private void OnLoginKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        if (sender == LoginPassword && Unit.IsVisible && Unit.IsEnabled)
        {
            Unit.Focus();
            args.Handled = true;
            return;
        }
        if (sender == Unit && Unit.IsDropDownOpen) return;
        args.Handled = true;
        OnLogin(sender, args);
    }

    private void OnEmailKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        LoginPassword.Focus();
        args.Handled = true;
    }
}
