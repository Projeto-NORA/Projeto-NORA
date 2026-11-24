using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class SettingsView : UserControl
{
    private Button? shortcutCapture;
    public SettingsView() => InitializeComponent();

    private void OnAppearanceSelected(object sender, RoutedEventArgs args)
    {
        if (DataContext is ShellViewModel model && sender is RadioButton option
            && Enum.TryParse<Appearance>(option.CommandParameter?.ToString(), out var appearance) && model.Appearance != appearance)
            model.SetAppearance(appearance);
    }

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var stacked = args.NewSize.Width < 870;
        ShortcutColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(ShortcutCard, stacked ? 0 : 1);
        Grid.SetRow(ShortcutCard, stacked ? 1 : 0);
        PreferencesColumn.Margin = new Thickness(0, 0, stacked ? 0 : 10, 0);
        ShortcutCard.Margin = new Thickness(stacked ? 0 : 10, 0, 0, 16);
    }

    private void OnRequestRestore(object sender, RoutedEventArgs args)
    {
        RestoreConfirmation.Visibility = Visibility.Visible;
        ConfirmRestore.Focus();
    }

    private void OnCancelRestore(object sender, RoutedEventArgs args) => RestoreConfirmation.Visibility = Visibility.Collapsed;

    private void OnCaptureShortcut(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: ShortcutRow { Editable: true } row } button) return;
        shortcutCapture = button;
        ShortcutFeedback.Text = $"Pressione o novo atalho para {row.Label}. Esc cancela.";
        button.Focus();
    }

    internal bool TryCaptureShortcut(KeyEventArgs args)
    {
        if (shortcutCapture is not { DataContext: ShortcutRow row } || DataContext is not ShellViewModel model) return false;
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return true;
        shortcutCapture = null;
        if (key == Key.Escape) { ShortcutFeedback.Text = ""; return true; }
        try
        {
            ShortcutFeedback.Text = model.ChangeShortcut(row.Id, new KeyGesture(key, Keyboard.Modifiers)) ?? "Atalho salvo.";
        }
        catch (Exception error) when (error is NotSupportedException or ArgumentException)
        {
            ShortcutFeedback.Text = "Use Ctrl com outra tecla ou uma tecla de F1 a F12.";
        }
        return true;
    }

    private void OnShortcutKeyDown(object sender, KeyEventArgs args)
    {
        if (!args.Handled) args.Handled = TryCaptureShortcut(args);
    }

    private void OnShortcutFocusLost(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (ReferenceEquals(shortcutCapture, sender)) { shortcutCapture = null; ShortcutFeedback.Text = ""; }
    }
}
