using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>"Chamar próximo" da Fila do OBS: escolher um horário livre e oferecer ao 1º da fila da especialidade.</summary>
internal static partial class Program
{
    private static void RunCallNextChecks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Chamar próximo: busca horários livres, mostra o 1º da fila, confirma e oferece; a fila e as vagas mostram a oferta e a origem Chamar próximo", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Chamar", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); fixture.AddSpecialty("Pediatria");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            var day = DateTime.Today.AddDays(2);
            fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 9)); fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 10));
            fixture.AddRegistry(unit.Id, "Maria Souza", "52998224725"); fixture.AddRegistry(unit.Id, "João Pereira Lima", "11144477735");
            fixture.AddWaiting(unit.Id, "52998224725", "Maria Souza", "Cardiologia", DateTimeOffset.UtcNow.AddHours(-3));
            fixture.AddWaiting(unit.Id, "11144477735", "João Pereira Lima", "cardiologia ", DateTimeOffset.UtcNow.AddHours(-2));
            var window = OpenOperational(fixture, storageRoot, "call-next-flow", Appearance.Light);
            var asked = new List<string>(); Confirmation.Handler = (_, _, text) => { asked.Add(text); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "queue", 1280, 2600);
                Require(ButtonIn(content, "Chamar próximo…").IsEnabled && !vm.CallNextOpen, "Com UBS ativa o botão Chamar próximo… deveria estar habilitado e o painel fechado.");
                Click(window, content, "Chamar próximo…", 1280, 2600);
                Require(vm.CallNextOpen && vm.CallNextSpecialties.Select(item => item.Label).SequenceEqual(["Cardiologia · 2 na fila", "Pediatria"]), "As especialidades com gente na fila deveriam vir primeiro: " + string.Join(" | ", vm.CallNextSpecialties.Select(item => item.Label)));
                Require(!vm.CanCallNextSearch && vm.CallNextState == "Escolha a especialidade para buscar horários livres.", "Sem especialidade escolhida não há busca: " + vm.CallNextState);
                vm.CallNextSpecialty = vm.CallNextSpecialties[0]; vm.CallNextPeriodChoice = CallNextWeek();
                Require(vm.CanCallNextSearch && !vm.CanCallNextOffer && vm.CallNextState == "Escolha o período e use Buscar horários livres.", "Com a especialidade escolhida a busca habilita, a oferta não.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-chamar-proximo-inicio-light.png"), 1280, 900);
                content = Layout(window, 1280, 2600);
                Click(window, content, "Buscar horários livres", 1280, 2600);
                Require(vm.CallNextSteps.Select(step => (step.Text, step.IsDone)).SequenceEqual([("Buscando horários livres…", true), ("Conferindo o 1º da fila…", true)]) && vm.CallNextSlots.Count == 2, "A busca deveria acumular os dois passos concluídos e listar os dois horários.");
                Require(vm.CallNextPreviewText.StartsWith("1º da fila que pode receber: Maria S. (desde ", StringComparison.Ordinal) && !vm.CanCallNextOffer, "A prévia deveria mostrar o 1º da fila em nome abreviado, e sem horário escolhido não se oferece: " + vm.CallNextPreviewText);
                Require(fixture.AvailabilityQueries.Count(item => item.Unit == unit.Id && item.Query["specialtyId"] == cardio.Id.ToString()) >= 1 && fixture.AvailabilityQueries[^1].Query["professionalId"] is null, "A busca deveria pedir os horários da especialidade, de qualquer profissional.");
                var grid = GridNamed(content, "Horários livres para o Chamar próximo");
                Require(grid.Items.Count == 2 && new[] { "Data", "Horário", "Profissional", "Consultório" }.SequenceEqual(grid.Columns.Select(column => column.Header?.ToString() ?? "")), "A lista de horários deveria ter data, horário, profissional e consultório.");
                vm.CallNextSlot = vm.CallNextSlots[0];
                Require(vm.CanCallNextOffer && vm.CallNextConfirmationText.EndsWith("a Maria S.? Ela tem 10 minutos para responder.", StringComparison.Ordinal) && vm.CallNextConfirmationText.Contains("com Dra. Ana Lima", StringComparison.Ordinal), "A confirmação deveria dizer horário, profissional e quem recebe: " + vm.CallNextConfirmationText);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-chamar-proximo-horarios-light.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 2600); SaveScrolled(window, Path.Combine(previewRoot, "obs-chamar-proximo-horarios-dark.png"), 1280, 900); window.ViewModel.SetAppearance(Appearance.Light);
                content = Layout(window, 1280, 2600);
                Click(window, content, "Oferecer ao 1º da fila", 1280, 2600);
                Require(asked.Count == 1 && fixture.CallNextBodies.Count == 1 && fixture.CallNextBodies[0].GetProperty("confirmed").GetBoolean() && fixture.CallNextBodies[0].GetProperty("professionalId").GetGuid() == ana.Id, "Oferecer deveria pedir confirmação e enviar o horário, o profissional e confirmed.");
                Require(vm.MessageKind == MessageKind.Success && vm.Message.StartsWith("Horário oferecido a Maria S. até ", StringComparison.Ordinal) && vm.Message.EndsWith("Se ela recusar ou não responder, continua na fila na mesma posição e o horário segue sozinho para os próximos.", StringComparison.Ordinal), "A mensagem de sucesso está errada: " + vm.Message);
                Require(vm.CallNextSteps.Any(step => step is { Text: "Oferecendo o horário…", IsDone: true }) && vm.CallNextSteps.Last() is { Text: "Conferindo o 1º da fila…", IsDone: true } && vm.CallNextSlots.Count == 1 && vm.CallNextSlot is null, "O passo de oferecer deveria ficar concluído e o horário sair da lista.");
                Require(vm.UnitWaitingList.Single(item => item.PatientName == "Maria Souza").Status == "offered" && vm.UnitLightningSlots.Single() is { Origin: "call-next", OriginDisplay: "Chamar próximo", AppointmentId: null, Status: "offered" }, "A fila e as vagas deveriam refletir a oferta do Chamar próximo.");
                Require(vm.CallNextPreviewText.Contains("João L.", StringComparison.Ordinal), "A prévia deveria passar a mostrar o próximo da fila: " + vm.CallNextPreviewText);
                // A coluna Origem da lista de vagas mostra Chamar próximo.
                content = Show(window, "slots", 1280, 2600);
                var slotsGrid = GridNamed(content, "Vagas da UBS");
                Require(slotsGrid.Columns.Any(column => column.Header?.ToString() == "Origem") && vm.UnitLightningSlots.Any(slot => slot.OriginDisplay == "Chamar próximo"), "A lista de vagas deveria ter a coluna Origem com Chamar próximo.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Chamar próximo: mensagens dos 409 (fila sem ninguém, horário muito próximo, horário ocupado), prévia sem ninguém e confirmação recusada", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Chamar 409", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            var day = DateTime.Today.AddDays(2);
            var first = fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 9)); var second = fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 10)); var third = fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 11));
            var soon = fixture.AddAvailableSlot(unit.Id, ana, cardio, room, DateTimeOffset.Now.AddMinutes(5));
            fixture.AddRegistry(unit.Id, "Maria Souza", "52998224725"); fixture.AddWaiting(unit.Id, "52998224725", "Maria Souza", "Cardiologia", DateTimeOffset.UtcNow.AddHours(-1));
            var window = OpenOperational(fixture, storageRoot, "call-next-409", Appearance.Light);
            var answer = true; var asked = 0; Confirmation.Handler = (_, _, _) => { asked++; return answer; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "queue", 1280, 2600);
                Click(window, content, "Chamar próximo…", 1280, 2600);
                vm.CallNextSpecialty = vm.CallNextSpecialties.Single(); vm.CallNextPeriodChoice = CallNextWeek();
                Click(window, content, "Buscar horários livres", 1280, 2600);
                Require(vm.CallNextSlots.Count == 4, "Os quatro horários deveriam aparecer: " + vm.CallNextSlots.Count);
                // Confirmação recusada: nada é enviado.
                vm.CallNextSlot = vm.CallNextSlots.Single(item => item.StartsAt == first.StartsAt); answer = false;
                Click(window, content, "Oferecer ao 1º da fila", 1280, 2600);
                Require(asked == 1 && fixture.CallNextBodies.Count == 0, "Confirmação recusada não pode enviar nada.");
                answer = true;
                // Horário com menos de 10 minutos: 409 call-next-too-late, o horário sai da lista.
                vm.CallNextSlot = vm.CallNextSlots.Single(item => item.StartsAt == soon.StartsAt);
                Click(window, content, "Oferecer ao 1º da fila", 1280, 2600);
                Require(vm.MessageKind == MessageKind.Error && vm.Message == "Faltam menos de 10 minutos para esse horário. Escolha um horário mais tarde." && vm.CallNextSlots.All(item => item.StartsAt != soon.StartsAt) && vm.CallNextSteps.Last().IsFailed, "call-next-too-late deveria dizer a mensagem certa e tirar o horário da lista: " + vm.Message);
                // Horário tomado por outra pessoa no meio do caminho: 409 slot-unavailable.
                ((List<AvailableSlot>)fixture.FreeSlotsOf(unit.Id)).Remove(second);
                vm.CallNextSlot = vm.CallNextSlots.Single(item => item.StartsAt == second.StartsAt);
                Click(window, content, "Oferecer ao 1º da fila", 1280, 2600);
                Require(vm.Message == "Esse horário não está mais livre. Escolha outro horário da lista." && vm.CallNextSlots.All(item => item.StartsAt != second.StartsAt), "slot-unavailable deveria usar a mensagem do horário ocupado: " + vm.Message);
                // Fila que ficou sem ninguém no meio do caminho: 409 queue-empty e a oferta fica desabilitada.
                fixture.IneligibleCpfs.Add("52998224725");
                vm.CallNextSlot = vm.CallNextSlots.Single(item => item.StartsAt == third.StartsAt);
                Click(window, content, "Oferecer ao 1º da fila", 1280, 2600);
                Require(vm.Message == "Ninguém desta fila pode receber a oferta agora (sem conta no aplicativo, cadastro inativo, oferta pendente ou consulta no mesmo horário)." && !vm.CanCallNextOffer, "queue-empty deveria dizer a mensagem certa e desabilitar a oferta: " + vm.Message);
                // Nova busca: a prévia diz que ninguém pode receber.
                Click(window, content, "Buscar horários livres", 1280, 2600);
                Require(vm.CallNextPreviewText == "Ninguém desta fila pode receber pelo aplicativo agora." && vm.CallNextSlots.Count == 3 && !vm.CanCallNextOffer, "Sem ninguém elegível a prévia deveria dizer isso (a busca traz de novo o horário próximo, que o servidor ainda lista): " + vm.CallNextPreviewText + " / horários " + vm.CallNextSlots.Count);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-chamar-proximo-sem-ninguem-light.png"), 1280, 900);
                Require(fixture.CallNextBodies.Count == 2, "Dois pedidos passaram pela conferência do horário e chegaram ao servidor; o terceiro parou nela (ninguém pode receber) e não foi enviado: " + fixture.CallNextBodies.Count);
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Chamar próximo: a confirmação nomeia quem de fato recebe o horário escolhido (pula quem já tem consulta ali) e, sem conferência, não nomeia ninguém", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Horário", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            var day = DateTime.Today.AddDays(2);
            var nine = fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 9)); var ten = fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 10));
            fixture.AddRegistry(unit.Id, "Maria Souza", "52998224725"); fixture.AddRegistry(unit.Id, "João Pereira Lima", "11144477735");
            fixture.AddWaiting(unit.Id, "52998224725", "Maria Souza", "Cardiologia", DateTimeOffset.UtcNow.AddHours(-3));
            fixture.AddWaiting(unit.Id, "11144477735", "João Pereira Lima", "Cardiologia", DateTimeOffset.UtcNow.AddHours(-2));
            fixture.BusyAtStart["52998224725"] = nine.StartsAt; // Maria já tem consulta às 09:00
            var window = OpenOperational(fixture, storageRoot, "call-next-time", Appearance.Light);
            var asked = new List<string>(); Confirmation.Handler = (_, _, text) => { asked.Add(text); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "queue", 1280, 2600);
                Click(window, content, "Chamar próximo…", 1280, 2600);
                vm.CallNextSpecialty = vm.CallNextSpecialties.Single(); vm.CallNextPeriodChoice = CallNextWeek();
                Click(window, content, "Buscar horários livres", 1280, 2600);
                Require(vm.CallNextPreviewText.Contains("Maria S.", StringComparison.Ordinal), "A prévia geral (sem horário) mostra a 1ª da fila: " + vm.CallNextPreviewText);
                vm.CallNextSlot = vm.CallNextSlots.Single(item => item.StartsAt == nine.StartsAt);
                Click(window, content, "Oferecer ao 1º da fila", 1280, 2600);
                Require(asked.Count == 1 && asked[0].Contains("João L.", StringComparison.Ordinal) && !asked[0].Contains("Maria", StringComparison.Ordinal), "A confirmação das 09:00 deveria nomear o João (Maria já tem consulta ali): " + string.Join(" | ", asked));
                Require(fixture.NextInQueueQueries.Any(query => query.Contains("startsAt=", StringComparison.Ordinal) && query.Contains($"professionalId={ana.Id}", StringComparison.OrdinalIgnoreCase)), "A conferência deveria enviar o horário e o profissional escolhidos: " + string.Join(" | ", fixture.NextInQueueQueries));
                Require(vm.Message.StartsWith("Horário oferecido a João L.", StringComparison.Ordinal), "A oferta foi ao João, como a confirmação disse: " + vm.Message);
                // Sem conseguir conferir o horário a confirmação não nomeia ninguém (o servidor decide).
                fixture.FailNextInQueue = true;
                vm.CallNextSlot = vm.CallNextSlots.Single(item => item.StartsAt == ten.StartsAt);
                Click(window, content, "Oferecer ao 1º da fila", 1280, 2600);
                Require(asked.Count == 2 && asked[1].Contains("ao 1º da fila que puder receber nesse horário", StringComparison.Ordinal) && !asked[1].Contains("Maria S.", StringComparison.Ordinal), "Sem conferência a confirmação não deveria nomear ninguém: " + asked[^1]);
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Chamar próximo: estados vazio, falha com o botão que refaz a busca e UBS sem grade; atendente e administrador veem o botão", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true, ObsRole = "attendant" };
            var unit = fixture.AddUnit("UBS Estados", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            fixture.AddRegistry(unit.Id, "Maria Souza", "52998224725"); fixture.AddWaiting(unit.Id, "52998224725", "Maria Souza", "Cardiologia", DateTimeOffset.UtcNow.AddHours(-1));
            var window = OpenOperational(fixture, storageRoot, "call-next-states", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "queue", 1280, 2600);
                Require(vm.AccountRoleLabel == "Atendente" && ButtonIn(content, "Chamar próximo…").IsEnabled, "A atendente também deveria ver o botão Chamar próximo….");
                Click(window, content, "Chamar próximo…", 1280, 2600);
                vm.CallNextSpecialty = vm.CallNextSpecialties.Single();
                Click(window, content, "Buscar horários livres", 1280, 2600);
                Require(vm.CallNextState == "Nenhum horário livre nesse período." && vm.CallNextSlots.Count == 0, "Sem horário livre o estado vazio deveria aparecer: " + vm.CallNextState);
                fixture.FailAvailability = true;
                Click(window, content, "Buscar horários livres", 1280, 2600);
                Require(vm.CallNextState == "Não foi possível buscar os horários livres. Use Buscar horários livres para tentar de novo." && ButtonIn(content, "Buscar horários livres").IsEnabled && vm.MessageKind == MessageKind.Error && vm.CallNextSteps.Single().IsFailed, "A falha deveria apontar para o botão que existe na tela: " + vm.CallNextState);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-chamar-proximo-falha-light.png"), 1280, 900);
                fixture.FailAvailability = false;
                fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(DateTime.Today.AddDays(2), 9));
                vm.CallNextPeriodChoice = CallNextWeek();
                Click(window, content, "Buscar horários livres", 1280, 2600);
                Require(vm.CallNextState == "" && vm.CallNextSlots.Count == 1 && vm.CallNextSteps.All(step => step.IsDone), "O mesmo botão deveria refazer a busca e limpar a falha: " + vm.CallNextState);
                // Carregando: a busca fica retida pelo servidor e o passo aparece girando.
                fixture.PauseReads = new TaskCompletionSource();
                var running = vm.SearchCallNextSlotsAsync();
                PumpUntil(() => vm.CallNextSteps.Any(step => step.IsRunning));
                Require(vm.CallNextSteps.Single(step => step.IsRunning).Text == "Buscando horários livres…" && vm.IsBusy, "Durante a busca o passo deveria aparecer em andamento.");
                fixture.PauseReads.SetResult(); fixture.PauseReads = null; Pump(running);
            }
            finally { CloseWindow(window); }
            // UBS sem profissional: sem grade não há horário para chamar.
            using var empty = new ApiFixture { UseWorkspaceIdentity = true };
            empty.AddUnit("UBS Sem Grade", "Centro"); empty.AddSpecialty("Cardiologia");
            var emptyWindow = OpenOperational(empty, storageRoot, "call-next-no-catalog", Appearance.Light);
            try
            {
                var vm = LoginObs(emptyWindow);
                var content = Show(emptyWindow, "queue", 1280, 2600);
                Click(emptyWindow, content, "Chamar próximo…", 1280, 2600);
                Require(vm.CallNextWithoutCatalog && vm.CallNextState == "Esta UBS ainda não tem grade; cadastre profissionais e grade para chamar pelo horário." && FullTexts(Layout(emptyWindow, 1280, 2600)).Contains(vm.CallNextState), "UBS sem grade deveria explicar o que falta: " + vm.CallNextState);
            }
            finally { CloseWindow(emptyWindow); }
        });

        Check("Chamar próximo: falha na leitura da grade (as especialidades não vieram) oferece o botão Reler a grade, que refaz só essa leitura e libera a busca", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Grade falha", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia");
            fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]); fixture.AddRoom(unit.Id, "Consultório 1");
            fixture.FailCatalogUnits.Add(unit.Id);
            var window = OpenOperational(fixture, storageRoot, "call-next-catalog-retry", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "queue", 1280, 2600);
                Click(window, content, "Chamar próximo…", 1280, 2600);
                content = Layout(window, 1280, 2600);
                Require(vm.CallNextState == "Não foi possível ler a grade da UBS. Use Reler a grade para tentar de novo." && vm.CanRetryCallNextCatalog && ButtonIn(content, "Reler a grade").IsEnabled && ButtonIn(content, "Reler a grade").IsVisible,
                    "A frase deveria apontar para um botão que existe: " + vm.CallNextState);
                fixture.FailCatalogUnits.Clear();
                var reads = fixture.Count("GET", "/professionals");
                Click(window, content, "Reler a grade", 1280, 2600);
                Require(fixture.Count("GET", "/professionals") == reads + 1 && !vm.CanRetryCallNextCatalog && vm.CallNextSpecialties.Count == 1 && vm.CallNextSteps.Single() is { Text: "Lendo a grade da UBS…", IsDone: true }, "O botão deveria refazer a leitura da grade e liberar as especialidades: " + vm.CallNextState);
                vm.CallNextSpecialty = vm.CallNextSpecialties.Single();
                Require(vm.CanCallNextSearch && !Descendants<Button>(Layout(window, 1280, 2600)).Any(button => button.IsVisible && button.Content?.ToString() == "Reler a grade"), "Com a grade lida a busca fica habilitada e o botão de reler some.");
            }
            finally { CloseWindow(window); }
        });
    }

    private static CallNextPeriod CallNextWeek() => WorkspaceViewModel.CallNextPeriods.Single(item => item.Days == 7);
}
