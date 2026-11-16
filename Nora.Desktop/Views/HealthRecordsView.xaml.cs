using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Aba "Exames e vacinas" da ficha do paciente. As ações que não têm volta (cancelar exame e anular registro) pedem confirmação.</summary>
public partial class HealthRecordsView : UserControl
{
    public HealthRecordsView() => InitializeComponent();
    private HealthRecordsViewModel? Health => DataContext as HealthRecordsViewModel;
    private async void OnRefresh(object sender, RoutedEventArgs e) { if (Health is { } vm) await vm.ReloadAsync(); }
    private async void OnCreateExam(object sender, RoutedEventArgs e) { if (Health is { } vm) await vm.CreateExamAsync(); }
    private async void OnExamDone(object sender, RoutedEventArgs e) { if (Health is { } vm) await vm.MarkExamDoneAsync(); }
    private async void OnCreateVaccine(object sender, RoutedEventArgs e) { if (Health is { } vm) await vm.CreateVaccineAsync(); }

    private async void OnExamCancel(object sender, RoutedEventArgs e)
    {
        if (Health is not { SelectedExam: { CanChangeStatus: true } exam } vm) return;
        if (!Confirmation.Ask(Window.GetWindow(this), "Cancelar exame", $"Cancelar o exame \"{exam.Name}\"? Um exame cancelado não volta a ficar solicitado nem pode ser marcado como realizado; se o registro estiver errado, use Anular.")) return;
        await vm.CancelExamAsync();
    }

    private async void OnVoid(object sender, RoutedEventArgs e)
    {
        if (Health is not { CanVoid: true } vm) return;
        // Com o motivo inválido o formulário mostra o erro; só um motivo válido chega à pergunta de confirmação.
        if (!vm.IsVoidReasonValid()) { await vm.VoidSelectedAsync(); return; }
        if (!Confirmation.Ask(Window.GetWindow(this), "Anular registro", $"Anular {vm.SelectedRecordName}?\n\nO registro deixa a lista (volta com \"Mostrar anulados\"), o motivo fica gravado com quem anulou e quando, e não dá para desfazer. O paciente deixa de vê-lo no aplicativo.")) return;
        await vm.VoidSelectedAsync();
    }
}
