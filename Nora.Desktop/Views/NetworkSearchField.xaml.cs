using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Campo "Pesquisar" em linha do cabeçalho das telas de números da rede. Largura preferida de 260 px; com pouco espaço encolhe até 140 antes de qualquer botão sumir
/// (como o campo das listagens). O texto vai para <c>WorkspaceViewModel.NetworkSearch</c>, que o "Limpar filtros" também apaga.
/// </summary>
public partial class NetworkSearchField : UserControl
{
    private const double PreferredWidth = 260;

    public NetworkSearchField() => InitializeComponent();

    /// <summary>Diz qual tela é pesquisada, para o nome acessível do campo ("Pesquisar: Visão geral").</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value ?? "");
    }
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(NetworkSearchField),
        new PropertyMetadata("", (owner, args) => AutomationProperties.SetName(((NetworkSearchField)owner).Box, ((string)args.NewValue).Length > 0 ? $"Pesquisar: {args.NewValue}" : "Pesquisar")));

    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        return new Size(double.IsInfinity(constraint.Width) ? PreferredWidth : Math.Min(constraint.Width, PreferredWidth), size.Height);
    }
}
