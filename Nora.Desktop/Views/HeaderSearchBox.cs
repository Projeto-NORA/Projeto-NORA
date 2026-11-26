using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Campo "Pesquisar" do cabeçalho para telas cuja pesquisa não é uma lista (o Painel da UBS filtra cards, gráficos e tabela): o mesmo desenho do campo das listagens
/// (estilo <c>SearchField</c>, lupa à direita), com largura preferida de 260 que encolhe até <see cref="FrameworkElement.MinWidth"/> antes de qualquer botão do cabeçalho sumir.
/// Vai dentro de um <see cref="PageFilterGroup"/> com <c>Inline="True"</c>.
/// </summary>
public sealed class HeaderSearchBox : TextBox
{
    private const double PreferredWidth = 260;

    public HeaderSearchBox()
    {
        MinWidth = 140;
        MaxWidth = PreferredWidth;
        Tag = "Pesquisar";
        VerticalAlignment = VerticalAlignment.Center;
        SetResourceReference(StyleProperty, "SearchField");
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        return new Size(double.IsInfinity(constraint.Width) ? PreferredWidth : Math.Min(constraint.Width, PreferredWidth), size.Height);
    }
}
