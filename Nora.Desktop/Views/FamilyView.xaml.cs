using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Pedidos de família da UBS: aprovar (lembrando de conferir os documentos), recusar (motivo opcional) e encerrar vínculos aprovados. Toda decisão pede confirmação.</summary>
public partial class FamilyView : UserControl
{
    public FamilyView() => InitializeComponent();
    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.LoadFamilyAsync(); }

    private async void OnApprove(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedPendingFamily: { } link, CanDecideFamily: true } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Aprovar vínculo de família",
            $"Aprovar o vínculo entre {link.GuardianName} (CPF {link.GuardianCpfDisplay}) e {link.DependentDisplay} (CPF {link.DependentCpfDisplay})?\n\nConfira antes os documentos do responsável e do dependente: o sistema não confere idade nem parentesco.\n\nDepois de aprovado, o responsável poderá agendar e cancelar consultas, confirmar presença, ver a fila e sair dela, responder ofertas e ver exames e vacinas do dependente pelo aplicativo."))
            return;
        await vm.ApproveFamilyAsync();
    }

    private async void OnReject(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedPendingFamily: { } link, CanDecideFamily: true } vm) return;
        var reason = string.IsNullOrWhiteSpace(vm.FamilyRejectReason) ? "Nenhum motivo será informado." : "O motivo informado será mostrado ao responsável.";
        if (!Confirmation.Ask(Window.GetWindow(this), "Recusar pedido de família",
            $"Recusar o pedido de {link.GuardianName} para {link.DependentDisplay}?\n\n{reason} O responsável poderá pedir de novo depois."))
            return;
        await vm.RejectFamilyAsync();
    }

    private async void OnEnd(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedApprovedFamily: { } link, CanEndFamily: true } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Encerrar vínculo de família",
            $"Encerrar o vínculo de {link.GuardianName} com {link.DependentDisplay}?\n\nO responsável perde na hora o acesso ao dependente nesta UBS. As consultas já marcadas continuam e, para voltar, será preciso um novo pedido."))
            return;
        await vm.EndFamilyAsync();
    }
}
