using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nora.Desktop.Views;

/// <summary>Navegação por Enter entre os campos de um mesmo formulário.</summary>
public static class EnterNavigation
{
    public static readonly DependencyProperty GroupProperty = DependencyProperty.RegisterAttached(
        "Group", typeof(string), typeof(EnterNavigation), new PropertyMetadata(null, OnGroupChanged));
    public static void SetGroup(DependencyObject element, string? value) => element.SetValue(GroupProperty, value);
    public static string? GetGroup(DependencyObject element) => (string?)element.GetValue(GroupProperty);

    public static readonly DependencyProperty ScopeProperty = DependencyProperty.RegisterAttached(
        "Scope", typeof(bool), typeof(EnterNavigation), new PropertyMetadata(false));
    public static void SetScope(DependencyObject element, bool value) => element.SetValue(ScopeProperty, value);
    public static bool GetScope(DependencyObject element) => (bool)element.GetValue(ScopeProperty);

    private static void OnGroupChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not UIElement element || target is Button) return;
        if (args.OldValue is string) element.PreviewKeyDown -= OnPreviewKeyDown;
        if (args.NewValue is string) element.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || sender is not FrameworkElement field) return;
        if (field is TextBox { AcceptsReturn: true } or TextBox { IsReadOnly: true } || field is ComboBox { IsDropDownOpen: true }) return;
        var group = GetGroup(field);
        if (string.IsNullOrEmpty(group)) return;

        DependencyObject root = field;
        while (VisualTreeHelper.GetParent(root) is { } parent)
        {
            root = parent;
            if (GetScope(root)) break;
        }
        var elements = new List<FrameworkElement>();
        Collect(root, group, elements);
        var index = elements.IndexOf(field);
        if (index < 0) return;
        for (var i = index + 1; i < elements.Count; i++)
        {
            if (elements[i] is Button) continue;
            if (!elements[i].IsEnabled || !elements[i].Focusable || elements[i] is TextBox { IsReadOnly: true }) continue;
            if (elements[i].Focus()) { args.Handled = true; return; }
        }
        var button = elements.OfType<Button>().FirstOrDefault();
        if (button is null) return;
        args.Handled = true;
        if (button.IsEnabled && UIElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
            invoke.Invoke();
    }

    private static void Collect(DependencyObject node, string group, List<FrameworkElement> elements)
    {
        if (node is UIElement { IsVisible: false }) return;
        if (node is FrameworkElement element && GetGroup(element) == group) elements.Add(element);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            Collect(VisualTreeHelper.GetChild(node, i), group, elements);
    }
}
