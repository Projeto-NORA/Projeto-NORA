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
    private bool filterClosedByButtonPress;

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

    /// <summary>Grupos visíveis da tela que ficam no pop-up do botão Filtros (os em linha vão direto para o cabeçalho).</summary>
    private PageFilterGroup[] PopupFilterGroups() => pageFilters.Where(group => group.IsVisible && group.Fields is not null && !group.IsInline).ToArray();

    /// <summary>
    /// Atualiza o cabeçalho com os filtros da tela visível: os grupos em linha (pesquisa, período…) aparecem no próprio cabeçalho e o botão
    /// Filtros só aparece quando sobra algum grupo para o pop-up.
    /// </summary>
    internal void UpdatePageFilters()
    {
        if (HeaderItems is null) return;
        var popupGroups = PopupFilterGroups();
        if (FiltersPopup.IsOpen && !popupGroups.SequenceEqual(openFilterGroups)) FiltersPopup.IsOpen = false;
        FiltersButton.Visibility = popupGroups.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var inline = pageFilters.Where(group => group.IsVisible && group.Fields is not null && group.IsInline).ToArray();
        SyncHeaderHosts(inlineFilterHosts, inline, group => group.Fields, group => group.Label, beforeFilters: true);
    }

    private void OnOpenFilters(object sender, RoutedEventArgs args)
    {
        if (filterClosedByButtonPress) { filterClosedByButtonPress = false; return; }
        if (FiltersPopup.IsOpen) { FiltersPopup.IsOpen = false; return; }
        openFilterGroups = PopupFilterGroups();
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
        // StaysOpen=false closes the popup on mouse down, before the button raises Click.
        var point = Mouse.GetPosition(FiltersButton);
        filterClosedByButtonPress = Mouse.LeftButton == MouseButtonState.Pressed &&
            point.X >= 0 && point.Y >= 0 && point.X < FiltersButton.ActualWidth && point.Y < FiltersButton.ActualHeight;
        foreach (var item in FilterGroups.Children.OfType<ContentControl>()) item.Content = null;
        FilterGroups.Children.Clear();
        openFilterGroups = [];
    }

    private void OnFiltersButtonMouseUp(object sender, MouseButtonEventArgs args)
    {
        // Click is raised after PreviewMouseLeftButtonUp; clear a cancelled drag afterwards too.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => filterClosedByButtonPress = false);
    }

    /// <summary>"Limpar filtros" limpa tudo o que filtra a tela, como antes do cabeçalho novo: os grupos do pop-up e a pesquisa em linha da tela visível.</summary>
    private async void OnClearFilters(object sender, RoutedEventArgs args)
    {
        foreach (var group in pageFilters)
            if (group.IsVisible && group.Fields is ListSearchFilter search) search.Clear();
        try { await ViewModel.Workspace.ClearPageFiltersAsync(openFilterGroups.Select(group => group.FilterKey).ToHashSet(StringComparer.Ordinal)); }
        catch (Exception error) when (error is not OutOfMemoryException) { ViewModel.Workspace.ReportUnexpected(error); }
    }
}
