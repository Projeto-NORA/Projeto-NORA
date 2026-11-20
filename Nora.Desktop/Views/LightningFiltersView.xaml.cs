using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Filtros de Vaga Relâmpago e Relatórios: período das vagas (coortes do resumo) e UBS; região e especialidade não existem para esses números.</summary>
public partial class LightningFiltersView : UserControl
{
    /// <summary>Título do cartão: cada aba que usa a view diz a que os filtros se aplicam (vagas, ou o resumo exportado em Relatórios).</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(LightningFiltersView), new PropertyMetadata("Filtros das vagas"));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public LightningFiltersView() => InitializeComponent();
}
