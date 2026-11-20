using System.IO;
using System.Globalization;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Nora.Desktop.Views;

public partial class NetworkMapView : UserControl
{
    // O panorama inicial é geográfico, não representa a localização de uma UBS.
    private const string MapUrl = "https://www.openstreetmap.org/export/embed.html?bbox=-74%2C-34%2C-34%2C6&layer=mapnik";
    private bool closed;
    private bool handlersAttached;
    private bool showingUnit;
    public NetworkMapView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => { closed = true; Browser.Dispose(); };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel { IsAuthenticated: true, UnitsLoaded: false, CanOperate: true } vm) await vm.LoadUnitsAsync();
    }
    private async void OnRefreshUnits(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm) await vm.LoadUnitsAsync();
    }
    public static string MapAddress(HealthUnit? unit)
    {
        if (unit == null) return MapUrl;
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        var left = Math.Max(-180, unit.Longitude - 0.012);
        var right = Math.Min(180, unit.Longitude + 0.012);
        var bottom = Math.Max(-85, unit.Latitude - 0.008);
        var top = Math.Min(85, unit.Latitude + 0.008);
        return $"https://www.openstreetmap.org/export/embed.html?bbox={Number(left)}%2C{Number(bottom)}%2C{Number(right)}%2C{Number(top)}&layer=mapnik&marker={Number(unit.Latitude)}%2C{Number(unit.Longitude)}";
    }
    private async void OnLoadMap(object sender, RoutedEventArgs e)
    {
        LoadButton.IsEnabled = false;
        MapStatus.Text = "Carregando mapa…";
        try
        {
            var data = Environment.GetEnvironmentVariable("NORA_MAP_BROWSER_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA", "MapBrowser");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
            if (closed) return;
            await Browser.EnsureCoreWebView2Async(environment);
            if (closed) return;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            if (!handlersAttached)
            {
            handlersAttached = true;
            Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            Browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "www.openstreetmap.org" || uri.AbsolutePath != "/export/embed.html") args.Cancel = true;
            };
            Browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (closed) return;
                if (args.IsSuccess)
                {
                    MapStatus.Text = showingUnit ? "Mapa carregado com a UBS selecionada." : "Mapa carregado.";
                    LoadButton.IsEnabled = true;
                    Placeholder.Visibility = Visibility.Collapsed;
                    Browser.Visibility = Visibility.Visible;
                }
                else
                {
                    MapStatus.Text = "Não foi possível carregar o mapa. Verifique a conexão e tente novamente.";
                    Browser.Visibility = Visibility.Collapsed;
                    Placeholder.Visibility = Visibility.Visible;
                    LoadButton.IsEnabled = true;
                }
            };
            }
            Placeholder.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            var unit = (DataContext as WorkspaceViewModel)?.SelectedUnit;
            showingUnit = unit != null;
            Browser.CoreWebView2.Navigate(MapAddress(unit));
        }
        catch (Exception)
        {
            if (closed) return;
            MapStatus.Text = "Não foi possível iniciar o mapa. Verifique a conexão e o Microsoft Edge WebView2 Runtime.";
            Browser.Visibility = Visibility.Collapsed;
            Placeholder.Visibility = Visibility.Visible;
            LoadButton.IsEnabled = true;
        }
    }
}
