using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Pedidos de família da UBS (aprovar, e recusar num pop-up com o motivo opcional) e vínculos aprovados (encerrar). As ações ficam no balão da listagem.
/// Aprovar e encerrar pedem confirmação; recusar é confirmada pelo próprio pop-up (o botão Enviar).
/// </summary>
public partial class FamilyView : UserControl
{
    public FamilyView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Um só registro por lista, mesmo que a tela seja carregada de novo sem ter sido descarregada antes.
        OnUnloaded(sender, e);
        ((INotifyCollectionChanged)PendingList.Items).CollectionChanged += OnListChanged;
        ((INotifyCollectionChanged)ApprovedList.Items).CollectionChanged += OnListChanged;
        UpdateSearchEmpty();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)PendingList.Items).CollectionChanged -= OnListChanged;
        ((INotifyCollectionChanged)ApprovedList.Items).CollectionChanged -= OnListChanged;
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSearchEmpty();

    /// <summary>A pesquisa do cabeçalho esvaziou a lista que tinha itens: diz que nada foi encontrado (o aviso de lista vazia de verdade vem do ViewModel).</summary>
    private void UpdateSearchEmpty()
    {
        PendingSearchEmpty.Visibility = PendingList.Items.Count == 0 && PendingList.ItemsSource is ICollection<FamilyLink> { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        ApprovedSearchEmpty.Visibility = ApprovedList.Items.Count == 0 && ApprovedList.ItemsSource is ICollection<FamilyLink> { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>O pedido do balão: a ação vale para um item só, então é o único selecionado (e o que o ViewModel já tem como selecionado).</summary>
    private FamilyLink? SelectedLink(WorkspaceViewModel vm, ListBox list, bool pending)
    {
        var selected = ListingBehavior.GetSelectedItems<FamilyLink>(list);
        if (selected.Count != 1) return null;
        if (pending) { if (!ReferenceEquals(vm.SelectedPendingFamily, selected[0])) vm.SelectedPendingFamily = selected[0]; }
        else if (!ReferenceEquals(vm.SelectedApprovedFamily, selected[0])) vm.SelectedApprovedFamily = selected[0];
        return selected[0];
    }

    private async void OnApprove(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanDecideFamily: true } vm || SelectedLink(vm, PendingList, pending: true) is not { } link) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Aprovar vínculo de família",
            $"Aprovar o vínculo entre {link.GuardianName} (CPF {link.GuardianCpfDisplay}) e {link.DependentDisplay} (CPF {link.DependentCpfDisplay})?\n\nConfira antes os documentos do responsável e do dependente: o sistema não confere idade nem parentesco.\n\nDepois de aprovado, o responsável poderá agendar e cancelar consultas, confirmar presença, ver a fila e sair dela, responder ofertas e ver exames e vacinas do dependente pelo aplicativo."))
            return;
        await vm.ApproveFamilyAsync();
    }

    /// <summary>Recusar abre o pop-up com o motivo opcional; Enviar recusa o pedido e o pop-up só fecha se a API aceitar. Fechar (ou o X e o Esc) desiste sem recusar.</summary>
    private void OnReject(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanDecideFamily: true } vm || SelectedLink(vm, PendingList, pending: true) is not { } link) return;
        vm.FamilyRejectReason = "";
        var form = new FamilyRejectForm($"Recusar o pedido de {link.GuardianName} para {link.DependentDisplay}. O responsável poderá pedir de novo depois.") { DataContext = vm };
        // "Fechar": o pop-up não fecha durante uma ação do botão, então o fechamento espera a ação terminar.
        Task Close()
        {
            form.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => Window.GetWindow(form)?.Close());
            return Task.CompletedTask;
        }
        DialogService.ShowForm(Window.GetWindow(this), "Recusar pedido", form, vm.RejectFamilyAsync, "Enviar", "Fechar", Close, () => vm.Message);
        vm.FamilyRejectReason = "";
    }

    private async void OnEnd(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { CanEndFamily: true } vm || SelectedLink(vm, ApprovedList, pending: false) is not { } link) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Encerrar vínculo de família",
            $"Encerrar o vínculo de {link.GuardianName} com {link.DependentDisplay}?\n\nO responsável perde na hora o acesso ao dependente nesta UBS. As consultas já marcadas continuam e, para voltar, será preciso um novo pedido."))
            return;
        await vm.EndFamilyAsync();
    }
}
