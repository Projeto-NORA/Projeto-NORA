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

/// <summary>Secretaria, seção Comunicação: publicar comunicados (público e alcance), pré-visualização em texto puro, leituras, encerramento e carga paginada.</summary>
internal static partial class Program
{
    private static void RunCommunicationChecks(string storageRoot, string previewRoot)
    {
        Check("Secretaria Comunicação: valida como o servidor e publica pelo pop-up para cada público e alcance com o texto exatamente como digitado, depois de confirmar", () =>
        {
            using var fixture = new ApiFixture();
            var centro = fixture.AddUnit("UBS Centro 1", "Centro"); var norte = fixture.AddUnit("UBS Norte 1", "Norte"); var sul = fixture.AddUnit("UBS Sul 1", "Sul");
            var asked = new List<(string Title, string Text)>(); var answer = false;
            Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return answer; };
            var window = OpenMunicipal(fixture, storageRoot, "communication-publish", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Require(fixture.Count("GET", "/api/municipal/announcements") == 0, "Os comunicados não deveriam ser lidos antes de abrir a seção.");
                var content = Show(window, "communication", 1280, 900);
                Require(fixture.Count("GET", "/api/municipal/announcements") == 1 && vm.MyAnnouncementsState.StartsWith("Você ainda não publicou", StringComparison.Ordinal) && FullTexts(content).Contains(vm.MyAnnouncementsState), "Abrir a seção deveria ler a lista uma vez e explicar a lista vazia.");
                Require(vm.AnnouncementRegionChoices.Select(choice => choice.Label).SequenceEqual(["Centro", "Norte", "Sul"]) && vm.AnnouncementUnitChoices.Count == 3, "As regiões e as UBS da rede deveriam virar opções.");
                // Validação local igual à do servidor.
                Require(!vm.IsAnnouncementDraftValid() && vm.AnnouncementTitleError.Contains("Informe o título") && vm.AnnouncementBodyError.Contains("Informe o texto") && vm.AnnouncementAudienceError.Contains("Escolha para quem") && vm.AnnouncementScopeError.Contains("Escolha o alcance"), "O formulário vazio deveria mostrar os quatro erros.");
                vm.AnnouncementTitle = new string('T', 121); Require(vm.AnnouncementTitleError.Contains("até 120"), "Título com mais de 120 caracteres foi aceito.");
                vm.AnnouncementTitle = "Linha 1\nLinha 2"; Require(vm.AnnouncementTitleError.Contains("quebra de linha"), "Título com quebra de linha foi aceito.");
                vm.AnnouncementTitle = "   "; Require(vm.AnnouncementTitleError.Contains("Informe o título"), "Título só com espaços foi aceito.");
                vm.AnnouncementBodyText = new string('x', 2001); Require(vm.AnnouncementBodyError.Contains("até 2.000"), "Texto com mais de 2.000 caracteres foi aceito.");
                vm.AnnouncementBodyText = "ok\u0007"; Require(vm.AnnouncementBodyError.Contains("controle"), "Texto com caractere de controle foi aceito.");
                vm.AnnouncementBodyText = "linha 1\r\n\tlinha 2"; Require(vm.AnnouncementBodyError == "", "Quebras de linha e tabulação deveriam ser aceitas no texto.");
                vm.AnnouncementScopeKey = "region"; Require(vm.IsAnnouncementDraftValid() == false && vm.AnnouncementScopeError.Contains("ao menos uma região") && vm.ShowRegionChoices && !vm.ShowUnitChoices, "Alcance por região sem região marcada foi aceito.");
                vm.AnnouncementScopeKey = "units"; Require(vm.AnnouncementScopeError.Contains("ao menos uma UBS") && vm.ShowUnitChoices && !vm.ShowRegionChoices, "Alcance por UBS sem UBS marcada foi aceito.");
                vm.AnnouncementExpiryDate = "31/12/2020"; Require(vm.AnnouncementExpiryError.Contains("futuro"), "Validade no passado foi aceita.");
                vm.AnnouncementExpiryDate = "xx"; Require(vm.AnnouncementExpiryError.Contains("dd/mm/aaaa"), "Validade fora do formato foi aceita.");
                vm.AnnouncementExpiryDate = "01/01/2099"; vm.AnnouncementExpiryTime = "25:00"; Require(vm.AnnouncementExpiryError.Contains("HH:mm"), "Hora inválida foi aceita.");
                vm.AnnouncementExpiryDate = ""; vm.AnnouncementExpiryTime = "23:59";
                Pump(vm.PublishAnnouncementAsync());
                Require(vm.MessageKind == MessageKind.Error && fixture.Count("POST", "/api/municipal/announcements") == 0, "Nenhum comunicado inválido deveria ser enviado.");
                // Publicar pelo pop-up: o rascunho abre limpo, o botão Publicar pede a confirmação e só então envia.
                void Publish(Action fill, bool refuseFirst = false, Action<FormDialog>? inspect = null) => InspectHeaderForm(window, "Novo comunicado", dialog =>
                {
                    Require(vm.AnnouncementTitle == "" && vm.AnnouncementAudienceKey is null && dialog.Title == "Novo comunicado", "O pop-up deveria abrir com o rascunho limpo.");
                    fill(); dialog.UpdateLayout(); inspect?.Invoke(dialog);
                    var primary = Descendants<Button>(dialog).Single(button => button.Content as string == "Publicar");
                    if (refuseFirst)
                    {
                        answer = false; var before = asked.Count; InvokeButton(primary); PumpUntil(() => !dialog.IsBusy);
                        Require(dialog.IsVisible && !dialog.Saved && asked.Count == before + 1 && dialog.ErrorMessage.Contains("não foi confirmada") && fixture.Count("POST", "/api/municipal/announcements") == 0, "Recusar a confirmação não publica e deixa o pop-up aberto.");
                    }
                    answer = true; InvokeButton(primary); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved, "O comunicado confirmado deveria ser publicado e o pop-up fechar: " + dialog.ErrorMessage);
                });
                // 1) Equipes, toda a rede, com o texto digitado exatamente (espaços, HTML e tabulação).
                const string typedTitle = "  Mutirão de vacinação  "; const string typedBody = "Linha 1 <b>negrito</b> & mais\r\n  Linha 2 com recuo\t(tab)";
                Publish(() => { vm.AnnouncementTitle = typedTitle; vm.AnnouncementBodyText = typedBody; vm.AnnouncementAudienceKey = "staff"; vm.AnnouncementScopeKey = "all"; }, refuseFirst: true, inspect: dialog =>
                {
                    Require(vm.AnnouncementPreviewMeta == "Para Equipes (OBS) · toda a rede · sem validade", "O resumo da confirmação deveria descrever público, alcance e validade: " + vm.AnnouncementPreviewMeta);
                    Require(Input(dialog, "Título do comunicado").Text == typedTitle && Input(dialog, "Texto do comunicado").Text == typedBody, "O pop-up deveria mostrar o texto exatamente como digitado.");
                    SavePreview(dialog, Path.Combine(previewRoot, "municipal-comunicacao-form-light.png"));
                });
                Require(asked.Count == 2 && asked[0].Title == "Publicar comunicado" && asked[0].Text.Contains("Mutirão de vacinação") && asked[0].Text.Contains("Para Equipes (OBS) · toda a rede"), "A publicação deveria pedir confirmação com o resumo.");
                var sent = fixture.AnnouncementBodies[0];
                Require(fixture.Count("POST", "/api/municipal/announcements") == 1 && sent.GetProperty("title").GetString() == typedTitle && sent.GetProperty("body").GetString() == typedBody && sent.GetProperty("audience").GetString() == "staff" && sent.GetProperty("scope").GetString() == "all"
                    && sent.GetProperty("regions").ValueKind == System.Text.Json.JsonValueKind.Null && sent.GetProperty("unitIds").ValueKind == System.Text.Json.JsonValueKind.Null && sent.GetProperty("expiresAt").ValueKind == System.Text.Json.JsonValueKind.Null, "O texto deveria ir exatamente como digitado, sem aparar nem converter, e sem região, UBS ou validade.");
                Require(vm.AnnouncementTitle == "" && vm.AnnouncementAudienceKey is null && vm.MyAnnouncements.Single() is { AudienceDisplay: "Equipes (OBS)", ScopeDisplay: "Toda a rede", StatusDisplay: "Em vigor", ValidityDisplay: "Sem validade" } && vm.MessageKind == MessageKind.Success, "Publicar deveria limpar o formulário e listar o comunicado: " + vm.Message);
                // A ficha mostra o texto completo, em texto puro, na cor do tema.
                var details = Await(vm.BuildMunicipalDetailsAsync(vm.MyAnnouncements.Single())) ?? throw new InvalidOperationException("A ficha do comunicado não abriu.");
                Require(details.Fields.Any(field => field.Label == "Texto" && field.Value == typedBody) && details.Fields.Any(field => field.Label == "Título" && field.Value == typedTitle), "A ficha deveria trazer título e texto exatamente como digitados.");
                // 2) Pacientes, duas regiões.
                Publish(() =>
                {
                    vm.AnnouncementTitle = "Campanha nas regiões"; vm.AnnouncementBodyText = "Texto"; vm.AnnouncementAudienceKey = "patients"; vm.AnnouncementScopeKey = "region";
                    vm.AnnouncementRegionChoices.Single(choice => choice.Label == "Sul").IsChecked = true; vm.AnnouncementRegionChoices.Single(choice => choice.Label == "Centro").IsChecked = true;
                    Require(vm.AnnouncementPreviewMeta == "Para Pacientes · região Centro, Sul · sem validade", "O resumo deveria listar as regiões marcadas: " + vm.AnnouncementPreviewMeta);
                });
                var regionBody = fixture.AnnouncementBodies[1];
                Require(regionBody.GetProperty("audience").GetString() == "patients" && regionBody.GetProperty("scope").GetString() == "region" && regionBody.GetProperty("regions").EnumerateArray().Select(item => item.GetString()).SequenceEqual(["Centro", "Sul"]) && regionBody.GetProperty("unitIds").ValueKind == System.Text.Json.JsonValueKind.Null, "O alcance por região deveria enviar só as regiões marcadas.");
                // 3) Equipes e pacientes, duas UBS, com validade.
                Publish(() =>
                {
                    vm.AnnouncementTitle = "Aviso das UBS"; vm.AnnouncementBodyText = "Texto"; vm.AnnouncementAudienceKey = "both"; vm.AnnouncementScopeKey = "units";
                    vm.AnnouncementUnitChoices.Single(choice => choice.Key == norte.Id.ToString()).IsChecked = true; vm.AnnouncementUnitChoices.Single(choice => choice.Key == sul.Id.ToString()).IsChecked = true;
                    vm.AnnouncementExpiryDate = "20/12/2099"; vm.AnnouncementExpiryTime = "08:30";
                });
                var unitsBody = fixture.AnnouncementBodies[2];
                Require(unitsBody.GetProperty("audience").GetString() == "both" && unitsBody.GetProperty("scope").GetString() == "units" && unitsBody.GetProperty("unitIds").EnumerateArray().Select(item => Guid.Parse(item.GetString()!)).ToHashSet().SetEquals([norte.Id, sul.Id]) && unitsBody.GetProperty("expiresAt").GetDateTimeOffset().LocalDateTime == new DateTime(2099, 12, 20, 8, 30, 0), "O alcance por UBS deveria enviar as UBS marcadas e a validade no horário local.");
                var third = vm.MyAnnouncements.Single(item => item.Title == "Aviso das UBS");
                Require(third.AudienceDisplay == "Equipes e pacientes" && third.ScopeDisplay == "UBS: UBS Norte 1, UBS Sul 1" && third.ValidityDisplay == "20/12/2099 08:30" && vm.MyAnnouncements.Count == 3, "A lista deveria mostrar público, alcance com os nomes das UBS e a validade: " + third.ScopeDisplay);
                Require(third.MetaLine.StartsWith("Equipes e pacientes | UBS: UBS Norte 1, UBS Sul 1 | Publicado ", StringComparison.Ordinal) && third.MetaLine.EndsWith(" | Validade 20/12/2099 08:30", StringComparison.Ordinal), "A linha de dados deveria trazer público, alcance, publicação e validade: " + third.MetaLine);
                // Validação do servidor continua valendo: o texto enviado precisa voltar como o servidor guardou.
                Require(fixture.Announcements.Select(item => item.Body).Contains(typedBody), "O servidor deveria ter guardado o texto exatamente como veio.");
                content = Layout(window, 1280, 900);
                Require(Descendants<Border>(content).All(border => AutomationProperties.GetName(border) != "Pré-visualização do comunicado") && Descendants<Button>(content).All(button => button.Content?.ToString() is not ("Atualizar lista" or "Limpar formulário")), "A tela não deveria ter a pré-visualização nem botões de atualizar.");
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 900);
                SavePreview(content, Path.Combine(previewRoot, "municipal-comunicacao-dark.png"));
                window.ViewModel.SetAppearance(Appearance.System);
                Require(Descendants<ListBox>(window).Any(list => AutomationProperties.GetName(list) == "Comunicados da Secretaria" && list.Items.Count == 3), "O tema Sistema deveria renderizar a lista.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
        Check("Secretaria Comunicação: lista os meus comunicados com leituras por público, mostra quem da equipe leu na ficha, encerra pelo balão (um ou vários) com confirmação e carrega os mais antigos", () =>
        {
            using var fixture = new ApiFixture();
            var centro = fixture.AddUnit("UBS Centro 1", "Centro"); fixture.AddUnit("UBS Norte 1", "Norte");
            var now = DateTimeOffset.UtcNow;
            var staffOnly = fixture.AddAnnouncement("Para equipes", "texto A", "staff"); staffOnly.OperatorReadAt = now.AddMinutes(-30);
            staffOnly.OtherStaff.Add(new("Bruno Alves", now.AddMinutes(-20))); staffOnly.OtherStaff.Add(new("Carla Dias", now.AddMinutes(-10)));
            var both = fixture.AddAnnouncement("Para os dois", "texto B", "both", "region", ["Centro"]); both.PatientReads = 12; both.OtherStaff.Add(new("Bruno Alves", now.AddMinutes(-5)));
            var patientsOnly = fixture.AddAnnouncement("Para pacientes", "texto C", "patients"); patientsOnly.PatientReads = 40;
            fixture.AddAnnouncement("Já encerrado", "texto D", "staff").ClosedAt = now.AddDays(-1);
            fixture.AddAnnouncement("Já venceu", "texto E", "staff", expiresAt: now.AddDays(-2));
            var asked = new List<(string Title, string Text)>(); var answer = false;
            Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return answer; };
            var window = OpenMunicipal(fixture, storageRoot, "communication-list", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "communication", 1280, 900);
                var list = ListNamed(content, "Comunicados da Secretaria");
                var rows = vm.MyAnnouncements.ToDictionary(item => item.Title);
                Require(rows["Para equipes"] is { StatusDisplay: "Em vigor", StaffReadsDisplay: "3", PatientReadsDisplay: "—", AudienceDisplay: "Equipes (OBS)" }, "As leituras de um comunicado só para equipes: equipes 3, pacientes —.");
                Require(rows["Para os dois"] is { StaffReadsDisplay: "1", PatientReadsDisplay: "12", ScopeDisplay: "Região: Centro" } && rows["Para pacientes"] is { StaffReadsDisplay: "—", PatientReadsDisplay: "40" }, "As leituras por público deveriam aparecer separadas.");
                Require(rows["Já encerrado"].StatusDisplay == "Encerrado" && rows["Já venceu"].StatusDisplay == "Vencido" && !rows["Já encerrado"].CanClose && !rows["Já venceu"].CanClose, "Encerrado e vencido deveriam aparecer com a situação e sem poder encerrar.");
                Require(new[] { "Em vigor", "Encerrado", "Vencido" }.All(VisibleTexts(list).Contains) && VisibleTexts(list).Any(text => text.StartsWith("Equipes (OBS) | Toda a rede | Publicado ", StringComparison.Ordinal)), "A listagem deveria mostrar a etiqueta de situação e a linha de dados.");
                // Quem da equipe leu: na ficha, lida sob demanda (uma requisição); só para pacientes não lista nomes.
                Require(fixture.Count("GET", "/reads") == 0, "As leituras só são lidas sob demanda.");
                var ficha = Await(vm.BuildMunicipalDetailsAsync(rows["Para equipes"])) ?? throw new InvalidOperationException("A ficha do comunicado não abriu.");
                var readers = ficha.Fields.Where(field => field.Group == "Quem da equipe leu").ToArray();
                Require(fixture.Count("GET", "/reads") == 1 && readers.Select(field => field.Label).ToHashSet().IsSupersetOf(["Bruno Alves", "Carla Dias", "Paciente Fixture"]) && readers.Any(field => field.Value.Length > 0 && field.Label == "Resumo" && field.Value.StartsWith("Equipes: 3 · Pacientes: 0", StringComparison.Ordinal)), "A ficha deveria trazer as três contas que leram: " + string.Join(", ", readers.Select(field => field.Label)));
                var patientsFicha = Await(vm.BuildMunicipalDetailsAsync(rows["Para pacientes"]))!;
                Require(fixture.Count("GET", "/reads") == 1 && patientsFicha.Fields.Any(field => field.Group == "Quem da equipe leu" && field.Value.Contains("só para pacientes", StringComparison.Ordinal)), "Um comunicado só para pacientes não lista quem leu (só a contagem).");
                // Encerrar pelo balão (lixeira): confirmação, 409 de outra pessoa e estado final.
                SelectRows(window, list, rows["Para equipes"]);
                Require(BalloonButton(list, "Encerrar comunicado").IsVisible && BalloonButton(list, "Ver ficha").IsVisible, "O balão deveria oferecer Ver ficha e Encerrar para um comunicado em vigor.");
                InvokeButton(BalloonButton(list, "Encerrar comunicado"));
                Require(asked.Count == 1 && asked[0].Title == "Encerrar comunicado" && asked[0].Text.Contains("Para equipes") && fixture.Count("POST", "/close") == 0 && vm.MyAnnouncements.Single(item => item.Title == "Para equipes").StatusDisplay == "Em vigor", "Encerrar deveria pedir confirmação, e recusar não encerra.");
                answer = true; InvokeButton(BalloonButton(list, "Encerrar comunicado"));
                Require(fixture.Count("POST", "/close") == 1 && vm.MyAnnouncements.Single(item => item.Title == "Para equipes").StatusDisplay == "Encerrado" && vm.Message.Contains("encerrado"), "Confirmar deveria encerrar e mostrar Encerrado: " + vm.Message);
                Layout(window, 1280, 900);
                Require(!BalloonButton(list, "Encerrar comunicado").IsVisible, "Com o comunicado já encerrado a ação Encerrar deveria sumir do balão.");
                SelectRows(window, list, vm.MyAnnouncements.Single(item => item.Title == "Para os dois")); fixture.Announcements.Single(item => item.Title == "Para os dois").ClosedAt = now;
                InvokeButton(BalloonButton(list, "Encerrar comunicado"));
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("já foi encerrado", StringComparison.Ordinal) && vm.MyAnnouncements.Single(item => item.Title == "Para os dois").StatusDisplay == "Encerrado", "Encerrar o que outra pessoa já encerrou deveria avisar e atualizar a lista: " + vm.Message);
                // Vários de uma vez: uma só confirmação.
                fixture.AddAnnouncement("Lote 1", "t", "staff"); fixture.AddAnnouncement("Lote 2", "t", "staff");
                Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 900);
                SelectRows(window, list, vm.MyAnnouncements.Single(item => item.Title == "Lote 1"), vm.MyAnnouncements.Single(item => item.Title == "Lote 2"));
                Require(!BalloonButton(list, "Ver ficha").IsVisible && ListingBehavior.GetBalloon(list)!.Label == "2 comunicados selecionados", "Com vários selecionados a ficha some do balão e o rótulo conta os itens.");
                var closes = fixture.Count("POST", "/close"); var questions = asked.Count;
                InvokeButton(BalloonButton(list, "Encerrar comunicado"));
                Require(asked.Count == questions + 1 && asked[^1].Text.Contains("2 comunicados") && fixture.Count("POST", "/close") == closes + 2 && vm.MyAnnouncements.Where(item => item.Title.StartsWith("Lote", StringComparison.Ordinal)).All(item => item.StatusDisplay == "Encerrado"), "Encerrar dois deveria pedir uma só confirmação e encerrar os dois.");
                // Ciclo: a lista é relida a cada ciclo (20 s) e também pela releitura depois de uma ação.
                var reads = fixture.Count("GET", "/api/municipal/announcements"); for (var cycle = 0; cycle < 2; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/api/municipal/announcements") == reads + 2, "O ciclo automático deveria reler a lista a cada ciclo.");
                // Paginação por cursor: 50 por página e "Carregar mais antigos".
                for (var index = 0; index < 55; index++) fixture.AddAnnouncement("Antigo " + index, "t", "patients");
                vm.RefreshCommand.Execute(null); Settle(vm);
                Require(vm.MyAnnouncements.Count == 50 && vm.CanLoadMoreAnnouncements, "A primeira página deveria ter 50 comunicados e oferecer mais.");
                content = Layout(window, 1280, 900); Click(window, content, "Carregar mais antigos", 1280, 900);
                Require(vm.MyAnnouncements.Count == 62 && !vm.CanLoadMoreAnnouncements && vm.MyAnnouncements.Select(item => item.Id).Distinct().Count() == 62, "A segunda página deveria completar os 62 (5 do começo, 2 do lote e 55 antigos) sem repetir nem faltar.");
                window.ViewModel.SetAppearance(Appearance.Dark); Layout(window, 1280, 900);
                SelectRows(window, list, vm.MyAnnouncements.Single(item => item.Title == "Para pacientes"));
                SavePreview(Layout(window, 1280, 900), Path.Combine(previewRoot, "municipal-comunicacao-balao-dark.png"));
                vm.LogoutCommand.Execute(null); Settle(vm);
                Require(vm.MyAnnouncements.Count == 0 && vm.AnnouncementRegionChoices.Count == 0, "Sair deveria limpar a seção.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
        RunMunicipalCadastrosChecks(storageRoot);
        RunMunicipalCadastrosGallery(storageRoot, previewRoot);
    }
}
