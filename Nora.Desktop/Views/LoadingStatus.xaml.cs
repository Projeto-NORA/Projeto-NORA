using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nora.Desktop.Views;

/// <summary>
/// Indicador de leitura das telas de números da rede (indicadores, alertas, cadastro da UBS em leitura): gira na cor da marca com o passo escrito ao lado
/// enquanto <see cref="Text"/> tem texto, e some quando a leitura termina. A animação só roda enquanto o indicador está na tela.
/// </summary>
public partial class LoadingStatus : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(LoadingStatus),
        new PropertyMetadata("", (owner, _) => ((LoadingStatus)owner).Update()));

    private static readonly DoubleAnimation Spin = CreateSpin();

    private static DoubleAnimation CreateSpin()
    {
        var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1))) { RepeatBehavior = RepeatBehavior.Forever };
        spin.Freeze();
        return spin;
    }

    public LoadingStatus()
    {
        InitializeComponent();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    /// <summary>O passo em andamento ("Atualizando os indicadores…"); vazio esconde o indicador.</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private void Update()
    {
        var text = Text ?? "";
        Label.Text = text;
        AutomationProperties.SetName(this, text);
        var running = text.Length > 0;
        Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, running && IsLoaded ? Spin : null);
    }
}
