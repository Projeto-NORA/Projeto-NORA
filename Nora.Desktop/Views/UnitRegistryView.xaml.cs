using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Cadastros de referência da Secretaria: pacientes e agenda da UBS escolhida, em listagens abertas com ficha em linhas. O cabeçalho alterna entre Cadastros e Agenda;
/// os filtros (UBS, período, situação) e os botões de consulta ficam no botão Filtros. A leitura nominal só roda quando a pessoa consulta (fica no histórico de acesso).
/// </summary>
public partial class UnitRegistryView : UserControl
{
    private static readonly string[] Modes = ["Cadastros", "Agenda"];

    public UnitRegistryView()
    {
        InitializeComponent();
        MunicipalDetailsHost.Attach(RegistryList, this);
        MunicipalDetailsHost.Attach(AgendaList, this);
        ModeToggle.ItemsSource = Modes;
        ModeToggle.SelectedIndex = 0;
    }

    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    /// <summary>A agenda está à vista (o padrão são os cadastros).</summary>
    internal bool ShowsAgenda => ModeToggle.SelectedIndex == 1;

    /// <summary>Mostra a listagem, a pesquisa e os filtros do modo escolhido; a seleção da outra listagem é desfeita para o balão dela sumir.</summary>
    private void OnModeChanged(object sender, SelectionChangedEventArgs args)
    {
        var agenda = ShowsAgenda;
        if (agenda) ListingBehavior.ClearSelection(RegistryList); else ListingBehavior.ClearSelection(AgendaList);
        // O texto de estado vazio de cada listagem some junto com ela (ListEmptyText acompanha a visibilidade da lista).
        RegistryList.Visibility = agenda ? Visibility.Collapsed : Visibility.Visible;
        AgendaList.Visibility = agenda ? Visibility.Visible : Visibility.Collapsed;
        RegistrySearch.Visibility = RegistryFilters.Visibility = agenda ? Visibility.Collapsed : Visibility.Visible;
        AgendaSearch.Visibility = AgendaFilters.Visibility = agenda ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnReadRegistry(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadUnitRegistryAsync(); }
    private async void OnReadAppointments(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadMunicipalAppointmentsAsync(); }
    private async void OnViewRegistry(object sender, RoutedEventArgs e) { if (ListingBehavior.GetSelectedItems<UnitPatientRecord>(RegistryList).FirstOrDefault() is { } item) await MunicipalDetailsHost.OpenAsync(this, item); }
    private async void OnViewAppointment(object sender, RoutedEventArgs e) { if (ListingBehavior.GetSelectedItems<AppointmentInfo>(AgendaList).FirstOrDefault() is { } item) await MunicipalDetailsHost.OpenAsync(this, item); }
}
