using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Botão do cabeçalho (vai dentro de <see cref="PageActions"/>) que alterna uma tela entre cards e gráficos. Mostra o ícone do modo atual (cards ou gráfico),
/// e a dica e o nome de automação dizem o modo atual e o que o clique faz. A escolha é lembrada por tela nas preferências do programa (<see cref="ScreenKey"/>;
/// sem chave vale a rota aberta). A tela mostra cards ou gráficos conforme <see cref="Mode"/>, ligado nos dois sentidos (<c>ElementName</c> ou um ViewModel).
/// Fora da janela principal (prévias e testes) o botão só alterna, sem lembrar.
/// </summary>
public sealed class ViewModeToggle : Button
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(ViewMode), typeof(ViewModeToggle),
        new FrameworkPropertyMetadata(ViewMode.Cards, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (owner, _) => ((ViewModeToggle)owner).OnModeChanged()));
    public static readonly DependencyProperty ScreenKeyProperty = DependencyProperty.Register(nameof(ScreenKey), typeof(string), typeof(ViewModeToggle), new PropertyMetadata(""));

    private bool restoring;

    static ViewModeToggle() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ViewModeToggle), new FrameworkPropertyMetadata(typeof(ViewModeToggle)));

    public ViewModeToggle()
    {
        Loaded += (_, _) => Restore();
        Describe();
    }

    /// <summary>O modo em que a tela está.</summary>
    public ViewMode Mode { get => (ViewMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    /// <summary>A tela a que o modo pertence nas preferências (padrão: a rota aberta, como "home" ou "indicators").</summary>
    public string ScreenKey { get => (string)GetValue(ScreenKeyProperty); set => SetValue(ScreenKeyProperty, value ?? ""); }

    private ShellViewModel? Shell => (Window.GetWindow(this) as ShellWindow)?.ViewModel;
    private string Key => ScreenKey.Length > 0 ? ScreenKey : Shell?.Page ?? "";

    protected override void OnClick()
    {
        base.OnClick();
        SetCurrentValue(ModeProperty, Mode == ViewMode.Cards ? ViewMode.Charts : ViewMode.Cards); // mantém a ligação do Mode, se a tela tiver uma
    }

    /// <summary>Ao entrar na tela, o botão assume o modo lembrado para ela (a ligação do <see cref="Mode"/> leva o valor à tela).</summary>
    private void Restore()
    {
        if (Shell is not { } shell || Key.Length == 0) return;
        restoring = true;
        try { SetCurrentValue(ModeProperty, shell.GetViewMode(Key)); }
        finally { restoring = false; }
    }

    private void OnModeChanged()
    {
        Describe();
        if (!restoring && IsLoaded && Shell is { } shell && Key.Length > 0) shell.SetViewMode(Key, Mode);
    }

    private void Describe()
    {
        var (now, next) = Mode == ViewMode.Cards ? ("cards", "gráficos") : ("gráficos", "cards");
        ToolTip = $"Visualização em {now}. Clique para ver em {next}.";
        AutomationProperties.SetName(this, $"Alternar visualização: em {now}, mudar para {next}");
    }
}
