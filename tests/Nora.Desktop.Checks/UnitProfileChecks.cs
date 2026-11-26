using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Atendimento ao público da UBS (OBS, Informações da UBS): horário de funcionamento por dia (um intervalo, caixa marcada = aberto) e serviços oferecidos.</summary>
internal static partial class Program
{
    private const string NotInformedNotice = "Ainda não informado. O aplicativo mostra \"Horário não informado\".";

    private static void RunUnitProfileChecks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Atendimento ao público: sem card, título e \"Fechado\"; aviso amarelo de não informado; caixa marcada abre os campos \"às\"; o administrador edita e salva tudo de uma vez com validação por campo", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Atendimento", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            fixture.AddScheduleRow(unit.Id, ana, room, cardio.Id);
            var window = OpenOperational(fixture, storageRoot, "profile-admin", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:profile", 1280, 900);
                Require(vm.CanEditProfile && vm.OpeningDays.Count == 7 && vm.OpeningDays.All(day => day.IsClosed && !day.IsOpen) && vm.ProfileNotInformedNotice == NotInformedNotice && vm.ProfileState == "", "Sem nada gravado o aviso deveria dizer que ainda não foi informado: " + vm.ProfileNotInformedNotice);
                Require(vm.OpeningDays.Select(day => day.Label).SequenceEqual(["Domingo", "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado"]), "Os sete dias deveriam começar no domingo.");
                // Tela: aviso amarelo no topo, uma caixa por dia, sem os campos de dia fechado, sem título, sem "Fechado" e sem a lista de especialidades da grade.
                var texts = FullTexts(content);
                var notice = Descendants<ContentControl>(content).Single(item => AutomationProperties.GetName(item) == "Horário ainda não informado");
                Require(notice.IsVisible && texts.Contains(NotInformedNotice) && notice.TranslatePoint(new Point(0, 0), (FrameworkElement)window.FindName("PageContainer")).Y < 12,"O aviso amarelo deveria estar no topo da tela.");
                var dayBoxes = Descendants<CheckBox>(content).Where(box => box.IsVisible && vm.OpeningDays.Any(day => day.Label == AutomationProperties.GetName(box))).ToArray();
                Require(dayBoxes.Length == 7 && dayBoxes.All(box => box.IsChecked == false), "Deveria haver uma caixa por dia, todas desmarcadas (dia fechado).");
                Require(!Descendants<TextBox>(content).Any(box => box.IsVisible && (AutomationProperties.GetName(box) ?? "").StartsWith("Abre", StringComparison.Ordinal)), "Dia fechado não mostra campos de horário.");
                Require(!texts.Any(text => text is "Fechado" or "Atendimento ao público" or "Especialidades com horário na grade" || text.StartsWith("Vem da grade", StringComparison.Ordinal) || text.StartsWith("Abre", StringComparison.Ordinal) || text.StartsWith("e Abre", StringComparison.Ordinal)), "A tela não deveria ter \"Fechado\", título, especialidades da grade nem rótulos do segundo intervalo: " + string.Join(" | ", texts));
                Require(!Descendants<Border>(content).Any(border => border.IsVisible && border.CornerRadius.TopLeft >= 12 && border.Background is System.Windows.Media.SolidColorBrush brush && brush.Color == ThemeBrush(Application.Current, "SurfaceBrush").Color && border.ActualWidth > 300), "Não deveria haver card em volta do atendimento.");
                Require(!Descendants<Button>(content).Any(button => button.IsVisible && (AutomationProperties.GetName(button) ?? "").StartsWith("Atualizar", StringComparison.Ordinal)), "Não deveria haver botão Atualizar.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-nao-informado-light.png"), 1280, 900);
                // Marcar o dia abre os dois campos com "às" entre eles, mais baixos que o campo padrão.
                var monday = vm.OpeningDays[1]; var tuesday = vm.OpeningDays[2]; var wednesday = vm.OpeningDays[3];
                dayBoxes.Single(box => AutomationProperties.GetName(box) == "Segunda-feira").IsChecked = true;
                content = Layout(window, 1280, 900);
                Require(monday.IsOpen && !monday.IsClosed && monday.HasIntervals, "Marcar a caixa deveria abrir o dia.");
                var opens = Input(content, "Abre Segunda-feira"); var closes = Input(content, "Fecha Segunda-feira");
                var plain = new TextBox { Text = "08:00" }; var probe = new Window { Content = plain, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0, SizeToContent = SizeToContent.WidthAndHeight }; probe.Show(); probe.UpdateLayout();
                var standardHeight = plain.ActualHeight; probe.Close();
                Require(opens.IsVisible && closes.IsVisible && opens.ActualHeight > 0 && standardHeight / opens.ActualHeight >= 1.15 && standardHeight / opens.ActualHeight <= 1.4, $"Os campos de horário deveriam ser cerca de 1,2 vez mais baixos que o padrão (campo {opens.ActualHeight:F1}, padrão {standardHeight:F1}).");
                Require(FullTexts(content).Count(text => text == "às") == 1 && opens.TranslatePoint(new Point(0, 0), content).X < closes.TranslatePoint(new Point(0, 0), content).X, "Deveria haver a palavra \"às\" entre os dois campos.");
                // Validação por campo, sem enviar nada.
                opens.Text = "07:00"; closes.Text = "11:30";
                tuesday.IsClosed = false; tuesday.Opens = "25:00"; tuesday.Closes = "11:00";
                Pump(vm.SaveProfileAsync());
                Require(fixture.ProfileBodies.Count == 0 && tuesday.Error.StartsWith("Terça-feira:", StringComparison.Ordinal) && tuesday.Error.Contains("HH:mm", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Error, "Horário 25:00 deveria apontar o campo da terça e não enviar nada: " + tuesday.Error);
                tuesday.Opens = "10:00"; tuesday.Closes = "10:00"; Pump(vm.SaveProfileAsync());
                Require(tuesday.Error.Contains("depois do de abrir", StringComparison.Ordinal) && fixture.ProfileBodies.Count == 0, "Fechar igual a abrir deveria ser recusado: " + tuesday.Error);
                tuesday.Opens = "08:00"; tuesday.Closes = "18:00";
                wednesday.IsClosed = false; Pump(vm.SaveProfileAsync());
                Require(wednesday.Error.Contains("ou desmarque o dia", StringComparison.Ordinal) && fixture.ProfileBodies.Count == 0, "Dia marcado sem horários deveria ser recusado: " + wednesday.Error);
                wednesday.IsClosed = true;
                vm.UnitServices = new string('x', 1001); Pump(vm.SaveProfileAsync());
                Require(vm.ServicesError.Contains("até 1.000 caracteres", StringComparison.Ordinal) && vm.ServicesCounter == "1001/1000" && fixture.ProfileBodies.Count == 0, "Serviços com 1.001 caracteres deveriam ser recusados: " + vm.ServicesError);
                vm.UnitServices = "Consulta\tcom tab"; Pump(vm.SaveProfileAsync());
                Require(vm.ServicesError.Contains("caractere de controle", StringComparison.Ordinal) && fixture.ProfileBodies.Count == 0, "Caractere de controle deveria ser recusado: " + vm.ServicesError);
                // Cada validação recalcula todos os dias: para o erro do dia aparecer na tela, o dia inválido precisa estar na última tentativa.
                wednesday.IsClosed = false; Pump(vm.SaveProfileAsync());
                content = Layout(window, 1280, 900);
                Require(!string.IsNullOrEmpty(wednesday.Error) && FullTexts(content).Any(text => text.StartsWith("Quarta-feira:", StringComparison.Ordinal)), "O erro do dia deveria aparecer na tela, junto do campo: " + wednesday.Error);
                wednesday.IsClosed = true;
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-erros-light.png"), 1280, 900);
                // Salvar: texto com quebra de linha é aceito e devolvido como veio; um intervalo por dia.
                vm.UnitServices = "Vacinação\nCurativos\r\nPré-natal  "; content = Layout(window, 1280, 900);
                var save = ButtonIn(content, "Salvar");
                var servicesBox = Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Serviços oferecidos pela UBS");
                Require(save.IsEnabled && save.TranslatePoint(new Point(save.ActualWidth, 0), content).X > content.ActualWidth - 80 && save.TranslatePoint(new Point(0, 0), content).Y > servicesBox.TranslatePoint(new Point(0, servicesBox.ActualHeight), content).Y, "O botão Salvar deveria ficar embaixo, à direita, depois dos serviços.");
                InvokeButton(save); Settle(vm);
                var saved = fixture.ProfileOf(unit.Id) ?? throw new InvalidOperationException("O atendimento deveria estar gravado: " + vm.Message);
                Require(saved.Informed && saved.Services == "Vacinação\nCurativos\nPré-natal" && saved.OpeningHours.Count(item => item.Weekday == 1) == 1 && saved.OpeningHours.Any(item => item is { Weekday: 1, Opens: "07:00", Closes: "11:30" }) && saved.OpeningHours.Any(item => item is { Weekday: 2, Opens: "08:00", Closes: "18:00" }) && saved.OpeningHours.Length == 2, "O que ficou gravado não confere com o editado.");
                Require(vm.MessageKind == MessageKind.Success && vm.ProfileSteps.Single() is { Text: "Salvando horários…", IsDone: true } && vm.ProfileNotInformedNotice == "" && vm.ProfileUpdatedLabel.StartsWith("Última alteração em ", StringComparison.Ordinal) && vm.OpeningDays[1].IsOpen && vm.OpeningDays[0].IsClosed, "Depois de salvar o passo fica concluído, o aviso some e a tela reflete o gravado.");
                content = Layout(window, 1280, 900);
                Require(!FullTexts(content).Contains(NotInformedNotice) && !Descendants<ContentControl>(content).Single(item => AutomationProperties.GetName(item) == "Horário ainda não informado").IsVisible, "O aviso amarelo deveria sumir depois de salvar.");
                // Todos os dias fechados (UBS fechada) é válido e deixa de ser "não informado".
                foreach (var day in vm.OpeningDays) day.IsClosed = true;
                Pump(vm.SaveProfileAsync());
                Require(fixture.ProfileOf(unit.Id)!.OpeningHours.Length == 0 && fixture.ProfileOf(unit.Id)!.Informed && vm.ProfileNotInformedNotice == "", "Todos os dias fechados é válido e deixa de ser 'não informado'.");
                vm.OpeningDays[1].IsOpen = true; vm.OpeningDays[1].Opens = "07:30"; vm.OpeningDays[1].Closes = "12:00"; vm.OpeningDays[5].IsOpen = true; vm.OpeningDays[5].Opens = "08:00"; vm.OpeningDays[5].Closes = "16:00";
                Pump(vm.SaveProfileAsync()); content = Layout(window, 1280, 900);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 900); SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light);
                // Uma edição em andamento não é apagada pela leitura automática.
                vm.OpeningDays[4].IsClosed = false; vm.OpeningDays[4].Opens = "09:00";
                Pump(vm.RefreshAutomaticallyAsync());
                Require(!vm.OpeningDays[4].IsClosed && vm.OpeningDays[4].Opens == "09:00", "A leitura automática não pode apagar a edição em andamento.");
            }
            finally { CloseWindow(window); }
        });

        Check("Atendimento ao público: dia gravado com dois intervalos mostra o primeiro início e o último fim, avisa do intervalo do meio e salva um só", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Dois Intervalos", "Centro");
            using (var seed = new NoraApiClient(fixture))
            {
                seed.Configure("https://fixture.invalid");
                Task.Run(() => seed.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password")).GetAwaiter().GetResult();
                Task.Run(() => seed.SaveUnitProfileAsync(unit.Id, new UnitProfileDraft([new(2, "07:00", "11:00"), new(2, "13:00", "17:30"), new(3, "08:00", "12:00")], null))).GetAwaiter().GetResult();
            }
            var window = OpenOperational(fixture, storageRoot, "profile-gap", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:profile", 1280, 900);
                var tuesday = vm.OpeningDays[2]; var wednesday = vm.OpeningDays[3];
                Require(tuesday.IsOpen && tuesday.Opens == "07:00" && tuesday.Closes == "17:30" && tuesday.GapNotice.Contains("intervalo do meio é desconsiderado", StringComparison.Ordinal) && wednesday.GapNotice == "" && vm.ProfileNotInformedNotice == "", "O dia com dois intervalos deveria mostrar o primeiro início e o último fim e avisar do meio: " + tuesday.GapNotice);
                Require(FullTexts(content).Contains(tuesday.GapNotice) && !FullTexts(content).Contains(wednesday.Label + ": gap"), "O aviso discreto deveria aparecer na linha do dia.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-intervalo-do-meio-light.png"), 1280, 900);
                tuesday.IsClosed = true; Require(tuesday.GapNotice == "", "Dia fechado não mostra o aviso do meio."); tuesday.IsClosed = false;
                Pump(vm.SaveProfileAsync());
                var saved = fixture.ProfileOf(unit.Id)!;
                Require(saved.OpeningHours.Count(item => item.Weekday == 2) == 1 && saved.OpeningHours.Any(item => item is { Weekday: 2, Opens: "07:00", Closes: "17:30" }) && tuesday.GapNotice == "", "Salvar deveria gravar um só intervalo, do primeiro início ao último fim.");
            }
            finally { CloseWindow(window); }
        });

        Check("Atendimento ao público: a atendente vê em leitura, com o aviso; a falha de leitura diz que a tela tenta de novo sozinha e a leitura seguinte a refaz (sem botão Atualizar)", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant" };
            var unit = fixture.AddUnit("UBS Leitura", "Centro");
            fixture.FailProfile = true;
            var window = OpenOperational(fixture, storageRoot, "profile-attendant", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:profile", 1280, 900);
                Require(!vm.CanEditProfile && vm.ShowProfileReadOnlyNotice && vm.ProfileReadOnlyNotice.StartsWith("Só o administrador da UBS altera o atendimento ao público.", StringComparison.Ordinal) && FullTexts(content).Contains(vm.ProfileReadOnlyNotice), "A atendente deveria ver o aviso de somente leitura.");
                Require(vm.ProfileState == "Não foi possível ler o atendimento da UBS agora. A tela tenta de novo sozinha." && !vm.CanSaveProfile && FullTexts(content).Contains(vm.ProfileState) && !Descendants<Button>(content).Any(button => button.IsVisible && (AutomationProperties.GetName(button) ?? "").StartsWith("Atualizar", StringComparison.Ordinal)), "A falha deveria dizer que a tela tenta de novo sozinha, sem botão: " + vm.ProfileState);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-falha-light.png"), 1280, 900);
                fixture.FailProfile = false; var reads = fixture.Count("GET", "/profile");
                Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 900);
                Require(fixture.Count("GET", "/profile") == reads + 1 && vm.ProfileState == "" && vm.ProfileNotInformedNotice == NotInformedNotice, "A leitura automática seguinte deveria refazer a leitura do atendimento: " + vm.ProfileState);
                var days = Descendants<ItemsControl>(content).Single(items => AutomationProperties.GetName(items) == "Horário de funcionamento por dia da semana");
                var save = ButtonIn(content, "Salvar");
                Require(!days.IsEnabled && !save.IsEnabled && !Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Serviços oferecidos pela UBS").IsEnabled, "Para a atendente os campos e o botão salvar ficam desabilitados.");
                // Se a atendente tentasse gravar, a API responde 403 com a mensagem própria.
                using var api = new NoraApiClient(fixture); api.Configure("https://fixture.invalid");
                Task.Run(() => api.LoginAsync("obs", "fixture@example.invalid", "isolated-test-password")).GetAwaiter().GetResult();
                try { Task.Run(() => api.SaveUnitProfileAsync(unit.Id, new UnitProfileDraft([], null))).GetAwaiter().GetResult(); throw new InvalidOperationException("A atendente não deveria gravar."); }
                catch (NoraApiException error) { Require(error.Status == 403 && error.Message.StartsWith("Somente o administrador da UBS altera", StringComparison.Ordinal), "Mensagem de permissão errada: " + error.Message); }
            }
            finally { CloseWindow(window); }
        });
    }
}
