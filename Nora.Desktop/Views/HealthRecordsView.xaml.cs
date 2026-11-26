using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Aba "Exames e vacinas" da ficha do paciente: duas listas abertas com balão de ações e, no lugar delas, o formulário aberto por "Novo exame", "Nova vacina",
/// "Marcar como realizado" ou "Anular" (a ficha já é um pop-up, então só há um por vez). As ações que não têm volta (cancelar exame e anular registro) pedem
/// confirmação. A aba lê ao abrir, a cada 20 s enquanto está à vista e depois de cada ação; não há botão Atualizar.
/// </summary>
public partial class HealthRecordsView : UserControl
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(20);
    private readonly DispatcherTimer refresh;

    public HealthRecordsView()
    {
        InitializeComponent();
        refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
        refresh.Tick += async (_, _) => { if (IsVisible && Health is { } vm) await vm.AutoRefreshAsync(); };
        ListingBehavior.AddBalloonRefreshedHandler(ExamList, OnExamBalloonRefreshed);
        ListingBehavior.AddBalloonRefreshedHandler(VaccineList, OnVaccineBalloonRefreshed);
        // A ficha de um registro (duplo clique, segundo clique, Enter) abre no lugar das listas, uma de cada vez.
        ListingBehavior.SetOpenHandler(ExamList, OpenRecord);
        ListingBehavior.SetOpenHandler(VaccineList, OpenRecord);
    }

    private HealthRecordsViewModel? Health => DataContext as HealthRecordsViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e) => refresh.Start();
    private void OnUnloaded(object sender, RoutedEventArgs e) => refresh.Stop();

    private Task OpenRecord(object item) => Health?.OpenDetailAsync(item) ?? Task.CompletedTask;

    /// <summary>Esc volta às listas quando um formulário ou a ficha do registro está aberto; só depois disso fecha a ficha do paciente.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Health is not { Panel: not HealthPanel.Lists } vm) return;
        vm.CloseForm();
        e.Handled = true;
    }

    // ----- balão: o que cada ação mostra depende do registro selecionado -----

    private void OnExamBalloonRefreshed(object sender, RoutedEventArgs e)
    {
        var selected = ListingBehavior.GetSelectedItems<ExamInfo>(ExamList);
        var exam = selected.Count == 1 ? selected[0] : null;
        var canWrite = Health?.CanRegister == true;
        ExamDoneButton.Visibility = canWrite && exam?.CanChangeStatus == true ? Visibility.Visible : Visibility.Collapsed;
        ExamCancelButton.Visibility = canWrite && exam?.CanChangeStatus == true ? Visibility.Visible : Visibility.Collapsed;
        ExamVoidButton.Visibility = canWrite && exam is { IsVoided: false } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnVaccineBalloonRefreshed(object sender, RoutedEventArgs e)
    {
        var selected = ListingBehavior.GetSelectedItems<VaccineInfo>(VaccineList);
        VaccineVoidButton.Visibility = Health?.CanRegister == true && selected is [{ IsVoided: false }] ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>O registro único selecionado na lista (a ação do balão só vale para um).</summary>
    private T? SingleSelected<T>(ListBox list) where T : class => ListingBehavior.GetSelectedItems<T>(list) is [var only] ? only : null;

    private void OnExamDoneAction(object sender, RoutedEventArgs e)
    {
        if (Health is not { } vm || SingleSelected<ExamInfo>(ExamList) is not { } exam) return;
        vm.SelectedExam = exam;
        vm.OpenForm(HealthPanel.ExamDone);
    }

    private async void OnExamCancelAction(object sender, RoutedEventArgs e)
    {
        if (Health is not { } vm || SingleSelected<ExamInfo>(ExamList) is not { CanChangeStatus: true } exam) return;
        vm.SelectedExam = exam;
        if (!Confirmation.Ask(Window.GetWindow(this), "Cancelar exame", $"Cancelar o exame \"{exam.Name}\"? Um exame cancelado não volta a ficar solicitado nem pode ser marcado como realizado; se o registro estiver errado, use Anular.")) return;
        await vm.CancelExamAsync();
    }

    /// <summary>Anular vale para um exame ou uma vacina: o balão da lista de onde veio o clique diz qual.</summary>
    private void OnVoidAction(object sender, RoutedEventArgs e)
    {
        if (Health is not { } vm) return;
        if (ReferenceEquals(sender, ExamVoidButton) && SingleSelected<ExamInfo>(ExamList) is { } exam) vm.SelectedExam = exam;
        else if (ReferenceEquals(sender, VaccineVoidButton) && SingleSelected<VaccineInfo>(VaccineList) is { } vaccine) vm.SelectedVaccine = vaccine;
        else return;
        vm.OpenForm(HealthPanel.Void);
    }

    // ----- cabeçalho: os formulários novos -----

    private void OnNewExam(object sender, RoutedEventArgs e) => Health?.OpenForm(HealthPanel.NewExam);
    private void OnNewVaccine(object sender, RoutedEventArgs e) => Health?.OpenForm(HealthPanel.NewVaccine);
    private void OnCancelForm(object sender, RoutedEventArgs e) => Health?.CloseForm();

    // ----- botões primários dos formulários -----

    private async void OnCreateExam(object sender, RoutedEventArgs e) { if (Health is { } vm) await vm.CreateExamAsync(); }
    private async void OnExamDone(object sender, RoutedEventArgs e) { if (Health is { } vm) await vm.MarkExamDoneAsync(); }
    private async void OnCreateVaccine(object sender, RoutedEventArgs e) { if (Health is { } vm) await vm.CreateVaccineAsync(); }

    private async void OnVoid(object sender, RoutedEventArgs e)
    {
        if (Health is not { CanVoid: true } vm) return;
        // Com o motivo inválido o formulário mostra o erro; só um motivo válido chega à pergunta de confirmação.
        if (!vm.IsVoidReasonValid()) { await vm.VoidSelectedAsync(); return; }
        if (!Confirmation.Ask(Window.GetWindow(this), "Anular registro", $"Anular {vm.SelectedRecordName}?\n\nO registro deixa a lista (volta com \"Mostrar anulados\"), o motivo fica gravado com quem anulou e quando, e não dá para desfazer. O paciente deixa de vê-lo no aplicativo.")) return;
        await vm.VoidSelectedAsync();
    }
}
