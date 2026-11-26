using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nora.Desktop.Views;

/// <summary>
/// Altura das telas da Secretaria que usam <see cref="PageLayout.FillProperty"/> (Alertas e Relatórios e metas). O contêiner da Secretaria
/// (<c>MunicipalView</c>) empilha as telas num painel sem limite de altura, então a tela mede a área de conteúdo (a rolagem da página) e assume a altura
/// dela: a página não rola, a lista rola sozinha e o balão de ações fica no pé da tela. Acompanha a janela quando ela muda de tamanho e se solta ao sair.
/// </summary>
internal sealed class ContentAreaFill
{
    /// <summary>Margens verticais da área de conteúdo da janela principal (32 em cima e 16 embaixo).</summary>
    private const double AreaMargins = 48;
    private const double MinimumHeight = 320;

    private readonly FrameworkElement screen;
    private ScrollViewer? area;

    private ContentAreaFill(FrameworkElement screen)
    {
        this.screen = screen;
        screen.Loaded += (_, _) => Connect();
        screen.Unloaded += (_, _) => Disconnect();
    }

    /// <summary>Liga a tela à área de conteúdo enquanto ela estiver carregada.</summary>
    public static void Attach(FrameworkElement screen) => _ = new ContentAreaFill(screen);

    private void Connect()
    {
        Disconnect();
        for (DependencyObject? node = VisualTreeHelper.GetParent(screen); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer found) { area = found; break; }
        if (area is null) return;
        area.SizeChanged += OnAreaChanged;
        Apply();
    }

    private void Disconnect()
    {
        if (area is not null) area.SizeChanged -= OnAreaChanged;
        area = null;
    }

    private void OnAreaChanged(object sender, SizeChangedEventArgs args) => Apply();

    private void Apply()
    {
        if (area is { ActualHeight: > 0 }) screen.Height = Math.Max(MinimumHeight, area.ActualHeight - AreaMargins);
    }
}
