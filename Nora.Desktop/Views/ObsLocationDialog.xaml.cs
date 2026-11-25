using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace Nora.Desktop.Views;

public partial class ObsLocationDialog : Window
{
    private const string LocalPage = "https://nora-location.invalid/";
    private const string Html = """
        <!doctype html><html lang="pt-BR"><meta charset="utf-8"><body>Obtendo localização…<script>
        if (!navigator.geolocation) window.chrome.webview.postMessage({ok:false});
        else navigator.geolocation.getCurrentPosition(
          p => window.chrome.webview.postMessage({ok:true,latitude:p.coords.latitude,longitude:p.coords.longitude}),
          () => window.chrome.webview.postMessage({ok:false}),
          {enableHighAccuracy:false,timeout:15000,maximumAge:600000});
        </script></body></html>
        """;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(25) };
    private bool closed;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    public ObsLocationDialog()
    {
        InitializeComponent();
        Services.DialogService.RoundCorners(this);
        Loaded += OnLoaded;
        timer.Tick += (_, _) => Fail("Não foi possível obter uma localização recente. Escolha a UBS manualmente.");
        Closed += (_, _) => { closed = true; timer.Stop(); Browser.Dispose(); };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        timer.Start();
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA", "LocationBrowser");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
            if (closed) return;
            await Browser.EnsureCoreWebView2Async(environment);
            if (closed) return;
            var core = Browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationStarting += (_, args) => { if (!string.Equals(args.Uri, LocalPage, StringComparison.Ordinal)) args.Cancel = true; };
            core.PermissionRequested += (_, args) =>
            {
                args.State = args.PermissionKind == CoreWebView2PermissionKind.Geolocation && args.Uri == LocalPage ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                args.SavesInProfile = false;
            };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) =>
            {
                var allowed = string.Equals(args.Request.Uri, LocalPage, StringComparison.Ordinal);
                var body = new MemoryStream(Encoding.UTF8.GetBytes(allowed ? Html : ""));
                args.Response = environment.CreateWebResourceResponse(body, allowed ? 200 : 403, allowed ? "OK" : "Forbidden", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\n");
            };
            core.WebMessageReceived += (_, args) =>
            {
                if (closed || !string.Equals(args.Source, LocalPage, StringComparison.Ordinal)) return;
                try
                {
                    using var json = JsonDocument.Parse(args.WebMessageAsJson); var data = json.RootElement;
                    if (!data.GetProperty("ok").GetBoolean()) { Fail("O Windows não forneceu localização. Escolha a UBS manualmente."); return; }
                    var latitude = data.GetProperty("latitude").GetDouble(); var longitude = data.GetProperty("longitude").GetDouble();
                    if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180) { Fail("Localização inválida. Escolha a UBS manualmente."); return; }
                    Latitude = latitude; Longitude = longitude; DialogResult = true;
                }
                catch (Exception) { Fail("Não foi possível obter a localização. Escolha a UBS manualmente."); }
            };
            core.Navigate(LocalPage);
        }
        catch (Exception) { Fail("Não foi possível iniciar a localização. Confira as permissões do Windows ou escolha a UBS manualmente."); }
    }
    private void Fail(string message)
    {
        if (closed) return;
        timer.Stop(); Status.Text = message;
    }
}
