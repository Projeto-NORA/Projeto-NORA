using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Filtros da Vaga Relâmpago da rede (Secretaria): período da vaga e UBS. Entra na Vaga Relâmpago e em Relatórios e metas.</summary>
public partial class LightningFiltersView : UserControl
{
    private bool periodInline = true, showUnit = true, showNotice = true;

    public LightningFiltersView() => InitializeComponent();

    /// <summary>O período da vaga fica em linha no cabeçalho (padrão); desligado, vai para o botão Filtros.</summary>
    public bool PeriodInline
    {
        get => periodInline;
        set { periodInline = value; PeriodGroup.Visibility = value ? Visibility.Visible : Visibility.Collapsed; PopupPeriod.Visibility = value ? Visibility.Collapsed : Visibility.Visible; }
    }

    /// <summary>Mostra a escolha de UBS (a tela que já tem esse filtro em outro grupo desliga, para não repetir).</summary>
    public bool ShowUnit
    {
        get => showUnit;
        set { showUnit = value; UnitPicker.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    }

    /// <summary>Mostra o aviso de UBS sem base (a tela que já o mostra em outro lugar desliga).</summary>
    public bool ShowNotice
    {
        get => showNotice;
        set { showNotice = value; if (!value) Notice.Visibility = Visibility.Collapsed; } // ligado, a visibilidade segue a ligação com HasUnitsWithoutDatabase
    }
}
