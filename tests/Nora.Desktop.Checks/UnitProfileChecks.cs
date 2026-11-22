using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Atendimento ao público da UBS (OBS, Configurações): horário de funcionamento por dia, serviços e especialidades da grade.</summary>
internal static partial class Program
{
    private static void RunUnitProfileChecks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Atendimento ao público: o administrador edita os horários por dia e os serviços, com validação por campo, e salva tudo de uma vez", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Atendimento", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); var pediatria = fixture.AddSpecialty("Pediatria"); var inativa = fixture.AddSpecialty("Dermatologia", active: false);
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id, pediatria.Id, inativa.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            fixture.AddScheduleRow(unit.Id, ana, room, cardio.Id); fixture.AddScheduleRow(unit.Id, ana, room, inativa.Id, weekday: 2);
            var window = OpenOperational(fixture, storageRoot, "profile-admin", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 5200);
                Require(vm.CanEditProfile && vm.OpeningDays.Count == 7 && vm.OpeningDays.All(day => day.IsClosed) && vm.ProfileState == "Ainda não informado. O aplicativo mostra \"Horário não informado\".", "Sem nada gravado o estado deveria dizer que ainda não foi informado: " + vm.ProfileState);
                Require(vm.ProfileSpecialtiesText == "Cardiologia" && vm.ProfileSpecialtiesHint == "Vem da grade; para mudar, edite a grade." && FullTexts(content).Contains("Vem da grade; para mudar, edite a grade."), "As especialidades da grade (só as ativas) deveriam aparecer em leitura: " + vm.ProfileSpecialtiesText);
                Require(vm.OpeningDays.Select(day => day.Label).SequenceEqual(["Domingo", "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado"]), "Os sete dias deveriam começar no domingo.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-nao-informado-light.png"), 1280, 900);
                // Validação por campo, sem enviar nada.
                var monday = vm.OpeningDays[1]; var tuesday = vm.OpeningDays[2]; var wednesday = vm.OpeningDays[3];
                monday.IsClosed = false; monday.Opens1 = "07:00"; monday.Closes1 = "11:30"; monday.Opens2 = "13:00"; monday.Closes2 = "17:00";
                tuesday.IsClosed = false; tuesday.Opens1 = "25:00"; tuesday.Closes1 = "11:00";
                Pump(vm.SaveProfileAsync());
                Require(fixture.ProfileBodies.Count == 0 && tuesday.Error.StartsWith("Terça-feira:", StringComparison.Ordinal) && tuesday.Error.Contains("HH:mm", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Error, "Horário 25:00 deveria apontar o campo da terça e não enviar nada: " + tuesday.Error);
                tuesday.Opens1 = "10:00"; tuesday.Closes1 = "10:00"; Pump(vm.SaveProfileAsync());
                Require(tuesday.Error.Contains("depois do de abrir", StringComparison.Ordinal) && fixture.ProfileBodies.Count == 0, "Fechar igual a abrir deveria ser recusado: " + tuesday.Error);
                tuesday.Opens1 = "08:00"; tuesday.Closes1 = "12:00"; tuesday.Opens2 = "11:00"; tuesday.Closes2 = "15:00"; Pump(vm.SaveProfileAsync());
                Require(tuesday.Error.Contains("se sobrepor", StringComparison.Ordinal) && fixture.ProfileBodies.Count == 0, "Intervalos que se sobrepõem deveriam ser recusados: " + tuesday.Error);
                tuesday.Opens2 = "12:00"; tuesday.Closes2 = "18:00";
                wednesday.IsClosed = false; Pump(vm.SaveProfileAsync());
                Require(wednesday.Error.Contains("ao menos um intervalo ou marque Fechado", StringComparison.Ordinal) && fixture.ProfileBodies.Count == 0, "Dia aberto sem intervalo deveria ser recusado: " + wednesday.Error);
                wednesday.IsClosed = true;
                vm.UnitServices = new string('x', 1001); Pump(vm.SaveProfileAsync());
                Require(vm.ServicesError.Contains("até 1.000 caracteres", StringComparison.Ordinal) && vm.ServicesCounter == "1001/1000" && fixture.ProfileBodies.Count == 0, "Serviços com 1.001 caracteres deveriam ser recusados: " + vm.ServicesError);
                vm.UnitServices = "Consulta\tcom tab"; Pump(vm.SaveProfileAsync());
                Require(vm.ServicesError.Contains("caractere de controle", StringComparison.Ordinal) && fixture.ProfileBodies.Count == 0, "Caractere de controle deveria ser recusado: " + vm.ServicesError);
                // Cada validação recalcula todos os dias: para o erro do dia aparecer na tela, o dia inválido precisa estar na última tentativa.
                wednesday.IsClosed = false; Pump(vm.SaveProfileAsync());
                content = Layout(window, 1280, 5200);
                Require(!string.IsNullOrEmpty(wednesday.Error) && FullTexts(content).Any(text => text.StartsWith("Quarta-feira:", StringComparison.Ordinal)), "O erro do dia deveria aparecer na tela, junto do campo: " + wednesday.Error);
                wednesday.IsClosed = true;
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-erros-light.png"), 1280, 900);
                // Salvar: encostar não é sobrepor; texto com quebra de linha é aceito e devolvido como veio.
                vm.UnitServices = "Vacinação\nCurativos\r\nPré-natal  "; Pump(vm.SaveProfileAsync());
                var saved = fixture.ProfileOf(unit.Id) ?? throw new InvalidOperationException("O atendimento deveria estar gravado: " + vm.Message);
                Require(saved.Informed && saved.Services == "Vacinação\nCurativos\nPré-natal" && saved.OpeningHours.Count(item => item.Weekday == 1) == 2 && saved.OpeningHours.Any(item => item is { Weekday: 2, Opens: "08:00", Closes: "12:00" }) && saved.OpeningHours.Any(item => item is { Weekday: 2, Opens: "12:00", Closes: "18:00" }), "O que ficou gravado não confere com o editado.");
                Require(vm.MessageKind == MessageKind.Success && vm.ProfileSteps.Single() is { Text: "Salvando horários…", IsDone: true } && vm.ProfileState == "" && vm.ProfileUpdatedLabel.StartsWith("Última alteração em ", StringComparison.Ordinal) && !vm.OpeningDays[1].IsClosed && vm.OpeningDays[0].IsClosed, "Depois de salvar o passo fica concluído e a tela reflete o gravado.");
                // Dia que volta a "Fechado" e lista vazia (UBS fechada todos os dias) são válidos.
                foreach (var day in vm.OpeningDays) day.IsClosed = true;
                Pump(vm.SaveProfileAsync());
                Require(fixture.ProfileOf(unit.Id)!.OpeningHours.Length == 0 && fixture.ProfileOf(unit.Id)!.Informed && vm.ProfileState == "", "Todos os dias fechados é válido e deixa de ser 'não informado'.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 5200); SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light);
                // Uma edição em andamento não é apagada pela leitura automática; o botão Atualizar volta ao gravado.
                vm.OpeningDays[4].IsClosed = false; vm.OpeningDays[4].Opens1 = "09:00";
                Pump(vm.RefreshAutomaticallyAsync());
                Require(!vm.OpeningDays[4].IsClosed && vm.OpeningDays[4].Opens1 == "09:00", "A leitura automática não pode apagar a edição em andamento.");
                content = Layout(window, 1280, 5200); Click(window, content, "Atualizar informações da UBS", 1280, 5200);
                Require(vm.OpeningDays[4].IsClosed, "Atualizar informações da UBS deveria devolver os campos ao que está gravado.");
            }
            finally { CloseWindow(window); }
        });

        Check("Atendimento ao público: a atendente vê em leitura, com o aviso; a falha de leitura aponta para Atualizar informações da UBS e ele refaz a leitura", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant" };
            var unit = fixture.AddUnit("UBS Leitura", "Centro");
            fixture.FailProfile = true;
            var window = OpenOperational(fixture, storageRoot, "profile-attendant", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings", 1280, 5200);
                Require(!vm.CanEditProfile && vm.ShowProfileReadOnlyNotice && vm.ProfileReadOnlyNotice.StartsWith("Só o administrador da UBS altera o atendimento ao público.", StringComparison.Ordinal) && FullTexts(content).Contains(vm.ProfileReadOnlyNotice), "A atendente deveria ver o aviso de somente leitura.");
                Require(vm.ProfileState == "Não foi possível ler o atendimento da UBS agora. Use Atualizar informações da UBS para tentar de novo." && ButtonIn(content, "Atualizar informações da UBS").IsEnabled && !vm.CanSaveProfile, "A falha deveria apontar para o botão Atualizar informações da UBS: " + vm.ProfileState);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-atendimento-falha-light.png"), 1280, 900);
                fixture.FailProfile = false; var reads = fixture.Count("GET", "/profile");
                Click(window, content, "Atualizar informações da UBS", 1280, 5200);
                Require(fixture.Count("GET", "/profile") == reads + 1 && vm.ProfileState.StartsWith("Ainda não informado.", StringComparison.Ordinal) && vm.MessageKind == MessageKind.Success, "O botão deveria refazer a leitura do atendimento: " + vm.ProfileState);
                var days = Descendants<ItemsControl>(content).Single(items => AutomationProperties.GetName(items) == "Horário de funcionamento por dia da semana");
                var save = ButtonIn(content, "Salvar atendimento ao público");
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
