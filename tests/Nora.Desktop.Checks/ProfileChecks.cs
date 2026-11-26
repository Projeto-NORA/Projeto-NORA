using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Menu do perfil (Trocar perfil só no OBS, Configurações, Dados do perfil e Sair da conta, com ícones desenhados), pop-up "Trocar perfil", abertura sozinha do pop-up
/// quando a conta tem várias UBS e nenhuma ativa, e o login salvo nos desktops (a sessão é sempre gravada e restaurada dentro do prazo).
/// </summary>
internal static partial class Program
{
    /// <summary>Abre o pop-up "Trocar perfil" por <paramref name="open"/> (padrão: o método da janela) e roda <paramref name="inspect"/> com a lista já lida.</summary>
    private static void InspectSwitchProfile(ShellWindow window, Action<SwitchProfileDialog> inspect, Action? open = null) =>
        InspectModal<SwitchProfileDialog>(window, dialog =>
        {
            PumpUntil(() => !dialog.IsLoading);
            dialog.UpdateLayout();
            inspect(dialog);
        }, open ?? window.OpenSwitchProfile);

    /// <summary>
    /// Para o pop-up que abre sozinho (depois do login ou de restaurar a sessão): <paramref name="start"/> faz o login ou abre a janela, e um temporizador acha o pop-up
    /// entre as janelas da dona, roda <paramref name="inspect"/> e o fecha. Falha se o pop-up não abrir.
    /// </summary>
    private static void InspectAutoOpened(ShellWindow window, Action<SwitchProfileDialog> inspect, Action start)
    {
        var observed = false; string? error = null;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var dialog = window.OwnedWindows.OfType<SwitchProfileDialog>().SingleOrDefault();
            if (dialog is null) { if (DateTime.UtcNow > deadline) timer.Stop(); return; }
            timer.Stop();
            observed = true;
            try
            {
                PumpUntil(() => !dialog.IsLoading);
                dialog.UpdateLayout();
                inspect(dialog);
            }
            catch (Exception failure) { error = failure.Message; }
            finally { if (dialog.IsVisible) dialog.Close(); }
        };
        timer.Start();
        try { start(); }
        finally { timer.Stop(); }
        var state = window.ViewModel.Workspace;
        Require(observed && error == null, error ?? $"O pop-up Trocar perfil não abriu sozinho (pendente: {state.ProfileChoicePending}, UBS: {state.ObsUnits.Count}, escolha: {state.NeedsObsUnitChoice}, operando: {state.CanOperate}, janela carregada: {window.IsLoaded}, entrou: {state.IsAuthenticated}, mensagem: {state.Message}).");
    }

    private static ShellWindow NewDesktopWindow(ApiFixture fixture, DesktopProfile profile, string storage, ObsDeviceStore devices, bool show = true)
    {
        var window = new ShellWindow(profile, storage, new NoraApiClient(fixture), devices, compactAccess: true);
        window.ViewModel.ConfirmClose = false;
        window.ViewModel.Animations = false;
        window.ViewModel.Workspace.Endpoint = "https://fixture.invalid";
        if (show) PrepareWindow(window);
        return window;
    }

    /// <summary>Preenche a tela de acesso compacta e aciona Entrar (a caixa "Manter conectado por 30 dias" só marca se pedida).</summary>
    private static void SignInDesktop(ShellWindow window, bool keep = false)
    {
        var content = Layout(window, 1080, 740);
        var access = Descendants<DesktopAccessView>(content).Single(view => view.IsVisible);
        Descendants<CheckBox>(access).Single(box => box.IsVisible).IsChecked = keep;
        Descendants<TextBox>(access).Single(box => box.IsVisible).Text = "fixture@example.invalid";
        Descendants<PasswordBox>(access).Single(box => box.IsVisible).Password = "isolated-test-password";
        InvokeButton(FindActionButton(access, "Entrar"));
    }

    private static void RunProfileChecks(Application application, ThemeService themes, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);

        Check("Menu do perfil: OBS mostra Trocar perfil, Configurações, Dados do perfil e Sair da conta com ícones desenhados; a Secretaria, os três últimos; o rodapé não tem mais o botão Configurações", () =>
        {
            foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
            {
                var window = new ShellWindow(profile, Path.Combine(storageRoot, "profile-menu", profile.Id));
                try
                {
                    window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                    PrepareWindow(window);
                    var root = Layout(window, 1280, 740);
                    Require(!Descendants<Button>(root).Any(button => AutomationProperties.GetName(button) == "Configurações"), $"{profile.Id}: o rodapé da barra lateral ainda tem o botão Configurações.");
                    var account = Descendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "Menu da conta NORA");
                    var menu = account.ContextMenu!;
                    var expected = profile.IsMunicipal ? new[] { "Configurações", "Dados do perfil", "Sair da conta" } : ["Trocar perfil", "Configurações", "Dados do perfil", "Sair da conta"];
                    var icons = new Dictionary<string, string> { ["Trocar perfil"] = "IconSwapGeometry", ["Configurações"] = "IconGearGeometry", ["Dados do perfil"] = "IconPersonGeometry", ["Sair da conta"] = "IconLogoutGeometry" };
                    foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                    {
                        window.ViewModel.SetAppearance(mode);
                        InvokeButton(account);
                        menu.UpdateLayout();
                        var items = menu.Items.Cast<MenuItem>().ToArray();
                        Require(menu.IsOpen && items.Select(item => item.Header?.ToString()).SequenceEqual(expected), $"{profile.Id}/{mode}: itens do menu fora de ordem ou faltando: {string.Join(", ", items.Select(item => item.Header))}.");
                        foreach (var item in items)
                        {
                            var name = item.Header!.ToString()!;
                            Require(item.Icon is System.Windows.Shapes.Path { Data: not null, Stroke: not null } icon && ReferenceEquals(icon.Data, application.FindResource(icons[name])) && AutomationProperties.GetName(item) == name,
                                $"{profile.Id}/{mode}: {name} deveria ter o ícone desenhado ({icons[name]}) e o nome de automação (ícone: {item.Icon?.GetType().Name}, traço: {(item.Icon as System.Windows.Shapes.Path)?.Stroke}).");
                        }
                        Require(items.Select(item => ((System.Windows.Shapes.Path)item.Icon).Data.ToString()).Distinct().Count() == items.Length, $"{profile.Id}/{mode}: dois itens usam o mesmo ícone.");
                        SavePreview(menu, Path.Combine(previewRoot, $"{profile.Id}-menu-do-perfil-{mode.ToString().ToLowerInvariant()}.png"));
                        menu.IsOpen = false;
                    }
                }
                finally { CloseWindow(window); }
            }
        });

        Check("Trocar perfil: lista as UBS da conta com a ativa marcada e selecionada, Trocar troca e carrega os dados, X e Esc fecham sem trocar, e a mesma UBS não troca", () =>
        {
            using var fixture = new ApiFixture();
            var north = fixture.AddUnit("UBS Central", "Norte", -23.4, -46.6);
            var south = fixture.AddUnit("UBS Central", "Sul", -23.9, -46.3);
            fixture.AddRegistry(north.Id, "Paciente Norte");
            fixture.AddRegistry(south.Id, "Paciente Sul 1", "11144477735");
            var window = OpenOperational(fixture, storageRoot, "switch-profile");
            try
            {
                var vm = LoginObs(window);
                Require(vm.ActiveObsUnit == null && vm.ObsUnits.Count == 2, "Cenário inicial incorreto.");
                vm.CandidateObsUnit = north; Pump(vm.ConfirmObsUnitAsync());
                Require(vm.ActiveObsUnit?.Id == north.Id && vm.ObsRegistry.Single().FullName == "Paciente Norte", "Cenário inicial: a UBS Norte deveria estar ativa.");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    window.ViewModel.SetAppearance(mode);
                    InspectSwitchProfile(window, dialog =>
                    {
                        var texts = VisibleTexts(dialog);
                        Require(dialog.Title == "Trocar perfil" && texts.Contains("Trocar perfil") && dialog.Options.Count == 2, $"{mode}: o pop-up deveria ter o título e as duas UBS.");
                        Require(texts.Contains("Região: Norte") && texts.Contains("Região: Sul") && texts.Count(text => text == "UBS Central") == 2, $"{mode}: nome e região de cada UBS deveriam aparecer: {string.Join(" / ", texts)}");
                        Require(dialog.Options.Count(option => option.IsActive) == 1 && dialog.Options.Single(option => option.IsActive).Unit.Id == north.Id && texts.Contains("Ativa"), $"{mode}: a UBS ativa deveria vir marcada.");
                        Require(dialog.Units.SelectedItem is ProfileOption { IsActive: true } && !dialog.Primary.IsEnabled, $"{mode}: a ativa deveria vir selecionada, com o Trocar desabilitado (é a mesma UBS).");
                        Require(dialog.Primary.IsDefault && dialog.CloseX.IsCancel && AutomationProperties.GetName(dialog.Primary) == "Trocar" && AutomationProperties.GetName(dialog.CloseX) == "Fechar sem trocar", $"{mode}: Enter deveria acionar o Trocar e Esc o X, com nomes de automação.");
                        Require(dialog.CloseX.TranslatePoint(new Point(0, 0), dialog).X > dialog.ActualWidth - 90 && dialog.CloseX.TranslatePoint(new Point(0, 0), dialog).Y < 70, $"{mode}: o X deveria ficar no canto superior direito.");
                        Require(dialog.Suggest.TranslatePoint(new Point(0, 0), dialog).X < dialog.Primary.TranslatePoint(new Point(0, 0), dialog).X && (string)dialog.Suggest.Content == "Sugerir pela localização" && (string)dialog.Primary.Content == "Trocar", $"{mode}: Sugerir pela localização deveria ficar à esquerda do Trocar.");
                        Require(!Descendants<Border>(dialog).Any(border => FindAncestor<ListBoxItem>(border) is null && FindAncestor<Button>(border) is null && FindAncestor<TextBox>(border) is null && border.Background is System.Windows.Media.SolidColorBrush brush && brush.Color == ThemeBrush(application, "SurfaceAltBrush").Color && border.ActualWidth > 200), $"{mode}: o pop-up não deveria ter card dentro.");
                        SavePreview(dialog, Path.Combine(previewRoot, $"obs-trocar-perfil-ativa-{mode.ToString().ToLowerInvariant()}.png"));
                    });
                }
                window.ViewModel.SetAppearance(Appearance.Light);
                // O X fecha sem trocar, mesmo com outra UBS selecionada.
                InspectSwitchProfile(window, dialog =>
                {
                    dialog.Units.SelectedItem = dialog.Options.Single(option => option.Unit.Id == south.Id);
                    Require(dialog.Primary.IsEnabled && vm.CandidateObsUnit?.Id == south.Id, "Escolher a outra UBS deveria habilitar o Trocar.");
                    InvokeButton(dialog.CloseX);
                    Require(!dialog.IsVisible && !dialog.Switched, "O X deveria fechar o pop-up sem trocar.");
                });
                Require(vm.ActiveObsUnit?.Id == north.Id && vm.ObsRegistry.Single().FullName == "Paciente Norte", "Fechar pelo X trocou de UBS.");
                // Esc fecha sem trocar.
                InspectSwitchProfile(window, dialog =>
                {
                    Require(dialog.Units.SelectedItem is ProfileOption { IsActive: true }, "Reabrir deveria voltar à UBS ativa selecionada, não à escolha que ficou do pop-up anterior.");
                    dialog.Units.SelectedItem = dialog.Options.Single(option => option.Unit.Id == south.Id);
                    AccessKeyManager.ProcessKey(PresentationSource.FromVisual(dialog)!, "\u001b", false);
                    Require(!dialog.IsVisible && !dialog.Switched, "Esc deveria fechar o pop-up sem trocar.");
                });
                Require(vm.ActiveObsUnit?.Id == north.Id, "Fechar com Esc trocou de UBS.");
                // Trocar: confirma pela lógica existente, carrega os dados da nova UBS e fecha.
                InspectSwitchProfile(window, dialog =>
                {
                    dialog.Units.SelectedItem = dialog.Options.Single(option => option.Unit.Id == south.Id);
                    InvokeButton(dialog.Primary);
                    PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Switched && !dialog.IsBusy, "Trocar deveria fechar o pop-up depois de trocar.");
                });
                Require(vm.ActiveObsUnit?.Id == south.Id && vm.ObsRegistry.Single().FullName == "Paciente Sul 1" && vm.Message.Contains("UBS Central", StringComparison.Ordinal), "Trocar não ativou a UBS escolhida com os dados dela.");
                InspectSwitchProfile(window, dialog => Require(dialog.Options.Single(option => option.IsActive).Unit.Id == south.Id && dialog.Units.SelectedItem is ProfileOption { IsActive: true } && !dialog.Primary.IsEnabled, "Depois de trocar, a nova UBS deveria vir marcada e selecionada."));
            }
            finally { CloseWindow(window); }
        });

        Check("Trocar perfil: lista com falha mostra a mensagem dentro do pop-up, conta sem UBS mostra a orientação e a troca que falha mantém o pop-up aberto", () =>
        {
            using var fixture = new ApiFixture();
            var first = fixture.AddUnit("UBS Primeira", "Norte"); var second = fixture.AddUnit("UBS Segunda", "Sul");
            var window = OpenOperational(fixture, storageRoot, "switch-profile-failures");
            try
            {
                var vm = LoginObs(window);
                fixture.FailReads = true;
                InspectSwitchProfile(window, dialog =>
                {
                    Require(!dialog.IsLoading && dialog.ErrorMessage.Length > 0 && Descendants<Border>(dialog).Single(border => AutomationProperties.GetName(border) == "Erro ao trocar de perfil").IsVisible, "A falha ao ler a lista deveria aparecer dentro do pop-up.");
                    Require(!Descendants<ProgressBar>(dialog).Single().IsVisible && dialog.Options.Count == 2, "O indicador deveria sumir e a última lista lida continuar disponível.");
                });
                fixture.FailReads = false;
                InspectSwitchProfile(window, dialog =>
                {
                    Require(dialog.ErrorMessage.Length == 0 && dialog.Options.Count == 2, "Com a API de volta o erro deveria sumir.");
                    fixture.UnavailableUnits.Add(second.Id);
                    dialog.Units.SelectedItem = dialog.Options.Single(option => option.Unit.Id == second.Id);
                    InvokeButton(dialog.Primary);
                    PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && !dialog.Switched && vm.ActiveObsUnit == null && dialog.ErrorMessage.Contains("indisponível", StringComparison.OrdinalIgnoreCase) && dialog.Primary.IsEnabled && dialog.CloseX.IsEnabled, "A troca que falha deveria manter o pop-up aberto com a mensagem e os botões de volta: " + dialog.ErrorMessage);
                    fixture.UnavailableUnits.Clear();
                    InvokeButton(dialog.Primary);
                    PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Switched && vm.ActiveObsUnit?.Id == second.Id, "A nova tentativa deveria trocar.");
                });
                // Sessão recusada pela API com o pop-up aberto: a conta sai e o pop-up fecha sozinho (a faixa da janela principal explica).
                // O pop-up fecha sozinho durante a própria leitura; um temporizador de segurança o fecha à força se isso não acontecer, para o teste nunca ficar preso no modal.
                fixture.UnauthorizedReads = true;
                var forced = false;
                var guard = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromSeconds(10) };
                guard.Tick += (_, _) => { guard.Stop(); if (window.OwnedWindows.OfType<SwitchProfileDialog>().SingleOrDefault() is { } stuck) { forced = true; stuck.Close(); } };
                guard.Start();
                try { window.OpenSwitchProfile(); }
                finally { guard.Stop(); fixture.UnauthorizedReads = false; }
                Require(!forced && !vm.IsAuthenticated, "A sessão recusada deveria sair da conta e fechar o pop-up sozinho.");
            }
            finally { CloseWindow(window); }

            using var empty = new ApiFixture();
            empty.AddUnit("UBS sem concessão"); empty.Granted = [];
            var noGrant = OpenOperational(empty, storageRoot, "switch-profile-empty");
            try
            {
                LoginObs(noGrant);
                InspectSwitchProfile(noGrant, dialog =>
                {
                    Require(dialog.Options.Count == 0 && VisibleTexts(dialog).Any(text => text.Contains("Nenhuma UBS autorizada", StringComparison.Ordinal)) && !dialog.Units.IsVisible, "Sem UBS autorizada o pop-up deveria explicar a concessão.");
                    Require(!dialog.Primary.IsEnabled && !dialog.Suggest.IsEnabled && dialog.CloseX.IsEnabled, "Sem UBS, só o X deveria funcionar.");
                });
            }
            finally { CloseWindow(noGrant); }
        });

        Check("Trocar perfil: Sugerir pela localização só seleciona a sugestão na lista (sem trocar) e recusar a localização não muda nada", () =>
        {
            using var fixture = new ApiFixture();
            var north = fixture.AddUnit("UBS Norte", "Norte", -23.4, -46.6);
            var south = fixture.AddUnit("UBS Sul", "Sul", -23.9, -46.3);
            var window = OpenOperational(fixture, storageRoot, "switch-profile-location");
            var asked = new List<string>();
            Confirmation.Handler = (_, title, _) => { asked.Add(title); return false; };
            try
            {
                var vm = LoginObs(window);
                InspectSwitchProfile(window, dialog =>
                {
                    Require(dialog.Suggest.IsEnabled && dialog.Units.SelectedItem is null && !dialog.Primary.IsEnabled, "Sem escolha, só o Sugerir deveria estar disponível.");
                    InvokeButton(dialog.Suggest);
                    Require(asked.SequenceEqual(["Acessar localização"]) && dialog.Units.SelectedItem is null && dialog.OwnedWindows.Count == 0, "Recusar a localização não deveria abrir nada nem mudar a escolha.");
                    Require(vm.SuggestClosestObsUnit(-23.95, -46.3) && dialog.Units.SelectedItem is ProfileOption { Unit.Id: var suggested } && suggested == south.Id, "A sugestão deveria selecionar a UBS mais próxima na lista.");
                    Require(dialog.Primary.IsEnabled && vm.ActiveObsUnit == null && vm.ObsRegistry.Count == 0, "Sugerir não pode trocar: só seleciona e habilita o Trocar.");
                    Require(!vm.SuggestClosestObsUnit(double.NaN, 0) && vm.CandidateObsUnit?.Id == south.Id, "Uma posição inválida não deveria mudar a sugestão.");
                    dialog.Units.SelectedItem = dialog.Options.Single(option => option.Unit.Id == north.Id);
                    Require(vm.CandidateObsUnit?.Id == north.Id, "Escolher na lista depois da sugestão deveria valer.");
                });
                Require(vm.ActiveObsUnit == null, "Fechar o pop-up com uma sugestão selecionada não pode trocar.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Trocar perfil abre sozinho depois do login e depois de restaurar a sessão quando há várias UBS e nenhuma ativa; fechar sem escolher mantém o aviso com Escolher UBS", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var north = fixture.AddUnit("UBS Norte", "Norte"); var south = fixture.AddUnit("UBS Sul", "Sul");
            fixture.AddRegistry(north.Id, "Paciente Norte"); fixture.AddRegistry(south.Id, "Paciente Sul", "11144477735");
            var storage = Path.Combine(storageRoot, "auto-profile");
            var devices = new ObsDeviceStore(storage);
            ShellWindow.AutoOpenProfileChoice = true;
            try
            {
                var first = NewDesktopWindow(fixture, DesktopProfile.Operational, storage, devices);
                try
                {
                    var vm = first.ViewModel.Workspace;
                    InspectAutoOpened(first, dialog =>
                    {
                        Require(dialog.Options.Count == 2 && !dialog.Options.Any(option => option.IsActive) && dialog.Units.SelectedItem is null && !dialog.Primary.IsEnabled, "O pop-up automático deveria listar as duas UBS, sem ativa e sem escolha.");
                        InvokeButton(dialog.CloseX);
                    }, () => { SignInDesktop(first); PumpUntil(() => vm.IsAuthenticated && !vm.IsBusy && !vm.ProfileChoicePending); });
                    Settle(vm);
                    Require(vm.ActiveObsUnit == null && vm.NeedsObsUnitChoice && !vm.ProfileChoicePending && !first.OwnedWindows.OfType<SwitchProfileDialog>().Any(), "Fechar sem escolher deveria deixar a conta sem UBS ativa e sem reabrir o pop-up.");
                    var agenda = Show(first, "agenda");
                    var choose = FindActionButton(agenda, "Escolher UBS");
                    Require(choose.IsVisible && VisibleTexts(agenda).Any(text => text.Contains("2 UBS", StringComparison.Ordinal)) && !Descendants<Button>(agenda).Any(button => button.Content?.ToString() == "Escolher UBS no Painel"), "A tela deveria continuar mostrando o aviso com o botão Escolher UBS.");
                    InspectSwitchProfile(first, dialog =>
                    {
                        dialog.Units.SelectedItem = dialog.Options.Single(option => option.Unit.Id == north.Id);
                        InvokeButton(dialog.Primary);
                        PumpUntil(() => !dialog.IsVisible);
                        Require(dialog.Switched, "Trocar deveria concluir pelo aviso da tela.");
                    }, () => InvokeButton(choose));
                    Require(vm.ActiveObsUnit?.Id == north.Id && !vm.NeedsObsUnitChoice, "O botão Escolher UBS não levou à UBS escolhida.");
                    Require(devices.ReadSession() is { } saved && saved.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23), "Sem a caixa a sessão deveria ficar gravada por cerca de 1 dia.");
                }
                finally { CloseWindow(first); }

                // Reabrir restaura a sessão (sem nova senha) e, como a escolha da UBS não é guardada, o pop-up abre sozinho de novo.
                var logins = fixture.Count("POST", "/api/auth/login");
                var reopened = NewDesktopWindow(fixture, DesktopProfile.Operational, storage, devices, show: false);
                try
                {
                    var vm = reopened.ViewModel.Workspace;
                    InspectAutoOpened(reopened, dialog =>
                    {
                        Require(dialog.Options.Count == 2 && !dialog.Options.Any(option => option.IsActive), "Depois de restaurar a sessão a escolha da UBS deveria voltar a ser pedida.");
                        dialog.Units.SelectedItem = dialog.Options.Single(option => option.Unit.Id == south.Id);
                        InvokeButton(dialog.Primary);
                        PumpUntil(() => !dialog.IsVisible);
                    }, () => { PrepareWindow(reopened); PumpUntil(() => vm.IsAuthenticated && !vm.IsBusy && !vm.ProfileChoicePending); });
                    Require(fixture.Count("POST", "/api/auth/login") == logins && vm.ActiveObsUnit?.Id == south.Id, "A sessão deveria ser restaurada sem novo login e a UBS escolhida no pop-up.");
                }
                finally { CloseWindow(reopened); }

                // Uma única UBS é ativada sozinha e nenhuma UBS não pede escolha: nos dois casos o pop-up não abre.
                foreach (var count in new[] { 1, 0 })
                {
                    using var small = new ApiFixture { UseWorkspaceIdentity = true };
                    for (var index = 0; index < count; index++) small.AddUnit("UBS Única");
                    var window = NewDesktopWindow(small, DesktopProfile.Operational, Path.Combine(storageRoot, "auto-profile-" + count), new ObsDeviceStore(Path.Combine(storageRoot, "auto-profile-" + count)));
                    try
                    {
                        var vm = window.ViewModel.Workspace;
                        SignInDesktop(window); PumpUntil(() => vm.IsAuthenticated && !vm.IsBusy);
                        PumpUntil(() => true);
                        Require(!vm.ProfileChoicePending && !window.OwnedWindows.OfType<SwitchProfileDialog>().Any() && (count == 1) == (vm.ActiveObsUnit is not null), $"Com {count} UBS o pop-up não deveria abrir sozinho.");
                    }
                    finally { CloseWindow(window); }
                }
            }
            finally { ShellWindow.AutoOpenProfileChoice = false; }
        });

        Check("Login salvo nos desktops: sem a caixa a sessão de 1 dia é gravada e restaurada ao reabrir; vencida ou recusada é apagada; Sair apaga e avisa a API", () =>
        {
            foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
            {
                using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
                fixture.AddUnit("UBS Sessão");
                var storage = Path.Combine(storageRoot, "saved-login", profile.Id);
                var devices = new ObsDeviceStore(storage);
                void Open(Action<ShellWindow> use)
                {
                    var window = NewDesktopWindow(fixture, profile, storage, devices);
                    try { use(window); }
                    finally { CloseWindow(window); }
                }
                void SignIn(ShellWindow window) { SignInDesktop(window); PumpUntil(() => window.ViewModel.Workspace.IsAuthenticated && !window.ViewModel.Workspace.IsBusy); }

                // 1. Entrar sem marcar a caixa grava a sessão com o vencimento de 1 dia; fechar o programa não a apaga nem avisa a API.
                Open(window =>
                {
                    var vm = window.ViewModel.Workspace;
                    Require(Descendants<TextBlock>((FrameworkElement)window.Content).Any(text => text.IsVisible && text.Text == "Sem marcar, você continua conectado por 1 dia neste computador."), $"{profile.Id}: o texto de ajuda da tela de acesso deveria dizer a verdade.");
                    SignIn(window);
                    Require(vm.IsAuthenticated && devices.ReadSession() is { Token: "fixture-token" } saved && saved.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23) && saved.ExpiresAt < DateTimeOffset.UtcNow.AddHours(25), $"{profile.Id}: a sessão de 1 dia não foi gravada.");
                    Require(!fixture.LastKeepSignedInRequested, $"{profile.Id}: o login sem a caixa não deveria pedir 30 dias.");
                });
                Require(File.Exists(devices.SessionPath) && !fixture.LogoutCalled, $"{profile.Id}: fechar o programa apagou a sessão ou avisou a API.");

                // 2. Reabrir dentro do prazo entra direto, conferindo a sessão na API.
                var logins = fixture.Count("POST", "/api/auth/login"); var checkedBefore = fixture.Count("GET", "/api/auth/me");
                Open(window =>
                {
                    var vm = window.ViewModel.Workspace;
                    PumpUntil(() => vm.IsAuthenticated && !vm.IsBusy);
                    Require(vm.IsAuthenticated && !window.ViewModel.ShowCompactAccess && fixture.Count("POST", "/api/auth/login") == logins && fixture.Count("GET", "/api/auth/me") == checkedBefore + 1, $"{profile.Id}: reabrir dentro do prazo deveria entrar direto e conferir a sessão em /api/auth/me.");
                });

                // 3. Sessão vencida: apaga o arquivo, volta ao login e nem chama a API.
                var saved = devices.ReadSession()!;
                devices.SaveSession(saved with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) });
                checkedBefore = fixture.Count("GET", "/api/auth/me");
                Open(window =>
                {
                    var vm = window.ViewModel.Workspace; Settle(vm);
                    Require(!vm.IsAuthenticated && window.ViewModel.ShowCompactAccess && devices.ReadSession() is null && fixture.Count("GET", "/api/auth/me") == checkedBefore, $"{profile.Id}: a sessão vencida deveria ser apagada e pedir o login.");
                });

                // 4. Sessão recusada pela API: apaga o arquivo, volta ao login com a mensagem de sempre.
                Open(window => SignIn(window));
                Require(devices.ReadSession() is not null, $"{profile.Id}: o segundo login não gravou a sessão.");
                fixture.UnauthorizedReads = true;
                Open(window =>
                {
                    var vm = window.ViewModel.Workspace; Settle(vm);
                    Require(!vm.IsAuthenticated && window.ViewModel.ShowCompactAccess && devices.ReadSession() is null && vm.Message.Contains("A sessão salva terminou", StringComparison.Ordinal), $"{profile.Id}: a sessão recusada deveria ser apagada com a mensagem de sempre.");
                });
                fixture.UnauthorizedReads = false;

                // 5. Sair da conta apaga o arquivo e avisa a API.
                Open(window =>
                {
                    var vm = window.ViewModel.Workspace;
                    SignIn(window);
                    Require(devices.ReadSession() is not null, $"{profile.Id}: o terceiro login não gravou a sessão.");
                    vm.LogoutCommand.Execute(null); Settle(vm);
                    Require(vm.IsSignedOut && devices.ReadSession() is null && fixture.LogoutCalled && window.ViewModel.ShowCompactAccess, $"{profile.Id}: Sair deveria apagar a sessão, avisar a API e voltar ao login.");
                });
            }
        });

        Check("Login salvo do OBS com a ativação de computadores ligada: a sessão de 1 dia é gravada, a reabertura confere o computador e computador revogado derruba a sessão salva", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true, ObsComputersEnabled = true };
            var unit = fixture.AddUnit("UBS Computador", "Centro");
            var device = fixture.AddDevice(unit.Id, "Recepção 1");
            var storage = Path.Combine(storageRoot, "saved-login-computers");
            var store = ComputerStore(storage);
            Require(store.SaveEnrollment(StoredFor(device)), "Não foi possível guardar a identificação de teste.");
            void Open(Action<ShellWindow> use)
            {
                var window = NewDesktopWindow(fixture, DesktopProfile.Operational, storage, store);
                try { use(window); }
                finally { CloseWindow(window); }
            }
            Open(window =>
            {
                var vm = window.ViewModel.Workspace;
                Require(vm.HasDeviceSupport, "O cenário deveria ter a ativação de computadores ligada.");
                SignInDesktop(window); PumpUntil(() => vm.IsAuthenticated && !vm.IsBusy);
                Require(vm.IsAuthenticated && fixture.DeviceHeaders.Contains(ApiFixture.HeaderFor(device)) && store.ReadSession() is { } saved && saved.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23) && saved.ExpiresAt < DateTimeOffset.UtcNow.AddHours(25), "O login deveria levar o computador e gravar a sessão de 1 dia.");
            });
            var logins = fixture.Count("POST", "/api/auth/login"); var computerChecks = fixture.Count("GET", "/devices/current");
            Open(window =>
            {
                var vm = window.ViewModel.Workspace;
                PumpUntil(() => vm.IsAuthenticated && !vm.IsBusy);
                Require(vm.IsAuthenticated && fixture.Count("POST", "/api/auth/login") == logins && fixture.Count("GET", "/devices/current") == computerChecks + 1, "Reabrir deveria restaurar a sessão depois de conferir o computador.");
            });
            fixture.RevokeDevice(device);
            Open(window =>
            {
                var vm = window.ViewModel.Workspace; Settle(vm);
                Require(!vm.IsAuthenticated && store.ReadSession() is null && window.ViewModel.ShowCompactAccess, "Com o computador revogado a sessão salva não pode valer: deveria ser apagada e pedir o login.");
            });
        });
    }
}
