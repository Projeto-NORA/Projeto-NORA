using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunMessageChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: faixa de retorno aparece em todas as abas e não se repete em cópias locais", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Faixa"); fixture.AddRegistry(unit.Id, "Ana Souza");
            var window = new ShellWindow(profile, Path.Combine(storageRoot, "message-strip", profile.Id), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; PrepareWindow(window);
                var vm = window.ViewModel.Workspace; vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                var routes = profile.IsMunicipal
                    ? new[] { "home", "units", "specialties", "network", "indicators", "slots", "capacity", "alerts", "reports", "communication", "users", "settings" }
                    : new[] { "home", "agenda", "indicators", "alerts", "patients", "slots", "unit-settings", "settings" };
                foreach (var route in routes)
                {
                    window.ViewModel.Navigate(route);
                    Require(!vm.HasMessage, "Mudar de aba deveria apagar a mensagem de sucesso anterior: " + route);
                    Settle(vm);
                    Layout(window, 1280, 740); Settle(vm);
                    vm.RefreshCommand.Execute(null); Settle(vm);
                    var content = Layout(window, 1280, 740);
                    var bar = Descendants<Border>(content).Single(border => AutomationProperties.GetName(border) == "Retorno da ação");
                    Require(bar.IsVisible && bar.ActualHeight > 0, "A faixa de retorno não aparece na aba: " + route);
                    Require(VisibleTexts(bar).Any(text => text.StartsWith("Sucesso: Dados atualizados pela API.", StringComparison.Ordinal)), "A faixa não mostra tipo e texto do sucesso: " + route);
                    Require(VisibleTexts(content).Count(text => text.Contains("Dados atualizados pela API.", StringComparison.Ordinal)) == 1, "A mensagem aparece duplicada na aba: " + route);
                    if (route is "home" or "indicators") SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-faixa-sucesso-{route}.png"));
                }
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Retorno: tipos distinguíveis por ícone e texto, erro fica até a próxima ação ou até ser dispensado, sucesso some ao trocar de seção", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Tipos", "Sul"); fixture.AddUnit("UBS Outra", "Norte");
            var window = OpenOperational(fixture, storageRoot, "message-kinds", Appearance.Dark);
            try
            {
                var vm = LoginObs(window);
                vm.CandidateObsUnit = unit; Pump(vm.ConfirmObsUnitAsync());
                var icons = new List<string>(); var labels = new List<string>();
                void Observe(MessageKind kind) { Require(vm.MessageKind == kind, $"Tipo esperado {kind}, recebido {vm.MessageKind}: {vm.Message}"); icons.Add(vm.MessageIcon); labels.Add(vm.MessageKindLabel); }
                Observe(MessageKind.Success);
                vm.SuggestClosestObsUnit(-23.5, -46.6); Observe(MessageKind.Info);
                var content = Layout(window, 1280, 740);
                SavePreview(content, Path.Combine(previewRoot, "obs-faixa-informacao-dark.png"));
                fixture.UnavailableUnits.Add(unit.Id); vm.CandidateObsUnit = unit;
                Pump(vm.ConfirmObsUnitAsync()); Observe(MessageKind.Error);
                Require(icons.Distinct().Count() == 3 && labels.SequenceEqual(["Sucesso", "Informação", "Erro"]), "Os três tipos não se distinguem por ícone e texto.");
                content = Layout(window, 1280, 740);
                var bar = Descendants<Border>(content).Single(border => AutomationProperties.GetName(border) == "Retorno da ação");
                Require(VisibleTexts(bar).Any(text => text.StartsWith("Erro: ", StringComparison.Ordinal) && text.Contains("indisponível")), "Faixa não mostra 'Erro' e a explicação.");
                SavePreview(content, Path.Combine(previewRoot, "obs-faixa-erro-dark.png"));
                window.ViewModel.Navigate("queue"); Settle(vm);
                Require(vm.HasMessage && vm.MessageKind == MessageKind.Error, "O erro deveria permanecer ao trocar de seção.");
                content = Layout(window, 1280, 740);
                var dismiss = Descendants<Button>(content).Single(button => AutomationProperties.GetName(button) == "Dispensar mensagem");
                Require(dismiss.IsVisible && dismiss.Focusable && dismiss.IsTabStop && dismiss.IsEnabled, "Botão de dispensar não é acessível por teclado.");
                InvokeButton(dismiss);
                Require(!vm.HasMessage, "Dispensar não limpou a mensagem.");
                Pump(vm.ConfirmObsUnitAsync()); Require(vm.MessageKind == MessageKind.Error, "Falha repetida deveria mostrar erro de novo, por ação do usuário.");
                fixture.UnavailableUnits.Clear();
                Pump(vm.ConfirmObsUnitAsync()); Observe(MessageKind.Success);
                Require(!vm.Message.Contains("indisponível"), "A próxima ação deveria substituir o erro anterior.");
                window.ViewModel.Navigate("home");
                Require(!vm.HasMessage, "Sucesso não foi apagado ao trocar de seção.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: barra de progresso acompanha só ação do usuário; atualização automática é silenciosa", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Progresso"); fixture.AddRegistry(unit.Id, "Ana Souza");
            var window = OpenOperational(fixture, storageRoot, "progress", Appearance.Light);
            Task? automatic = null;
            try
            {
                var vm = LoginObs(window);
                var content = Layout(window, 1280, 740);
                var progress = Descendants<ProgressBar>(content).Single(bar => AutomationProperties.GetName(bar) == "Operação em andamento");
                Require(!progress.IsVisible && !vm.ShowProgress, "Barra de progresso visível sem operação.");
                fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.RefreshCommand.Execute(null); PumpUntil(() => vm.IsBusy); content = Layout(window, 1280, 740);
                Require(vm.ShowProgress && progress.IsVisible && progress.IsIndeterminate && !vm.HasMessage, "Ação do usuário em andamento não acendeu a barra de progresso.");
                SavePreview(content, Path.Combine(previewRoot, "obs-progresso-acao-light.png"));
                fixture.PauseReads.TrySetResult(); Settle(vm); Layout(window, 1280, 740);
                Require(!progress.IsVisible && vm.MessageKind == MessageKind.Success, "A barra deveria apagar ao concluir a ação.");
                fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                automatic = vm.RefreshAutomaticallyAsync(); PumpUntil(() => vm.IsBusy); Layout(window, 1280, 740);
                Require(!vm.ShowProgress && !progress.IsVisible && vm.CanOperate && vm.CanEditDraft, "A atualização automática acendeu a barra ou bloqueou a tela.");
                fixture.PauseReads.TrySetResult(); Pump(automatic);
            }
            finally { fixture.PauseReads?.TrySetResult(); if (automatic != null) Pump(automatic); window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: formulário continua editável durante a atualização automática e o clique do usuário aguarda o ciclo e executa", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Ciclo"); fixture.AddRegistry(unit.Id, "Ana Souza");
            var window = OpenOperational(fixture, storageRoot, "draft-during-refresh", Appearance.Light);
            Task? refresh = null;
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("patients"); Settle(vm);
                var content = Layout(window, 980, 640);
                
                content = Layout(window, 980, 640);
                var name = Input(content, "Nome no cadastro da UBS");
                name.Text = "Digitado antes do ciclo";
                Require(name.IsEnabled, "Cadastro estava desabilitado antes da reprodução.");
                fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                refresh = vm.RefreshAutomaticallyAsync(); PumpUntil(() => vm.IsBusy);
                Layout(window, 980, 640);
                Require(name.IsEnabled && name.Focusable && vm.CanEditDraft && vm.CanOperate && !vm.ShowProgress, "O ciclo automático desabilitou o cadastro ou acendeu a barra.");
                name.Text = "Digitado durante o ciclo";
                Require(vm.ObsRegistryName == name.Text, "Cadastro não recebeu a digitação durante o ciclo.");
                vm.ObsRegistryCpf = "11144477735"; vm.ObsRegistryEmail = "novo@example.test";
                var save = FindActionButton(content, "Cadastrar paciente");
                Require(save.IsEnabled, "O botão de envio foi bloqueado pelo ciclo automático.");
                InvokeButton(save);
                PumpUntil(() => vm.ShowProgress);
                Require(fixture.Count("POST", "/registry") == 0 && vm.ObsRegistryName == "Digitado durante o ciclo", "O clique executou antes do ciclo terminar ou perdeu a digitação.");
                fixture.PauseReads.TrySetResult();
                PumpUntil(() => !vm.IsBusy);
                Pump(refresh);
                Require(fixture.Count("POST", "/registry") == 1 && vm.ObsRegistry.Any(item => item.FullName == "Digitado durante o ciclo") && vm.MessageKind == MessageKind.Success, "O clique feito durante a atualização foi descartado em silêncio: " + vm.Message);
            }
            finally { fixture.PauseReads?.TrySetResult(); if (refresh != null) Pump(refresh); window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: falha de leitura automática aparece uma vez, marca os dados como desatualizados e some ao recuperar", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Falha"); fixture.AddRegistry(unit.Id, "Ana Souza");
            var window = OpenOperational(fixture, storageRoot, "refresh-failure", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var shown = new List<string>();
                vm.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(WorkspaceViewModel.Message) && vm.Message.Length > 0) shown.Add(vm.Message); };
                vm.SelectedObsRegistry = vm.ObsRegistry.Single();
                Require(!vm.IsDataStale && vm.StaleLabel == "", "Dados recém-lidos já nasceram marcados como antigos.");
                fixture.FailReads = true;
                for (var cycle = 0; cycle < 3; cycle++) Pump(vm.RefreshAutomaticallyAsync());
                Require(shown.Count == 1 && vm.MessageKind == MessageKind.Error, $"O mesmo erro foi mostrado {shown.Count} vezes em três ciclos.");
                Require(vm.IsDataStale && vm.StaleLabel.Contains("Última leitura bem-sucedida às") && vm.ObsRegistry.Count == 1 && vm.SelectedObsRegistry != null, "Dados não ficaram marcados como desatualizados mantendo o que já havia.");
                var content = Layout(window, 1280, 740);
                var notice = Descendants<Border>(content).Single(border => AutomationProperties.GetName(border) == "Aviso de dados desatualizados");
                Require(notice.IsVisible && VisibleTexts(notice).Any(text => text.StartsWith("Dados possivelmente desatualizados", StringComparison.Ordinal)), "Aviso de dados desatualizados não aparece na tela.");
                SavePreview(content, Path.Combine(previewRoot, "obs-dados-desatualizados-light.png"));
                vm.DismissMessageCommand.Execute(null);
                Pump(vm.RefreshAutomaticallyAsync());
                Require(shown.Count == 1 && !vm.HasMessage && vm.IsDataStale, "Erro dispensado voltou a cada ciclo.");
                fixture.FailReads = false; fixture.UnavailableUnits.Add(unit.Id);
                Pump(vm.RefreshAutomaticallyAsync());
                Require(shown.Count == 2 && vm.Message.Contains("indisponível"), "Um erro diferente deveria ser mostrado.");
                fixture.UnavailableUnits.Clear();
                Pump(vm.RefreshAutomaticallyAsync());
                Require(!vm.HasMessage && !vm.IsDataStale && vm.StaleLabel == "" && vm.SelectedObsRegistry != null, "Recuperação manteve erro antigo, aviso de dados antigos ou perdeu a seleção.");
                fixture.UnauthorizedReads = true;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.IsSignedOut && vm.ObsRegistry.Count == 0 && vm.ActiveObsUnit == null && vm.Message.Contains("sessão terminou") && vm.MessageKind == MessageKind.Error, "401 não encerrou a sessão com mensagem visível.");
                var calls = fixture.Requests.Count;
                Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Requests.Count == calls, "Atualização continuou usando a sessão encerrada.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: o ciclo automático lê só o que a seção atual precisa e não recria listas sem mudança", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Escopo"); fixture.AddRegistry(unit.Id, "Ana Souza");
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Clínica", DateTimeOffset.UtcNow.AddHours(-4).AddSeconds(-30));
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Clínica", "Prof", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(30), "open", DateTimeOffset.UtcNow));
            fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(DateTime.Today.AddHours(12)));
            var window = OpenOperational(fixture, storageRoot, "refresh-scope", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                (int Registry, int Queue, int Slots, int Agenda) Snapshot() => (fixture.Count("GET", "/registry"), fixture.Count("GET", "/waiting-list"), fixture.Count("GET", "/lightning-slots"), fixture.Count("GET", "/appointments"));
                void Cycle(string route, (int Registry, int Queue, int Slots, int Agenda) delta)
                {
                    window.ViewModel.Navigate(route); Settle(vm);
                    var before = Snapshot(); Pump(vm.RefreshAutomaticallyAsync()); var after = Snapshot();
                    var actual = (after.Registry - before.Registry, after.Queue - before.Queue, after.Slots - before.Slots, after.Agenda - before.Agenda);
                    Require(actual == delta, $"{route}: leituras do ciclo {actual}, esperadas {delta}.");
                }
                var changes = 0;
                vm.ObsRegistry.CollectionChanged += (_, _) => changes++;
                vm.UnitLightningSlots.CollectionChanged += (_, _) => changes++; vm.Appointments.CollectionChanged += (_, _) => changes++;
                vm.SelectedObsRegistry = vm.ObsRegistry.Single();
                Cycle("home", (1, 0, 1, 1)); // a fila manual não é mais lida (Vaga Relâmpago automática)
                Cycle("patients", (1, 0, 0, 0));
                Cycle("queue", (0, 0, 1, 0)); // "queue" leva ao painel da Vaga Relâmpago
                Cycle("slots", (0, 0, 1, 0));
                Cycle("agenda", (0, 0, 0, 1));
                Cycle("indicators", (0, 0, 0, 0));
                Cycle("alerts", (0, 0, 0, 0));
                Cycle("settings", (0, 0, 0, 0));
                Require(changes == 0 && vm.SelectedObsRegistry?.FullName == "Ana Souza", $"Atualizações sem mudança recriaram listas ({changes} eventos) ou perderam a seleção.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS: exceção inesperada vira mensagem genérica na faixa comum, sem detalhes técnicos", () =>
        {
            using var fixture = new ApiFixture();
            var window = OpenOperational(fixture, storageRoot, "unexpected-error");
            try
            {
                var vm = window.ViewModel.Workspace;
                vm.ReportUnexpected(new InvalidOperationException("detalhe-interno-secreto"));
                Require(vm.MessageKind == MessageKind.Error && vm.Message.Contains("erro inesperado") && !vm.Message.Contains("detalhe-interno-secreto"), "Exceção inesperada não virou mensagem genérica: " + vm.Message);
                var content = Layout(window, 980, 640);
                Require(Descendants<Border>(content).Any(border => AutomationProperties.GetName(border) == "Retorno da ação" && border.IsVisible), "A faixa comum não mostrou o erro inesperado.");
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
    }
}
