using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Nora.Desktop.Views;

public partial class ShellWindow
{
    private readonly List<PageFilterGroup> pageFilters = [];
    private PageFilterGroup[] openFilterGroups = [];

    internal void RegisterPageFilter(PageFilterGroup group)
    {
        if (!pageFilters.Contains(group)) pageFilters.Add(group);
        UpdatePageFilters();
    }

    internal void UnregisterPageFilter(PageFilterGroup group)
    {
        pageFilters.Remove(group);
        UpdatePageFilters();
    }

    internal void UpdatePageFilters()
    {
        if (FiltersButton is null) return;
        var visible = pageFilters.Where(group => group.IsVisible && group.Fields is not null).ToArray();
        if (FiltersPopup.IsOpen && !visible.SequenceEqual(openFilterGroups)) FiltersPopup.IsOpen = false;
        FiltersButton.Visibility = visible.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenFilters(object sender, RoutedEventArgs args)
    {
        if (FiltersPopup.IsOpen) { FiltersPopup.IsOpen = false; return; }
        openFilterGroups = pageFilters.Where(group => group.IsVisible && group.Fields is not null).ToArray();
        if (openFilterGroups.Length == 0) return;
        foreach (var group in openFilterGroups)
        {
            if (group.Label.Length > 0)
                FilterGroups.Children.Add(new TextBlock { Text = group.Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
            var content = new ContentControl { DataContext = group.DataContext, Content = group.Fields, Margin = new Thickness(0, 0, 0, 10) };
            content.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = group });
            AutomationProperties.SetName(content, group.Label);
            FilterGroups.Children.Add(content);
        }
        FiltersPopup.HorizontalOffset = FiltersButton.ActualWidth - FilterPopupBody.Width;
        FiltersPopup.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (FiltersPopup.IsOpen) FilterGroups.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
    }

    private void OnFilterPopupKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true }) return;
        args.Handled = true;
        FiltersPopup.IsOpen = false;
        FiltersButton.Focus();
    }

    private void OnCloseFilters(object sender, EventArgs args)
    {
        foreach (var item in FilterGroups.Children.OfType<ContentControl>()) item.Content = null;
        FilterGroups.Children.Clear();
        openFilterGroups = [];
    }

    private async void OnClearFilters(object sender, RoutedEventArgs args)
    {
        foreach (var group in openFilterGroups)
            if (group.Fields is ListSearchFilter search) search.Clear();
        try { await ViewModel.Workspace.ClearPageFiltersAsync(openFilterGroups.Select(group => group.FilterKey).ToHashSet(StringComparer.Ordinal)); }
        catch (Exception error) when (error is not OutOfMemoryException) { ViewModel.Workspace.ReportUnexpected(error); }
    }
}
