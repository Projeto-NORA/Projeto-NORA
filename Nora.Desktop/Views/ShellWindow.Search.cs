using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nora.Desktop.Generics;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class ShellWindow
{
    private Border? screenSearch;
    private TextBox? searchBox;
    private TextBlock? searchCount;
    private IInputElement? searchOrigin;
    private SearchOutline? outline;
    private IReadOnlyList<Func<FrameworkElement?>> searchResults = [];
    private int searchIndex;

    private void InitializeScreenSearch()
    {
        searchBox = new TextBox { Width = 280, MinHeight = 36, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(searchBox, "Pesquisar na tela");
        searchBox.TextChanged += (_, _) => RebuildSearch();
        searchCount = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 76, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetLiveSetting(searchCount, AutomationLiveSetting.Polite);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(searchBox);
        row.Children.Add(searchCount);
        row.Children.Add(SearchButton("↑", "Resultado anterior", () => SelectSearchResult(-1)));
        row.Children.Add(SearchButton("↓", "Próximo resultado", () => SelectSearchResult(1)));
        row.Children.Add(SearchButton("×", "Fechar pesquisa", () => CloseScreenSearch()));
        screenSearch = new Border
        {
            Child = row, Padding = new Thickness(10), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 9, 12, 0), Visibility = Visibility.Collapsed
        };
        screenSearch.SetResourceReference(BorderBrushProperty, "AccentTextBrush");
        screenSearch.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        Grid.SetColumnSpan(screenSearch, 3);
        Panel.SetZIndex(screenSearch, 20);
        ShellLayout.Children.Add(screenSearch);
        ViewModel.Workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated)) CloseScreenSearch(restoreFocus: false);
        };
    }

    private static Button SearchButton(string glyph, string label, Action run)
    {
        var button = new Button { Content = glyph, MinWidth = 36, MinHeight = 36, Padding = new Thickness(5), Margin = new Thickness(2, 0, 0, 0), ToolTip = label };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => run();
        return button;
    }

    private bool HandleScreenSearchKey(KeyEventArgs args)
    {
        if (Keyboard.FocusedElement is DependencyObject focused)
        {
            for (DependencyObject? parent = focused; parent is Visual; parent = VisualTreeHelper.GetParent(parent))
                if (parent is SettingsView view && view.TryCaptureShortcut(args)) return true;
        }
        if (screenSearch?.IsVisible != true) return false;
        if (args.Key == Key.Escape) { CloseScreenSearch(); return true; }
        if (args.Key == Key.Enter && ReferenceEquals(Keyboard.FocusedElement, searchBox))
        {
            SelectSearchResult(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            return true;
        }
        return false;
    }

    private void OpenScreenSearch()
    {
        if (screenSearch is null || searchBox is null) return;
        if (!screenSearch.IsVisible) searchOrigin = Keyboard.FocusedElement;
        screenSearch.Visibility = Visibility.Visible;
        searchBox.Focus();
        searchBox.SelectAll();
        RebuildSearch();
    }

    private void RebuildSearch()
    {
        if (screenSearch?.IsVisible != true || searchBox is null || searchCount is null) return;
        RemoveOutline();
        var query = searchBox.Text.Trim();
        searchResults = ViewModel.ShowCompactAccess
            ? ScreenSearch.Find(DesktopAccessHost, screenSearch, query)
            : ScreenSearch.Find(PageHeader, screenSearch, query).Concat(ScreenSearch.Find(PageContainer, screenSearch, query)).ToArray();
        searchIndex = -1;
        searchCount.Text = query.Length == 0 ? "Digite para buscar" : "Sem resultados";
        if (searchResults.Count > 0) SelectSearchResult(1);
    }

    private void SelectSearchResult(int direction)
    {
        if (searchCount is null || searchResults.Count == 0) return;
        RemoveOutline();
        searchIndex = (searchIndex + direction + searchResults.Count) % searchResults.Count;
        searchCount.Text = $"{searchIndex + 1} de {searchResults.Count}";
        if (searchResults[searchIndex]() is not { IsVisible: true } element) return;
        element.BringIntoView();
        if (AdornerLayer.GetAdornerLayer(element) is { } layer)
        {
            outline = new SearchOutline(element, (Brush)FindResource("AccentTextBrush")) { IsHitTestVisible = false };
            layer.Add(outline);
        }
    }

    private void RemoveOutline()
    {
        if (outline?.Parent is AdornerLayer layer) layer.Remove(outline);
        outline = null;
    }

    private void CloseScreenSearch(bool restoreFocus = true)
    {
        if (screenSearch?.Visibility != Visibility.Visible) return;
        RemoveOutline();
        screenSearch.Visibility = Visibility.Collapsed;
        searchResults = [];
        if (searchBox is not null) searchBox.Text = "";
        if (restoreFocus && searchOrigin is UIElement { IsVisible: true } element) element.Focus();
        searchOrigin = null;
    }

    private sealed class SearchOutline(UIElement element, Brush brush) : Adorner(element)
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRoundedRectangle(null, new Pen(brush, 2), new Rect(AdornedElement.RenderSize), 3, 3);
        }
    }
}
