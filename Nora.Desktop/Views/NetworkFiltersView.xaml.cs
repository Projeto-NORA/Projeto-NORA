using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Filtros dos indicadores da rede no cabeçalho da tela: o Período em linha (ou no botão Filtros, com <see cref="PeriodInline"/> desligado) e, no botão
/// Filtros, UBS, região, especialidade e as datas do período personalizado. Só entra onde os filtros têm efeito.
/// </summary>
public partial class NetworkFiltersView : UserControl
{
    private bool allowCustom, periodInline = true;

    public NetworkFiltersView() => InitializeComponent();

    /// <summary>Oferece "Personalizado" no Período (a tela que mostra as datas). Nas outras, a opção só aparece enquanto o período personalizado está valendo.</summary>
    public bool AllowCustom
    {
        get => allowCustom;
        set { allowCustom = value; PeriodBox.Tag = PopupPeriodBox.Tag = value ? "custom" : "fixed"; }
    }

    /// <summary>O Período fica em linha no cabeçalho (padrão); desligado, vai para o botão Filtros (telas com muitos controles no cabeçalho).</summary>
    public bool PeriodInline
    {
        get => periodInline;
        set { periodInline = value; PeriodGroup.Visibility = value ? Visibility.Visible : Visibility.Collapsed; PopupPeriod.Visibility = value ? Visibility.Collapsed : Visibility.Visible; }
    }

    private void OnApplyPeriod(object sender, RoutedEventArgs e) { if (DataContext is WorkspaceViewModel vm) vm.ApplyCustomPeriod(); }
}
