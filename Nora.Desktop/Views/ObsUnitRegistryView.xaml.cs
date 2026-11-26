using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Pacientes (OBS): a listagem aberta com o balão de ações (Ver ficha, Editar ficha, Corrigir e-mail, Desativar ou Reativar) e o cadastro em grade.
/// Duplo clique, segundo clique e Enter abrem a ficha pelo <see cref="ListingBehavior"/>.
/// </summary>
public partial class ObsUnitRegistryView : UserControl
{
    /// <summary>Largura da lista abaixo da qual o balão não cabe com os rótulos (nome, CPF e quatro botões): os botões ficam só com o ícone (o nome e a dica continuam).</summary>
    private const double CompactBalloonWidth = 860;
    private readonly DispatcherTimer cepTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly (Button Button, string? Label)[] balloonLabels;
    private bool compactBalloon;
    public ObsUnitRegistryView()
    {
        InitializeComponent();
        cepTimer.Tick += OnCepTimer;
        Unloaded += (_, _) => cepTimer.Stop();
        balloonLabels = new[] { ViewButton, EditButton, EmailButton, DeactivateButton, ReactivateButton }.Select(button => (button, button.Content as string)).ToArray();
        // Desativar e Reativar se alternam conforme a situação do paciente selecionado (o balão avisa a cada mudança da seleção).
        ListingBehavior.AddBalloonRefreshedHandler(PatientList, OnBalloonRefreshed);
    }
    private WorkspaceViewModel? Workspace => DataContext as WorkspaceViewModel;

    /// <summary>Com a janela estreita (980) o balão com cinco rótulos passaria da lista e cortaria o X: os botões ficam só com o ícone.</summary>
    private void OnListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactBalloonWidth;
        if (compact == compactBalloon) return;
        compactBalloon = compact;
        foreach (var (button, label) in balloonLabels) button.Content = compact ? null : label;
    }
    private void OnCepChanged(object sender, TextChangedEventArgs e)
    {
        cepTimer.Stop();
        if (sender is TextBox { IsKeyboardFocusWithin: true } box && BrazilianDataValidator.IsCompleteCep(box.Text)) cepTimer.Start();
    }
    private async void OnCepTimer(object? sender, EventArgs e)
    {
        cepTimer.Stop();
        if (Workspace is not { } vm || !BrazilianDataValidator.IsCompleteCep(vm.ObsRegistryCep)) return;
        if (!vm.CanOperate) { cepTimer.Start(); return; }
        await vm.LookupObsRegistryCepAsync();
    }
    private async void OnCreate(object sender, RoutedEventArgs e) { if (Workspace is { } vm) await vm.CreateObsRegistryAsync(); }
    /// <summary>Descarta a edição em andamento: o formulário volta a ser um cadastro novo, em branco.</summary>
    private void OnCancelEdit(object sender, RoutedEventArgs e) => Workspace?.NewObsRegistryDraft();

    /// <summary>O paciente do balão: a ação vale para um item só, então é o único selecionado na lista (e o que o ViewModel já tem como selecionado).</summary>
    private UnitPatientRecord? SelectedPatient(WorkspaceViewModel vm)
    {
        var selected = ListingBehavior.GetSelectedItems<UnitPatientRecord>(PatientList);
        if (selected.Count != 1) return null;
        if (!ReferenceEquals(vm.SelectedObsRegistry, selected[0])) vm.SelectedObsRegistry = selected[0];
        return selected[0];
    }

    private void OnBalloonRefreshed(object sender, RoutedEventArgs e)
    {
        var selected = ListingBehavior.GetSelectedItems<UnitPatientRecord>(PatientList);
        if (selected.Count != 1) return;
        var active = selected[0].IsActive;
        DeactivateButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ReactivateButton.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Leva à subtela indicada (Cadastro ou Listagem) quando a tela está na janela principal.</summary>
    private void OpenSubPage(string route) { if (Window.GetWindow(this) is ShellWindow shell) shell.ViewModel.Navigate(route); }
    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || SelectedPatient(vm) is null) return;
        vm.EditSelectedObsRegistry();
        OpenSubPage("patients:register");
    }
    private void OnDetails(object sender, RoutedEventArgs e)
    {
        if (Workspace is { } vm && SelectedPatient(vm) is { } patient && Window.GetWindow(this) is ShellWindow shell) shell.OpenItemDetails(patient);
    }

    /// <summary>Corrigir só o e-mail: o pop-up traz o campo; a confirmação que já existia vem ao enviar, e o pop-up só fecha se a API aceitar.</summary>
    private void OnCorrectEmail(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || SelectedPatient(vm) is not { } patient) return;
        var form = new ObsEmailCorrectionForm { DataContext = vm };
        string? error = null;
        async Task<bool> Save()
        {
            error = null;
            if (string.IsNullOrWhiteSpace(vm.CorrectedObsEmail)) { error = "Informe o e-mail correto."; return false; }
            if (vm.CorrectedObsEmailError.Length > 0) { error = vm.CorrectedObsEmailError; return false; }
            if (!Confirmation.Ask(Window.GetWindow(form), "Corrigir e-mail", $"Corrigir o e-mail de {patient.FullName} nesta UBS?", "Corrigir", "Cancelar"))
            {
                error = "O e-mail não foi corrigido: a confirmação foi cancelada.";
                return false;
            }
            if (await vm.UpdateObsRegistryEmailAsync()) return true;
            error = vm.Message;
            return false;
        }
        DialogService.ShowForm(Window.GetWindow(this), "Corrigir e-mail", form, Save, "Corrigir e-mail", error: () => error);
    }

    /// <summary>Desativar (com a prévia de quantas consultas futuras serão canceladas) ou reativar o paciente selecionado, sempre com confirmação.</summary>
    private async void OnSetActive(object sender, RoutedEventArgs e)
    {
        if (Workspace is not { } vm || SelectedPatient(vm) is not { } patient) return;
        string description;
        // O que a pessoa vai confirmar é fixado antes da pergunta: a lista pode ser relida enquanto ela está aberta, e o botão não pode virar o contrário.
        var activate = !patient.IsActive;
        if (patient.IsActive)
        {
            // Desativar cancela as consultas futuras da UBS: a API diz quantas antes da pergunta. Sem esse número a confirmação não é feita.
            var preview = await vm.PreviewObsDeactivationAsync();
            if (preview is null || vm.SelectedObsRegistry?.Cpf != patient.Cpf) return;
            description = WorkspaceViewModel.DeactivationConfirmationText(patient.FullName, preview);
        }
        else description = $"Reativar {patient.FullName} nesta UBS? Entradas e consultas canceladas não serão restauradas.";
        if (Confirmation.Ask(Window.GetWindow(this), "Confirmar situação na UBS", description) && vm.SelectedObsRegistry?.Cpf == patient.Cpf)
            await vm.SetObsRegistryActiveAsync(patient.Cpf, activate);
    }
}
