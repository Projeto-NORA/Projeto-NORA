using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>OBS: comunicados recebidos (não lidos, contador, ciclo incremental), Jornada na ficha do paciente (exames e vacinas), Família e a confirmação de desativar com a prévia.</summary>
internal static partial class Program
{
    private static Button ButtonIn(DependencyObject root, string content) => Descendants<Button>(root).Single(button => button.IsVisible && (button.Content?.ToString() == content || AutomationProperties.GetName(button) == content));
    /// <summary>Salva a aba da ficha em partes (-01, -02…), rolando a área da Jornada.</summary>
    private static void SavePopupScrolled(ItemDetailsWindow popup, string pathWithoutExtension)
    {
        var scroll = Descendants<ScrollViewer>(popup).First(viewer => viewer.IsVisible && Descendants<HealthRecordsView>(viewer).Any());
        scroll.ScrollToTop(); popup.UpdateLayout();
        var offsets = new List<double> { 0 }; var step = Math.Max(1, scroll.ViewportHeight - 40);
        for (var next = step; next < scroll.ScrollableHeight; next += step) offsets.Add(next);
        if (scroll.ScrollableHeight > 1) offsets.Add(scroll.ScrollableHeight);
        for (var index = 0; index < offsets.Count; index++) { scroll.ScrollToVerticalOffset(offsets[index]); popup.UpdateLayout(); SavePreview(popup, $"{pathWithoutExtension}-{index + 1:D2}.png"); }
        scroll.ScrollToTop(); popup.UpdateLayout();
    }
    private static string Iso(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void RunCommunityChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        RunAnnouncementChecks(storageRoot, previewRoot);
        RunHealthRecordChecks(storageRoot, previewRoot);
        RunFamilyChecks(storageRoot, previewRoot);
        RunDeactivationChecks(storageRoot, previewRoot);
        RunCommunicationChecks(storageRoot, previewRoot);
    }

    private static void RunAnnouncementChecks(string storageRoot, string previewRoot)
    {
        Check("OBS Comunicados: aviso de novidades não confirma leitura e volta para comunicado novo", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Avisos"); fixture.AddAnnouncement("Primeiro aviso", "Texto");
            var window = OpenOperational(fixture, storageRoot, "announcements-badge", Appearance.Light);
            try
            {
                var vm = LoginObs(window); var nav = window.ViewModel.Navigation.Single(item => item.Id == "announcements");
                Require(nav.Badge == 1, "O aviso inicial deveria aparecer no menu.");
                window.ViewModel.Navigate("announcements"); Settle(vm);
                Require(nav.Badge == 0 && vm.UnreadAnnouncementCount == 1 && fixture.Count("POST", "/read") == 0, "Abrir a aba não deve marcar o comunicado como lido.");
                window.ViewModel.Navigate("home"); Settle(vm);
                fixture.AddAnnouncement("Aviso novo", "Texto"); Pump(vm.RefreshAutomaticallyAsync());
                Require(nav.Badge == 1 && vm.UnreadAnnouncementCount == 2, "Um comunicado novo deve aparecer no menu sem contar novamente o já visto.");
                window.ViewModel.Navigate("announcements"); Settle(vm);
                Require(nav.Badge == 0 && vm.UnreadAnnouncementCount == 2, "Abrir novamente deve limpar só o aviso de navegação.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Comunicados: lista só o que vale para a conta, destaca não lidos por texto, conta na navegação, marca lido e faz uma chamada incremental por ciclo", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Comunicados", "Centro"); var other = fixture.AddUnit("UBS Outra", "Norte");
            fixture.Granted = [unit.Id];
            fixture.AddAnnouncement("Campanha de vacinação", "Linha 1 com <b>html</b> e &amp; texto\nLinha 2 do aviso");
            fixture.AddAnnouncement("Reunião das equipes", "Sexta, às 10h.", "both", "region", ["Centro"]);
            fixture.AddAnnouncement("Só para pacientes", "não aparece", "patients");
            fixture.AddAnnouncement("De outra região", "não aparece", "staff", "region", ["Norte"]);
            fixture.AddAnnouncement("De outra UBS", "não aparece", "staff", "units", unitIds: [other.Id]);
            fixture.AddAnnouncement("Já venceu", "não aparece", "staff", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
            fixture.AddAnnouncement("Encerrado", "não aparece", "staff").ClosedAt = DateTimeOffset.UtcNow;
            var window = OpenOperational(fixture, storageRoot, "announcements", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var nav = window.ViewModel.Navigation.Single(item => item.Id == "announcements");
                Require(vm.Announcements.Select(row => row.Title).SequenceEqual(["Reunião das equipes", "Campanha de vacinação"]), "Só os comunicados em vigor do público e do alcance da conta (mais novo primeiro): " + string.Join(" | ", vm.Announcements.Select(row => row.Title)));
                Require(vm.UnreadAnnouncementCount == 2 && nav.Badge == 2 && nav.AutomationName == "Comunicados, 2 novos comunicados", $"O contador de novidades deveria ser 2: {vm.UnreadAnnouncementCount} / {nav.Badge} / {nav.AutomationName}.");
                Require(fixture.AnnouncementFeedQueries.Count == 1 && fixture.AnnouncementFeedQueries[0]["after"] is null, "O login deveria fazer uma leitura completa (sem after).");
                var content = Show(window, "announcements", 1280, 1500);
                Require(fixture.AnnouncementFeedQueries.Count == 2 && fixture.AnnouncementFeedQueries[1]["after"] is null, "Abrir a aba deveria fazer uma leitura completa.");
                var texts = FullTexts(content);
                Require(texts.Count(text => text == "Não lido") == 2 && Descendants<Button>(content).Count(button => button.IsVisible && button.Content?.ToString() == "Marcar como lido") == 2, "Os dois não lidos deveriam aparecer com o texto \"Não lido\" e o botão de marcar.");
                Require(texts.Contains("Linha 1 com <b>html</b> e &amp; texto\nLinha 2 do aviso") || Descendants<TextBlock>(content).Any(text => text.Text == "Linha 1 com <b>html</b> e &amp; texto\nLinha 2 do aviso"), "O texto do comunicado deveria aparecer como texto puro, sem interpretar o que parece HTML.");
                Require(nav.Badge == 0 && vm.UnreadAnnouncementCount == 2 && fixture.Count("POST", "/read") == 0, "Abrir a aba deveria ocultar o aviso sem registrar leitura dos comunicados.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-comunicados-light.png"), 1280, 900);
                // Marcar como lido: pela ViewModel e pela tela; idempotente.
                Pump(vm.MarkAnnouncementReadAsync(vm.Announcements[1]));
                Require(fixture.Count("POST", "/read") == 1 && vm.UnreadAnnouncementCount == 1 && nav.Badge == 0 && vm.Announcements[1].StateDisplay.StartsWith("Lido em", StringComparison.Ordinal), "Marcar como lido deveria gravar e baixar o contador de não lidos.");
                Pump(vm.MarkAnnouncementReadAsync(vm.Announcements[1]));
                Require(fixture.Count("POST", "/read") == 1, "Marcar de novo o que já está lido não deveria chamar a API.");
                content = Layout(window, 1280, 1500);
                Require(FullTexts(content).Count(text => text == "Não lido") == 1 && Descendants<Button>(content).Count(button => button.IsVisible && button.Content?.ToString() == "Marcar como lido") == 1, "O comunicado lido deveria perder o destaque e o botão.");
                Click(window, content, "Marcar como lido", 1280, 1500);
                Require(fixture.Count("POST", "/read") == 2 && vm.UnreadAnnouncementCount == 0 && nav.Badge == 0 && nav.AutomationName == "Comunicados" && !Descendants<TextBlock>(window).Any(text => text.IsVisible && text.Text == "0"), "Sem não lidos o contador some do menu.");
                // Ciclo: uma chamada incremental com o cursor, sem mexer na lista quando nada mudou.
                var before = fixture.AnnouncementFeedQueries.Count; var changes = 0;
                vm.Announcements.CollectionChanged += (_, _) => changes++;
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.AnnouncementFeedQueries.Count == before + 3 && fixture.AnnouncementFeedQueries.Skip(before).All(query => query["after"] == "7") && changes == 0, $"Cada ciclo deveria fazer uma só chamada incremental (after=7) e não alterar a lista: {fixture.AnnouncementFeedQueries.Count - before} chamadas, {changes} mudanças.");
                fixture.AddAnnouncement("Novo comunicado", "chegou agora", "staff");
                Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.AnnouncementFeedQueries.Count == before + 4 && fixture.AnnouncementFeedQueries[^1]["after"] == "7" && vm.Announcements[0].Title == "Novo comunicado" && changes == 1 && vm.UnreadAnnouncementCount == 1 && nav.Badge == 0, "Comunicado recebido enquanto a aba está aberta aparece sem aviso de navegação.");
                Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.AnnouncementFeedQueries[^1]["after"] == "8" && changes == 1, "O cursor deveria avançar para o último comunicado existente (8).");
                // Encerrar ou vencer no servidor só some na leitura completa; a validade também vence no cliente, sem requisição.
                fixture.Announcements.Single(item => item.Title == "Campanha de vacinação").ClosedAt = DateTimeOffset.UtcNow;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.Announcements.Any(row => row.Title == "Campanha de vacinação"), "A leitura incremental não traz o que foi encerrado depois (só a completa).");
                var full = fixture.AnnouncementFeedQueries.Count;
                Pump(vm.LoadAnnouncementsAsync());
                Require(fixture.AnnouncementFeedQueries.Count == full + 1 && fixture.AnnouncementFeedQueries[^1]["after"] is null && vm.Announcements.All(row => row.Title != "Campanha de vacinação"), "Atualizar dados deveria ler tudo de novo e tirar o encerrado.");
                fixture.AddAnnouncement("Expira logo", "dura dois segundos", "staff", expiresAt: DateTimeOffset.UtcNow.AddSeconds(2));
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.Announcements.Any(row => row.Title == "Expira logo"), "O comunicado com validade futura deveria aparecer.");
                Thread.Sleep(2300);
                var queries = fixture.AnnouncementFeedQueries.Count; Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.Announcements.All(row => row.Title != "Expira logo") && fixture.AnnouncementFeedQueries.Count == queries + 1, "A validade vencida deveria tirar o comunicado da lista sem leitura completa.");
                // Falha de leitura: sem erro na faixa, a lista continua e a aba avisa.
                fixture.FailAnnouncementFeed = true; var kept = vm.Announcements.Count;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.Announcements.Count == kept && vm.AnnouncementsFootnote.Contains("não concluiu", StringComparison.Ordinal) && vm.MessageKind != MessageKind.Error, "A falha de leitura deveria manter a lista e avisar na aba: " + vm.AnnouncementsFootnote);
                fixture.FailAnnouncementFeed = false; Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.AnnouncementsFootnote.StartsWith("Atualizado", StringComparison.Ordinal), "A leitura que voltou deveria limpar o aviso.");
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 1500);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-comunicados-dark.png"), 1280, 900);
                var body = Descendants<TextBlock>(content).First(text => text.IsVisible && text.Text == "chegou agora");
                Require(((SolidColorBrush)body.Foreground).Color == ((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color && Contrast(((SolidColorBrush)body.Foreground).Color, ((SolidColorBrush)Application.Current.FindResource("SurfaceBrush")).Color) >= 4.5, "O texto do comunicado deveria ter contraste AA no tema escuro.");
                window.ViewModel.SetAppearance(Appearance.System);
                Require(Descendants<TextBlock>(window).Any(text => text.Text.Contains("não lidos", StringComparison.Ordinal) || text.Text == "Comunicados"), "O tema Sistema deveria renderizar a aba.");
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(vm.Announcements.Count == 0 && nav.Badge == 0 && vm.UnreadAnnouncementCount == 0, "Sair deveria limpar os comunicados e o contador.");
            }
            finally { CloseWindow(window); }
        });
        Check("OBS Comunicados: sem UBS ativa o ciclo não faz chamadas; a leitura sai do login, da aba e do Atualizar dados", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS A", "Centro"); fixture.AddUnit("UBS B", "Norte");
            fixture.AddAnnouncement("Aviso geral", "texto", "staff");
            var window = OpenOperational(fixture, storageRoot, "announcements-no-unit");
            try
            {
                var vm = LoginObs(window);
                Require(vm.ActiveObsUnit is null && vm.UnreadAnnouncementCount == 1 && fixture.AnnouncementFeedQueries.Count == 1, "Com duas UBS e nenhuma ativa, o login deveria ler os comunicados uma vez.");
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.AnnouncementFeedQueries.Count == 1, "Sem UBS ativa o ciclo não deveria chamar a API.");
                window.ViewModel.Navigate("announcements"); Settle(vm);
                Require(fixture.AnnouncementFeedQueries.Count == 2 && vm.Announcements.Count == 1, "Abrir a aba deveria ler de novo, mesmo sem UBS.");
            }
            finally { CloseWindow(window); }
        });
    }

    private static void RunHealthRecordChecks(string storageRoot, string previewRoot)
    {
        Check("OBS Jornada: aba Exames e vacinas na ficha lista (com anulados), registra exame, marca realizado ou cancelado, registra vacina com dose e data, anula com motivo e confirmação, com validação igual à do servidor", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Jornada", "Centro");
            var patient = fixture.AddRegistry(unit.Id, "Ana Souza", "52998224725");
            fixture.AddRegistry(unit.Id, "Beto Lima", "11144477735", active: false);
            fixture.AddExam(unit.Id, patient.Cpf, "Hemograma", "done", new DateOnly(2026, 3, 10));
            fixture.AddVaccine(unit.Id, patient.Cpf, "Gripe", "single", new DateOnly(2026, 4, 1));
            fixture.AddVaccine(unit.Id, patient.Cpf, "Tétano antigo", "1", new DateOnly(2020, 1, 5), "voided", "Registro duplicado");
            var asked = new List<(string Title, string Text)>(); var answer = true;
            Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return answer; };
            var window = OpenOperational(fixture, storageRoot, "health", Appearance.Light);
            ItemDetailsWindow? popup = null;
            try
            {
                var vm = LoginObs(window);
                var record = vm.ObsRegistry.Single(item => item.Cpf == patient.Cpf);
                var health = vm.CreateHealthRecords(record) ?? throw new InvalidOperationException("A ficha deveria oferecer a aba de exames e vacinas.");
                Require(fixture.Count("POST", "/health-records/query") == 0 && health.CanRegister, "A Jornada não deveria ser lida antes de abrir a aba.");
                popup = new ItemDetailsWindow(vm.BuildDetails(record)!, new ClipboardFixture(), health) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                popup.Show(); popup.UpdateLayout();
                Require(Descendants<Button>(popup).Any(button => AutomationProperties.GetName(button) == "Aba Dados") && !Descendants<HealthRecordsView>(popup).Any(view => view.IsVisible), "A ficha deveria abrir na aba Dados, com a aba de exames e vacinas oferecida mas fechada.");
                InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Aba Exames e vacinas")); PumpUntil(() => !health.IsBusy); popup.UpdateLayout();
                Require(fixture.Count("POST", "/health-records/query") == 1 && Descendants<HealthRecordsView>(popup).Any(view => view.IsVisible), "Abrir a aba deveria ler a Jornada uma vez.");
                Require(health.Exams.Single() is { Name: "Hemograma", StatusDisplay: "Realizado", DateDisplay: "Realizado em 10/03/2026" } && health.Vaccines.Single() is { Name: "Gripe", DoseDisplay: "Dose única" }, "A lista deveria trazer o exame realizado e a vacina aplicada, sem os anulados.");
                InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Aba Dados")); InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Aba Exames e vacinas")); PumpUntil(() => !health.IsBusy);
                Require(fixture.Count("POST", "/health-records/query") == 1, "Voltar para a aba não deveria ler de novo.");
                health.IncludeVoided = true; PumpUntil(() => !health.IsBusy);
                Require(fixture.Count("POST", "/health-records/query") == 2 && health.Vaccines.Count == 2 && health.Vaccines.Single(item => item.IsVoided).VoidDisplay.Contains("motivo: Registro duplicado", StringComparison.Ordinal), "Mostrar anulados deveria ler uma vez e exibir o motivo da anulação.");
                health.IncludeVoided = false; PumpUntil(() => !health.IsBusy);
                Require(fixture.Count("POST", "/health-records/query") == 3 && health.Vaccines.Count == 1, "Ocultar anulados deveria ler uma vez e esconder o anulado.");
                // Exame: validação local antes de enviar (mesmas regras do servidor) e nunca o CPF na URL.
                health.ExamName = ""; Pump(health.CreateExamAsync());
                Require(health.ExamNameError.Contains("Informe o nome do exame") && health.MessageKind == MessageKind.Error && fixture.Count("POST", "/health-records/exams") == 0, "Exame sem nome foi enviado.");
                health.ExamName = new string('E', 161); Pump(health.CreateExamAsync());
                Require(health.ExamNameError.Contains("1 a 160") && fixture.Count("POST", "/health-records/exams") == 0, "Nome com mais de 160 caracteres foi enviado.");
                health.ExamName = "Raio\tX"; Pump(health.CreateExamAsync());
                Require(health.ExamNameError.Contains("inválidos") && fixture.Count("POST", "/health-records/exams") == 0, "Nome com caractere de controle foi enviado.");
                health.ExamName = "Raio-X"; health.ExamNote = new string('o', 281); Pump(health.CreateExamAsync());
                Require(health.ExamNoteError.Contains("280") && fixture.Count("POST", "/health-records/exams") == 0, "Observação com mais de 280 caracteres foi enviada.");
                health.ExamName = "  Raio-X de tórax  "; health.ExamNote = " Jejum de 6 h "; Pump(health.CreateExamAsync());
                Require(fixture.Count("POST", "/health-records/exams") == 1 && health.Exams.First(item => item.Name == "Raio-X de tórax") is { Status: "requested", StatusDisplay: "Solicitado", Note: "Jejum de 6 h" } && health.MessageKind == MessageKind.Success && health.ExamName == "",
                    "O exame válido deveria nascer solicitado, com nome e observação aparados: " + health.Message);
                Require(fixture.Requests.All(request => !request.Contains(patient.Cpf, StringComparison.Ordinal)), "O CPF nunca deveria ir na URL.");
                Require(health.SelectedExam?.Name == "Raio-X de tórax" && health.CanMarkExamDone && health.CanCancelExam, "O exame criado deveria ficar selecionado, com as ações de solicitado.");
                // Marcar realizado: data obrigatória, dentro de 01/01/1900 e hoje.
                foreach (var invalid in new[] { "", "xx", "31/12/1899", DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today.AddDays(2))) })
                {
                    health.ExamDoneDate = invalid; Pump(health.MarkExamDoneAsync());
                    Require(health.ExamDoneError.Contains("dd/mm/aaaa") || health.ExamDoneError.Contains("entre 01/01/1900 e hoje"), $"A data \"{invalid}\" deveria ser recusada no campo: {health.ExamDoneError}");
                }
                Require(fixture.Count("PATCH", "/status") == 0, "Nenhuma data inválida deveria chegar ao servidor.");
                health.ExamDoneDate = DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today)); Pump(health.MarkExamDoneAsync());
                var done = fixture.HealthBodies.Last(item => item.Action == "exam-status").Body;
                Require(fixture.Count("PATCH", "/status") == 1 && done.GetProperty("status").GetString() == "done" && done.GetProperty("performedOn").GetString() == Iso(DateTime.Today) && health.Exams.First(item => item.Name == "Raio-X de tórax").Status == "done" && health.ExamDoneDate == "",
                    "Marcar realizado deveria enviar a data em yyyy-MM-dd e mostrar Realizado.");
                Require(!health.CanMarkExamDone && !health.CanCancelExam, "Um exame realizado não pode mais mudar de situação.");
                Pump(health.MarkExamDoneAsync()); Pump(health.CancelExamAsync());
                Require(fixture.Count("PATCH", "/status") == 1 && health.Message.Contains("exame solicitado", StringComparison.Ordinal), "A transição inválida (realizado → realizado ou cancelado) deveria ser recusada sem chamar a API.");
                // Cancelar pela tela, com confirmação.
                health.ExamName = "Ultrassom"; Pump(health.CreateExamAsync()); popup.UpdateLayout();
                PumpFrames(2); answer = false; asked.Clear(); InvokeButton(ButtonIn(popup, "Cancelar exame")); PumpUntil(() => !health.IsBusy);
                Require(asked.Count == 1 && asked[0].Title == "Cancelar exame" && fixture.Count("PATCH", "/status") == 1 && health.SelectedExam?.Status == "requested", "Recusar a confirmação não deveria cancelar o exame.");
                answer = true; InvokeButton(ButtonIn(popup, "Cancelar exame")); PumpUntil(() => !health.IsBusy);
                Require(fixture.Count("PATCH", "/status") == 2 && fixture.HealthBodies.Last(item => item.Action == "exam-status").Body.GetProperty("status").GetString() == "cancelled" && health.Exams.First(item => item.Name == "Ultrassom").StatusDisplay == "Cancelado", "Confirmar deveria cancelar o exame.");
                Pump(health.CancelExamAsync());
                Require(fixture.Count("PATCH", "/status") == 2, "Cancelar um exame já cancelado não deveria chamar a API.");
                // Registro alterado por outra pessoa (409): mensagem própria e a lista volta a refletir o servidor.
                var stale = fixture.AddExam(unit.Id, patient.Cpf, "Colesterol"); Pump(health.ReloadAsync()); health.SelectedExam = health.Exams.Single(item => item.Id == stale.Id);
                fixture.ChangeExam(unit.Id, patient.Cpf, stale.Id, item => item with { Status = "done", PerformedOn = new DateOnly(2026, 5, 5) });
                health.ExamDoneDate = DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today)); Pump(health.MarkExamDoneAsync()); PumpUntil(() => !health.IsBusy);
                Require(health.Message.Contains("outra pessoa", StringComparison.Ordinal) && health.Exams.Single(item => item.Id == stale.Id).Status == "done", "O 409 deveria avisar e a lista deveria refletir o exame já realizado: " + health.Message);
                // Vacina: dose, data e nome.
                Require(HealthRecordsViewModel.DoseOptions.Select(option => option.Label).SequenceEqual(["1ª dose", "2ª dose", "3ª dose", "Reforço", "Dose única"]), "As doses oferecidas deveriam ser as cinco da API.");
                health.VaccineName = ""; health.VaccineDate = ""; Pump(health.CreateVaccineAsync());
                Require(health.VaccineNameError.Contains("nome da vacina") && health.VaccineDateError.Contains("dd/mm/aaaa") && fixture.Count("POST", "/health-records/vaccines") == 0, "Vacina sem nome e sem data foi enviada.");
                health.VaccineName = "Hepatite B"; health.VaccineDate = "15/02/2999"; Pump(health.CreateVaccineAsync());
                Require(health.VaccineDateError.Contains("entre 01/01/1900 e hoje") && fixture.Count("POST", "/health-records/vaccines") == 0, "Data de aplicação no futuro foi enviada.");
                health.VaccineDate = "15/02/2026"; health.VaccineDoseChoice = HealthRecordsViewModel.DoseOptions.Single(option => option.Value == "booster"); health.VaccineNote = "Lote 123"; Pump(health.CreateVaccineAsync());
                var vaccineBody = fixture.HealthBodies.Last(item => item.Action == "vaccine-create").Body;
                Require(vaccineBody.GetProperty("dose").GetString() == "booster" && vaccineBody.GetProperty("appliedOn").GetString() == "2026-02-15" && vaccineBody.GetProperty("cpf").GetString() == patient.Cpf && health.Vaccines.First(item => item.Name == "Hepatite B") is { DoseDisplay: "Reforço", DateDisplay: "Aplicada em 15/02/2026", Note: "Lote 123" },
                    "A vacina deveria ir com a dose (booster) e a data em yyyy-MM-dd.");
                Require(health.Vaccines.Select(item => item.Name).SequenceEqual(["Gripe", "Hepatite B"]) && health.VaccineName == "" && health.VaccineDoseChoice?.Value == "1", "A lista deveria vir da data mais recente e o formulário voltar ao início.");
                foreach (var dose in new[] { "1", "2", "3", "single" })
                {
                    health.VaccineName = "Dose " + dose; health.VaccineDate = "01/01/2026"; health.VaccineDoseChoice = HealthRecordsViewModel.DoseOptions.Single(option => option.Value == dose); Pump(health.CreateVaccineAsync());
                    Require(fixture.VaccinesOf(unit.Id, patient.Cpf).Any(item => item.Name == "Dose " + dose && item.Dose == dose) && health.Vaccines.First(item => item.Name == "Dose " + dose).DoseDisplay == HealthRecordsViewModel.DoseOptions.Single(option => option.Value == dose).Label, $"A dose {dose} não foi gravada e exibida.");
                }
                // Anular: motivo de 3 a 280, confirmação, e o registro sai da lista (volta com Mostrar anulados).
                health.SelectedVaccine = health.Vaccines.First(item => item.Name == "Hepatite B"); Require(health.SelectedExam is null && health.CanVoid, "Selecionar uma vacina deveria soltar o exame e liberar Anular.");
                health.VoidReason = "ab"; Pump(health.VoidSelectedAsync());
                Require(health.VoidReasonError.Contains("3 a 280") && fixture.Count("POST", "/void") == 0, "Motivo curto demais foi enviado.");
                popup.UpdateLayout(); PumpFrames(2); InvokeButton(ButtonIn(popup, "Anular")); popup.UpdateLayout(); Require(health.Panel == HealthPanel.Void, "Anular, no balão, deveria abrir o formulário do motivo no lugar das listas."); health.VoidReason = "Aplicada no paciente errado";
                answer = false; asked.Clear(); InvokeButton(ButtonIn(popup, "Anular registro")); PumpUntil(() => !health.IsBusy);
                Require(asked.Count == 1 && asked[0].Title == "Anular registro" && asked[0].Text.Contains("Hepatite B", StringComparison.Ordinal) && fixture.Count("POST", "/void") == 0, "A anulação deveria pedir confirmação citando o registro, e recusar não anula.");
                answer = true; InvokeButton(ButtonIn(popup, "Anular registro")); PumpUntil(() => !health.IsBusy);
                var voidBody = fixture.HealthBodies.Last(item => item.Action == "vaccine-void").Body;
                Require(fixture.Count("POST", "/void") == 1 && voidBody.GetProperty("confirmed").GetBoolean() && voidBody.GetProperty("reason").GetString() == "Aplicada no paciente errado" && health.Vaccines.All(item => item.Name != "Hepatite B") && health.VoidReason == "", "Confirmar deveria anular com o motivo e tirar da lista.");
                health.IncludeVoided = true; PumpUntil(() => !health.IsBusy);
                var voided = health.Vaccines.Single(item => item.Name == "Hepatite B");
                Require(voided.IsVoided && voided.VoidDisplay.Contains("motivo: Aplicada no paciente errado", StringComparison.Ordinal), "Mostrar anulados deveria exibir o motivo.");
                health.SelectedVaccine = voided; Pump(health.VoidSelectedAsync());
                Require(!health.CanVoid && fixture.Count("POST", "/void") == 1 && health.Message.Contains("já foi anulado", StringComparison.Ordinal), "Um registro anulado não pode ser anulado de novo.");
                health.SelectedExam = health.Exams.First(item => item.Name == "Ultrassom"); health.VoidReason = "Pedido duplicado"; Pump(health.VoidSelectedAsync());
                Require(fixture.Count("POST", "/void") == 2 && health.Exams.Single(item => item.Name == "Ultrassom").IsVoided, "Anular um exame cancelado deveria funcionar (o exame permanece no histórico).");
                // Aparência: a aba nos temas claro e escuro, página inteira.
                health.IncludeVoided = false; PumpUntil(() => !health.IsBusy);
                popup.UpdateLayout();
                Require(Descendants<ListBox>(popup).Any(list => AutomationProperties.GetName(list) == "Exames do paciente") && Descendants<ListBox>(popup).Any(list => AutomationProperties.GetName(list) == "Vacinas do paciente"), "A aba deveria ter as duas listas.");
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-exames-vacinas-light"));
                window.ViewModel.SetAppearance(Appearance.Dark); popup.UpdateLayout();
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-exames-vacinas-dark"));
                var label = Descendants<TextBlock>(popup).First(text => text.IsVisible && text.Text == "Exames");
                Require(Contrast(((SolidColorBrush)label.Foreground).Color, ((SolidColorBrush)Application.Current.FindResource("SurfaceAltBrush")).Color) >= 4.5, "O título da seção deveria ter contraste AA no tema escuro.");
                popup.Close(); popup = null;
                // Cadastro desativado: só consulta.
                var inactive = vm.ObsRegistry.Single(item => item.Cpf == "11144477735");
                var readOnly = vm.CreateHealthRecords(inactive)!;
                var examPosts = fixture.Count("POST", "/health-records/exams"); var vaccinePosts = fixture.Count("POST", "/health-records/vaccines");
                Pump(readOnly.EnsureLoadedAsync()); Pump(readOnly.CreateExamAsync()); Pump(readOnly.CreateVaccineAsync());
                Require(!readOnly.CanRegister && readOnly.HasRegisterNotice && readOnly.IsLoaded && readOnly.MessageKind == MessageKind.Error && fixture.Count("POST", "/health-records/exams") == examPosts && fixture.Count("POST", "/health-records/vaccines") == vaccinePosts, "O cadastro desativado deveria ser só leitura, sem enviar nada.");
                Require(fixture.ExamsOf(unit.Id, inactive.Cpf).Count == 0 && fixture.VaccinesOf(unit.Id, inactive.Cpf).Count == 0, "Nada deveria ser registrado para o cadastro desativado.");
                // Sessão encerrada no meio da ficha: a janela principal sai da conta.
                fixture.UnauthorizedReads = true; Pump(health.ReloadAsync());
                Require(!vm.IsAuthenticated && health.MessageKind == MessageKind.Error, "A sessão encerrada deveria sair da conta e avisar na ficha.");
            }
            finally { Confirmation.Handler = null; popup?.Close(); CloseWindow(window); }
        });
    }
}
