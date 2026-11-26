using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Redesenho de Configurações, Exclusão programada, Dados do perfil, Exames e vacinas (aba da ficha), troca obrigatória de senha, tela de acesso, aviso de UBS e tela protegida sem sessão:
/// sem título repetido, sem card em listagem e formulário, ações só no balão (lixeira em vermelho), ficha em linhas, hover azul bebê, sem botão Atualizar, estados e capturas nos dois temas.
/// </summary>
internal static partial class Program
{
    private const string Cfa = "Config/ficha/acesso";

    private static SolidColorBrush CfaBrush(string key) => (SolidColorBrush)Application.Current.FindResource(key);

    /// <summary>Janela ou botão com "Atualizar" no texto ou no nome de automação: nenhuma tela deste grupo tem botão assim.</summary>
    private static bool HasRefreshButton(DependencyObject root) =>
        Descendants<Button>(root).Any(button => button.IsVisible && ((button.Content as string)?.Contains("Atualizar", StringComparison.OrdinalIgnoreCase) == true || AutomationProperties.GetName(button).Contains("Atualizar", StringComparison.OrdinalIgnoreCase)));

    /// <summary>O balão de uma lista curta (que não rola) fica abaixo da última linha, sem cobri-la.</summary>
    private static bool BalloonClearsList(ListBox list, SelectionBalloon balloon)
    {
        var rows = Descendants<ListBoxItem>(list).Where(row => row.IsVisible).ToArray();
        if (rows.Length == 0) return true;
        // Linha que passou do fim da área visível da lista conta só até onde a lista vai (ela rola até ela).
        var listBottom = list.TranslatePoint(new Point(0, list.ActualHeight), balloon).Y;
        var lastBottom = Math.Min(listBottom, rows.Max(row => row.TranslatePoint(new Point(0, row.ActualHeight), balloon).Y));
        return lastBottom <= 1;
    }

    private static bool HasCardBorder(DependencyObject root) =>
        Descendants<Border>(root).Any(border => ReferenceEquals(border.Style, Application.Current.FindResource("Card")));

    /// <summary>Abre o que <paramref name="open"/> dispara (a ficha abre depois do clique ou do Enter) e inspeciona a janela <typeparamref name="T"/> dentro do laço dela; fecha ao terminar.</summary>
    private static void InspectOpened<T>(Window owner, Action<T> inspect, Action open) where T : Window
    {
        var observed = false; string? error = null; var attempts = 0;
        void Run()
        {
            var dialog = owner.OwnedWindows.OfType<T>().SingleOrDefault();
            if (dialog == null)
            {
                if (++attempts < 3000) owner.Dispatcher.BeginInvoke(Run, DispatcherPriority.ApplicationIdle);
                else error = "A janela não abriu.";
                return;
            }
            try { inspect(dialog); }
            catch (Exception failure) { error = failure.Message; }
            finally { observed = true; if (dialog.IsVisible) dialog.Close(); }
        }
        owner.Dispatcher.BeginInvoke(Run, DispatcherPriority.ApplicationIdle);
        open();
        PumpUntil(() => observed || error != null);
        Require(observed && error == null, error ?? "Nenhuma janela foi aberta.");
    }

    private static void RunConfigAccessChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);

        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{Cfa}: {profile.Name}: Configurações sem título repetido, sem card e sem Atualizar; tema e preferências continuam funcionando; hover azul bebê", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Config");
            var window = OpenProfile(profile, fixture, storageRoot, "cfa-settings-" + profile.Id, Appearance.Light);
            try
            {
                LoginObs(window);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Show(window, "settings", 1280, 900);
                    var view = Descendants<SettingsView>(content).Single(item => item.IsVisible);
                    Require(!HasCardBorder(view), $"{mode}: as seções de Configurações não deveriam ser cards.");
                    Require(!HasRefreshButton(view), $"{mode}: Configurações não deveria ter botão Atualizar.");
                    var texts = VisibleTexts(view);
                    Require(!texts.Contains("Configurações"), $"{mode}: o título da tela não deveria se repetir dentro dela.");
                    foreach (var heading in new[] { "Foto de perfil", "Aparência", "Preferências da interface", "Atalhos do teclado", "Restaurar preferências padrão" })
                        Require(texts.Contains(heading), $"{mode}: faltou a seção \"{heading}\".");
                    Require(Descendants<Border>(view).Count(border => border.Height == 1 && border.IsVisible) >= 4, $"{mode}: cada seção deveria ter o seu fio.");
                    var tiles = Descendants<RadioButton>(view).Where(button => button.CommandParameter?.ToString() is "Light" or "Dark" or "System").ToArray();
                    Require(tiles.Length == 3, "Faltaram as três escolhas de tema.");
                    var idle = tiles.First(button => button.IsChecked != true);
                    var tile = (Border)idle.Template.FindName("Tile", idle);
                    idle.SetValue(MouseOverKey!, true); view.UpdateLayout();
                    Require(((SolidColorBrush)tile.Background).Color == CfaBrush("ButtonHoverBrush").Color, $"{mode}: o bloco de tema deveria ficar em azul bebê ao passar o mouse.");
                    idle.ClearValue(MouseOverKey!); view.UpdateLayout();
                    Require(((SolidColorBrush)tile.Background).Color != CfaBrush("ButtonHoverBrush").Color, $"{mode}: sem o mouse o bloco de tema volta ao fundo normal.");
                    SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-configuracoes-{mode.ToString().ToLowerInvariant()}.png"));
                }
                // Preferências continuam valendo: a confirmação ao fechar liga e desliga (e persiste no estado da janela).
                // Janela mínima (980): a coluna da direita desce para baixo das preferências e nada passa da borda.
                var narrow = Show(window, "settings", 980, 640);
                SaveScrolled(window, Path.Combine(previewRoot, $"{profile.Id}-configuracoes-janela-minima.png"), 980, 640);
                Require(Grid.GetRow((FrameworkElement)Descendants<SettingsView>(narrow).Single(item => item.IsVisible).FindName("ShortcutCard")) == 1, "Na janela mínima os atalhos e a Exclusão programada descem para baixo das preferências.");
                var view2 = Descendants<SettingsView>(Show(window, "settings", 1280, 900)).Single(item => item.IsVisible);
                var toggle = Descendants<ToggleButton>(view2).Single(item => AutomationProperties.GetName(item) == "Confirmar antes de fechar o aplicativo");
                toggle.IsChecked = true; Require(window.ViewModel.ConfirmClose, "Ligar a confirmação ao fechar não chegou ao estado da janela.");
                toggle.IsChecked = false; Require(!window.ViewModel.ConfirmClose, "Desligar a confirmação ao fechar não chegou ao estado da janela.");
                var photo = Descendants<Button>(view2).Single(button => AutomationProperties.GetName(button) == "Alterar foto de perfil");
                Require(photo.IsVisible, "O botão da foto sumiu das Configurações.");
                // Restaurar padrões: a confirmação na própria tela tem o primário à direita do Cancelar; Cancelar recolhe sem restaurar.
                InvokeButton(Descendants<Button>(view2).Single(button => button.Content as string == "Restaurar padrões")); view2.UpdateLayout();
                var confirmRestore = (Button)view2.FindName("ConfirmRestore");
                var cancelRestore = Descendants<Button>((FrameworkElement)view2.FindName("RestoreConfirmation")).Single(button => button.Content as string == "Cancelar");
                Require(confirmRestore.IsVisible && confirmRestore.TranslatePoint(new Point(0, 0), view2).X > cancelRestore.TranslatePoint(new Point(0, 0), view2).X, "Na confirmação de restaurar, o primário fica à direita do Cancelar.");
                InvokeButton(cancelRestore); view2.UpdateLayout();
                Require(!confirmRestore.IsVisible, "Cancelar deveria recolher a confirmação de restaurar.");
            }
            finally { CloseWindow(window); }
        });

        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{Cfa}: {profile.Name}: Exclusão programada: política em bloco curto, lista aberta com balão (lixeira), ficha em linhas, agendar e cancelar com confirmação, releitura sem piscar", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Exclusão");
            var free = new RetentionItem(Guid.NewGuid(), "Sala 3 antiga", true, null, false);
            var planned = new RetentionItem(Guid.NewGuid(), "Consultório 2", true, DateTimeOffset.UtcNow.AddDays(10), false);
            var reverted = new RetentionItem(Guid.NewGuid(), "Sala de vacina", true, null, true);
            var guarded = new RetentionItem(Guid.NewGuid(), "Consultório 1", false, null, false);
            fixture.RetentionItems.AddRange([free, planned, reverted, guarded]);
            var asked = new List<string>(); var answer = true;
            Confirmation.Handler = (_, title, text) => { asked.Add(title + "|" + text); return answer; };
            var window = OpenProfile(profile, fixture, storageRoot, "cfa-retention-" + profile.Id, Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "settings", 1280, 1100);
                var retention = Descendants<RetentionSettingsView>(content).Single(view => view.IsVisible);
                var never = (CheckBox)retention.FindName("NeverDelete");
                var list = (ListBox)retention.FindName("Items");
                var balloon = (SelectionBalloon)retention.FindName("ItemBalloon");
                var schedule = (Button)retention.FindName("Schedule"); var cancel = (Button)retention.FindName("CancelSchedule");
                PumpUntil(() => never.IsEnabled && list.Items.Count == 4);
                content = Layout(window, 1280, 1100);
                Require(!HasCardBorder(retention) && !HasRefreshButton(retention), "A seção não deveria ter card nem botão Atualizar.");
                Require(VisibleTexts(retention).Contains("Exclusão programada") && VisibleTexts(retention).Contains("Cadastros desativados"), "Faltaram o título da seção e o rótulo da lista.");
                Require(list.Style == (Style)application.FindResource("OpenList") && list.SelectionMode == SelectionMode.Extended && !Descendants<DataGrid>(retention).Any() && !Descendants<ComboBox>(retention).Any(), "Os cadastros desativados deveriam ser uma lista aberta, sem combo nem grade.");
                Require(ReferenceEquals(schedule.Style, application.FindResource("ListActionDangerButton")) && ReferenceEquals(cancel.Style, application.FindResource("ListActionDangerButton")), "Agendar e cancelar a exclusão são ações destrutivas: lixeira branca em vermelho.");
                Require(Descendants<StatusTag>(list).Select(tag => tag.Text).OrderBy(text => text).SequenceEqual(["Cancelada", "Elegível", "Programada", "Protegido"]), "Cada linha deveria trazer a etiqueta de situação.");
                Require(VisibleTexts(list).Contains("Exclusão cancelada") && VisibleTexts(list).Contains("Protegido por vínculos ou histórico"), "A linha de dados deveria dizer o que acontece com o cadastro.");
                Require(!balloon.IsVisible, "Sem seleção o balão não aparece.");
                void Select(params RetentionItem[] items) { PumpFrames(2); list.SelectedItems.Clear(); foreach (var item in items) list.SelectedItems.Add(list.Items.Cast<RetentionItem>().Single(entry => entry.Id == item.Id)); list.UpdateLayout(); balloon.UpdateLayout(); }
                Select(free);
                Require(balloon.IsVisible && balloon.Label == "Sala 3 antiga" && schedule.IsVisible && !cancel.IsVisible && balloon.HasActions, "Cadastro elegível sem agendamento: só Agendar exclusão.");
                Require(BalloonClearsList(list, balloon), "A lista é curta e não rola até o fim: o balão não pode cobrir o último cadastro.");
                Select(planned);
                Require(schedule.IsVisible && cancel.IsVisible, "Cadastro com exclusão programada: Agendar de novo e Cancelar exclusão.");
                Select(reverted);
                Require(schedule.IsVisible && !cancel.IsVisible, "Exclusão já cancelada: só Agendar.");
                Select(guarded);
                Require(balloon.IsVisible && !schedule.IsVisible && !cancel.IsVisible && !balloon.HasActions, "Cadastro protegido por vínculos: sem ação.");
                Select(free, planned);
                Require(balloon.Label == "2 cadastros selecionados" && !schedule.IsVisible && !cancel.IsVisible, "Com vários selecionados as ações de um só item somem.");
                Select(free);
                SaveScrolled(window, Path.Combine(previewRoot, $"{profile.Id}-exclusao-programada-claro.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark);
                SaveScrolled(window, Path.Combine(previewRoot, $"{profile.Id}-exclusao-programada-escuro.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Light); content = Layout(window, 1280, 1100);

                // Agendar: o prazo vem de um pop-up com a grade de cadastro; prazo inválido, confirmação recusada e confirmação aceita.
                Select(free); asked.Clear(); answer = false;
                InspectModal<FormDialog>(window, dialog =>
                {
                    var days = Descendants<TextBox>(dialog).Single(box => AutomationProperties.GetName(box) == "Prazo deste cadastro em dias");
                    var primary = Descendants<Button>(dialog).Single(button => button.Content as string == "Agendar exclusão");
                    Require(dialog.Title == "Agendar exclusão de Sala 3 antiga" && days.Text == "30" && Descendants<FormGrid>(dialog).Count() == 1, "O pop-up deveria vir com o prazo de 30 dias, na grade de cadastro.");
                    days.Text = "0"; InvokeButton(primary); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.ErrorMessage == "Informe um prazo de 1 a 36.500 dias." && asked.Count == 0 && fixture.RetentionWrites.Count == 0, "Prazo fora de 1 a 36.500 dias não pode chegar à confirmação nem à API.");
                    // Captura com o erro dentro do pop-up, sem a animação curta de entrada (que deixaria a imagem esmaecida).
                    ((FrameworkElement)dialog.FindName("Frame")).BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.Zero));
                    if (dialog.FindName("Zoom") is ScaleTransform zoom) { zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.Zero)); zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.Zero)); }
                    dialog.UpdateLayout(); SavePreview(dialog, Path.Combine(previewRoot, $"{profile.Id}-exclusao-programada-agendar-claro.png"));
                    days.Text = "45"; InvokeButton(primary); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && dialog.ErrorMessage == "Nada foi agendado." && asked.Count == 1 && asked[0] == "Exclusão programada|Agendar a exclusão de Sala 3 antiga para daqui a 45 dias?" && fixture.RetentionWrites.Count == 0, "Recusar a confirmação não agenda nada e mantém o pop-up aberto.");
                    answer = true; InvokeButton(primary); PumpUntil(() => !dialog.IsVisible);
                }, () => InvokeButton(schedule));
                PumpUntil(() => list.Items.Cast<RetentionItem>().Single(entry => entry.Id == free.Id).ScheduledAt is not null);
                Require(fixture.RetentionWrites.Single() == (free.Id, 45), "O agendamento deveria chegar à API com o prazo informado.");
                Select(free);
                Require(cancel.IsVisible && balloon.IsVisible, "Depois de agendar, o balão acompanha o cadastro e passa a oferecer Cancelar exclusão.");

                // Cancelar a exclusão: pede confirmação; recusar não grava.
                asked.Clear(); answer = false; InvokeButton(cancel); PumpFrames(2);
                Require(asked.Count == 1 && asked[0] == "Exclusão programada|Cancelar a exclusão de Sala 3 antiga?" && fixture.RetentionWrites.Count == 1, "Recusar a confirmação não cancela.");
                answer = true; InvokeButton(cancel);
                PumpUntil(() => list.Items.Cast<RetentionItem>().Single(entry => entry.Id == free.Id).ScheduledAt is null);
                Require(fixture.RetentionWrites.Last() == (free.Id, null) && list.Items.Cast<RetentionItem>().Single(entry => entry.Id == free.Id) is { Tag: "Cancelada" }, "Cancelar a exclusão deveria gravar e mostrar a situação nova.");

                // A releitura de 20 s não pisca o balão nem derruba a seleção quando nada mudou.
                Select(planned);
                var changes = 0; balloon.IsVisibleChanged += (_, _) => changes++;
                var reads = fixture.RetentionReads;
                Pump(retention.RefreshAutomaticallyAsync());
                Require(fixture.RetentionReads == reads + 1 && changes == 0 && balloon.IsVisible && ListingBehavior.GetSelectedItems<RetentionItem>(list).Single().Id == planned.Id, "A releitura automática não pode piscar o balão nem derrubar a seleção.");
                fixture.RetentionItems.Add(new RetentionItem(Guid.NewGuid(), "Sala nova", true, null, false));
                Pump(retention.RefreshAutomaticallyAsync()); PumpFrames(2);
                Require(list.Items.Count == 5 && balloon.IsVisible && changes == 0 && ListingBehavior.GetSelectedItems<RetentionItem>(list).Single().Id == planned.Id, "Um cadastro novo entra na lista sem derrubar a seleção.");
                fixture.FailRetentionReads = true; Pump(retention.RefreshAutomaticallyAsync());
                Require(list.Items.Count == 5 && fixture.FailRetentionReads, "Uma falha da releitura automática mantém a lista na tela."); fixture.FailRetentionReads = false;

                // Ficha em linhas ao Enter (e ao segundo clique/duplo clique, que usam o mesmo caminho).
                Select(guarded);
                InspectOpened<ItemDetailsWindow>(window, popup =>
                {
                    var fields = VisibleTexts(popup);
                    Require(fields.Contains("Consultório 1") && fields.Contains("Não: há vínculos ou histórico de uso") && fields.Contains("Situação") && fields.Contains(profile.IsMunicipal ? "Conta OBS desativada" : "Consultório desativado"), "A ficha deveria mostrar o cadastro em linhas.");
                    Require(!Descendants<Border>(popup).Any(border => border.Background is SolidColorBrush brush && brush.Color == CfaBrush("SurfaceAltBrush").Color && border.CornerRadius.TopLeft >= 8), "A ficha não deveria ter cards.");
                    popup.UpdateLayout(); SavePreview(popup, Path.Combine(previewRoot, $"{profile.Id}-exclusao-programada-ficha-claro.png"));
                }, () => PressKey(list, Key.Enter));

                // Política: o prazo automático só aparece desmarcando "Nunca excluir sozinho"; salvar pede confirmação.
                var days60 = (TextBox)retention.FindName("PolicyDays"); var savePolicy = (Button)retention.FindName("SavePolicy");
                Require(never.IsChecked == true && !days60.IsVisible, "O prazo automático começa desligado e recolhido.");
                never.IsChecked = false; days60.Text = "60"; asked.Clear(); answer = true; InvokeButton(savePolicy);
                PumpUntil(() => fixture.RetentionDays == 60);
                Require(asked.Any(text => text == "Exclusão programada|Excluir cadastros elegíveis automaticamente após 60 dias?") && fixture.RetentionWrites.Contains((null, 60)), "O prazo automático deveria ser confirmado e gravado.");
                PumpUntil(() => never.IsEnabled);

                // Vazio: a lista diz "Nenhum cadastro desativado." (e some quando há itens).
                Require(!((FrameworkElement)retention.FindName("ItemsEmpty")).IsVisible, "Com cadastros na lista não deveria haver texto de lista vazia.");
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check($"{Cfa}: Exclusão programada vazia e com falha de leitura mostram o estado certo e o passo escrito", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Vazia");
            var window = OpenOperational(fixture, storageRoot, "cfa-retention-empty", Appearance.Light);
            try
            {
                LoginObs(window);
                var content = Show(window, "settings", 1280, 900);
                var retention = Descendants<RetentionSettingsView>(content).Single(view => view.IsVisible);
                var never = (CheckBox)retention.FindName("NeverDelete");
                PumpUntil(() => never.IsEnabled);
                Require(((FrameworkElement)retention.FindName("ItemsEmpty")).IsVisible && VisibleTexts(retention).Contains("Nenhum cadastro desativado."), "Sem cadastros desativados a lista deveria dizer isso.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-exclusao-programada-vazia-claro.png"), 1280, 900);
                window.ViewModel.SetAppearance(Appearance.Dark);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-exclusao-programada-vazia-escuro.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
            using var failing = new ApiFixture { UseWorkspaceIdentity = true, FailRetentionReads = true };
            failing.AddUnit("UBS Falha");
            var broken = OpenOperational(failing, storageRoot, "cfa-retention-fail", Appearance.Light);
            try
            {
                LoginObs(broken);
                var content = Show(broken, "settings", 1280, 900);
                var retention = Descendants<RetentionSettingsView>(content).Single(view => view.IsVisible);
                var feedback = (TextBlock)retention.FindName("Feedback"); var retry = (Button)retention.FindName("Retry");
                PumpUntil(() => retry.IsVisible);
                Require(feedback.Text.Length > 0 && retry.IsVisible && !((FrameworkElement)retention.FindName("Controls")).IsEnabled, "A falha da leitura deveria aparecer com o botão Tentar novamente, sem liberar os controles.");
                failing.FailRetentionReads = false; InvokeButton(retry);
                PumpUntil(() => ((FrameworkElement)retention.FindName("Controls")).IsEnabled);
                Require(!retry.IsVisible, "Tentar novamente deveria ler de novo e tirar o aviso.");
            }
            finally { CloseWindow(broken); }
        });

        Check($"{Cfa}: Exames e vacinas: listas abertas com balão, formulários no lugar das listas, ficha em linhas, sem Atualizar e releitura automática", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Jornada", "Centro");
            var patient = fixture.AddRegistry(unit.Id, "Ana Souza", "52998224725");
            fixture.AddRegistry(unit.Id, "Beto Lima", "11144477735", active: false);
            fixture.AddExam(unit.Id, patient.Cpf, "Hemograma", "done", new DateOnly(2026, 3, 10));
            var requested = fixture.AddExam(unit.Id, patient.Cpf, "Raio-X de tórax");
            fixture.AddVaccine(unit.Id, patient.Cpf, "Gripe", "single", new DateOnly(2026, 4, 1));
            fixture.AddVaccine(unit.Id, patient.Cpf, "Tétano antigo", "1", new DateOnly(2020, 1, 5), "voided", "Registro duplicado");
            var asked = new List<(string Title, string Text)>(); var answer = true;
            Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return answer; };
            var window = OpenOperational(fixture, storageRoot, "cfa-health", Appearance.Light);
            ItemDetailsWindow? popup = null;
            try
            {
                var vm = LoginObs(window);
                var record = vm.ObsRegistry.Single(item => item.Cpf == patient.Cpf);
                var health = vm.CreateHealthRecords(record) ?? throw new InvalidOperationException("A ficha deveria oferecer a aba de exames e vacinas.");
                popup = new ItemDetailsWindow(vm.BuildDetails(record)!, new ClipboardFixture(), health) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                popup.Show(); popup.UpdateLayout();
                InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Aba Exames e vacinas")); PumpUntil(() => !health.IsBusy); popup.UpdateLayout();
                var view = Descendants<HealthRecordsView>(popup).Single(item => item.IsVisible);
                var examList = (ListBox)view.FindName("ExamList"); var vaccineList = (ListBox)view.FindName("VaccineList");
                var examBalloon = (SelectionBalloon)view.FindName("ExamBalloon"); var vaccineBalloon = (SelectionBalloon)view.FindName("VaccineBalloon");
                var done = (Button)view.FindName("ExamDoneButton"); var cancel = (Button)view.FindName("ExamCancelButton"); var voidExam = (Button)view.FindName("ExamVoidButton"); var voidVaccine = (Button)view.FindName("VaccineVoidButton");

                Require(!HasRefreshButton(view) && !HasCardBorder(view) && !Descendants<DataGrid>(view).Any(), "A aba não deveria ter botão Atualizar, card nem grade.");
                Require(examList.Style == (Style)application.FindResource("OpenListLow") && AutomationProperties.GetName(examList) == "Exames do paciente" && AutomationProperties.GetName(vaccineList) == "Vacinas do paciente", "As duas listas deveriam ser abertas e manter os nomes de acessibilidade.");
                var newExam = ButtonIn(view, "Novo exame"); var newVaccine = ButtonIn(view, "Nova vacina");
                Require(ReferenceEquals(newExam.Style, application.FindResource("NewItemButton")) && ReferenceEquals(newVaccine.Style, application.FindResource("NewItemButton")), "Os botões de novo registro usam o estilo do botão novo.");
                Require(!Descendants<Button>(view).Any(button => button.Content as string is "Registrar exame" or "Registrar vacina" or "Anular registro selecionado"), "Os formulários de registro saíram da aba aberta; só abrem sob demanda.");
                Require(examList.Items.Count == 2 && vaccineList.Items.Count == 1 && VisibleTexts(view).Contains("Exames") && VisibleTexts(view).Contains("Vacinas"), "As duas listas deveriam trazer os registros e o título de cada uma.");
                Require(Descendants<StatusTag>(examList).Select(tag => tag.Text).OrderBy(text => text).SequenceEqual(["Realizado", "Solicitado"]), "Cada exame deveria trazer a etiqueta de situação.");
                Require(VisibleTexts(examList).Any(text => text.StartsWith("Realizado em 10/03/2026", StringComparison.Ordinal)) && VisibleTexts(vaccineList).Any(text => text.StartsWith("Dose única | Aplicada em 01/04/2026", StringComparison.Ordinal)), "A linha de dados deveria juntar só o que foi informado com ' | '.");

                // Balão por registro.
                void SelectExam(string name) { vaccineList.SelectedItems.Clear(); examList.SelectedItems.Clear(); examList.SelectedItems.Add(examList.Items.Cast<ExamInfo>().Single(item => item.Name == name)); popup.UpdateLayout(); }
                SelectExam("Raio-X de tórax");
                Require(examBalloon.IsVisible && done.IsVisible && cancel.IsVisible && voidExam.IsVisible && ReferenceEquals(cancel.Style, application.FindResource("ListActionDangerButton")) && ReferenceEquals(voidExam.Style, application.FindResource("ListActionDangerButton")), "Exame solicitado: marcar realizado, cancelar e anular (cancelar e anular em lixeira vermelha).");
                Require(BalloonClearsList(examList, examBalloon), "A lista da ficha é curta: o balão não pode cobrir os últimos exames.");
                SelectExam("Hemograma");
                Require(!done.IsVisible && !cancel.IsVisible && voidExam.IsVisible, "Exame realizado só pode ser anulado.");
                vaccineList.SelectedItems.Add(vaccineList.Items.Cast<VaccineInfo>().Single()); popup.UpdateLayout();
                Require(examList.SelectedItems.Count == 0 && !examBalloon.IsVisible && vaccineBalloon.IsVisible && voidVaccine.IsVisible && health.SelectedVaccine?.Name == "Gripe" && health.SelectedExam is null, "Selecionar uma vacina solta o exame (um balão por vez) e oferece só Anular.");
                vaccineList.SelectedItems.Clear(); popup.UpdateLayout();

                // Cancelar exame: confirmação, depois a API.
                SelectExam("Raio-X de tórax"); asked.Clear(); answer = false;
                InvokeButton(cancel); PumpUntil(() => !health.IsBusy);
                Require(asked.Count == 1 && asked[0].Title == "Cancelar exame" && fixture.Count("PATCH", "/status") == 0, "Recusar a confirmação não cancela o exame.");
                answer = true; InvokeButton(cancel); PumpUntil(() => !health.IsBusy);
                Require(fixture.Count("PATCH", "/status") == 1 && health.Exams.Single(item => item.Name == "Raio-X de tórax").StatusDisplay == "Cancelado" && health.MessageKind == MessageKind.Success, "Confirmar cancela o exame e a lista mostra a situação nova.");

                // Novo exame: o formulário abre no lugar das listas (um pop-up por vez), valida e fecha ao registrar.
                InvokeButton(newExam); popup.UpdateLayout();
                Require(health.Panel == HealthPanel.NewExam && !examList.IsVisible && !vaccineList.IsVisible && Descendants<FormGrid>(view).Count(grid => grid.IsVisible) == 1 && VisibleTexts(view).Contains("Novo exame"), "O formulário deveria abrir no lugar das listas, na grade de cadastro.");
                var name = Descendants<TextBox>(view).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Nome do exame");
                var primary = ButtonIn(view, "Registrar exame"); var back = ButtonIn(view, "Cancelar");
                Require(primary.TranslatePoint(new Point(0, 0), view).X > back.TranslatePoint(new Point(0, 0), view).X && primary.IsDefault == false, "O primário fica à direita do Cancelar.");
                name.Text = ""; InvokeButton(primary); PumpUntil(() => !health.IsBusy); popup.UpdateLayout();
                Require(health.Panel == HealthPanel.NewExam && health.ExamNameError.Contains("Informe o nome do exame") && VisibleTexts(view).Any(text => text.Contains("Informe o nome do exame")) && fixture.Count("POST", "/health-records/exams") == 0, "Sem nome o formulário continua aberto com o erro junto ao campo.");
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-novo-exame-erro-claro"));
                name.Text = "Ultrassom"; InvokeButton(primary); PumpUntil(() => !health.IsBusy && health.Panel == HealthPanel.Lists); popup.UpdateLayout();
                Require(examList.IsVisible && health.Exams.Any(item => item.Name == "Ultrassom") && health.SelectedExam?.Name == "Ultrassom" && examBalloon.IsVisible && health.MessageKind == MessageKind.Success, "Registrar fecha o formulário, mostra o exame novo selecionado e o aviso de sucesso.");

                // Cancelar (botão e Esc) descarta o que foi digitado.
                InvokeButton(newExam); Descendants<TextBox>(view).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Nome do exame").Text = "Rascunho";
                InvokeButton(ButtonIn(view, "Cancelar")); popup.UpdateLayout();
                Require(health.Panel == HealthPanel.Lists && health.ExamName == "" && examList.IsVisible, "Cancelar volta às listas e descarta o rascunho.");
                InvokeButton(newVaccine); health.VaccineName = "Rascunho";
                Require(PressKey(view, Key.Escape) && health.Panel == HealthPanel.Lists && health.VaccineName == "", "Esc volta às listas (sem fechar a ficha) e descarta o rascunho.");
                Require(popup.IsVisible, "Esc dentro do formulário não deveria fechar a ficha do paciente.");

                // Marcar como realizado: a data pede um formulário próprio.
                SelectExam("Ultrassom"); InvokeButton(done); popup.UpdateLayout();
                Require(health.Panel == HealthPanel.ExamDone && VisibleTexts(view).Any(text => text.Contains("Exame selecionado: Ultrassom")), "Marcar como realizado abre o formulário da data, dizendo qual é o exame.");
                var date = Descendants<TextBox>(view).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Data de realização do exame");
                date.Text = "31/12/1899"; InvokeButton(ButtonIn(view, "Marcar como realizado")); PumpUntil(() => !health.IsBusy);
                Require(health.Panel == HealthPanel.ExamDone && health.ExamDoneError.Contains("entre 01/01/1900 e hoje") && fixture.Count("PATCH", "/status") == 1, "Data inválida mantém o formulário aberto com o erro.");
                date.Text = DisplayFormat.Date(DateOnly.FromDateTime(DateTime.Today)); InvokeButton(ButtonIn(view, "Marcar como realizado")); PumpUntil(() => !health.IsBusy && health.Panel == HealthPanel.Lists);
                Require(fixture.Count("PATCH", "/status") == 2 && health.Exams.Single(item => item.Name == "Ultrassom").Status == "done", "A data válida marca o exame como realizado e fecha o formulário.");

                // Nova vacina.
                InvokeButton(newVaccine); popup.UpdateLayout();
                Require(health.Panel == HealthPanel.NewVaccine && VisibleTexts(view).Contains("Nova vacina") && Descendants<ComboBox>(view).Any(box => box.IsVisible && AutomationProperties.GetName(box) == "Dose da vacina"), "Nova vacina abre o formulário com a dose.");
                Descendants<TextBox>(view).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Nome da vacina").Text = "Hepatite B";
                Descendants<TextBox>(view).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Data de aplicação da vacina").Text = "15/02/2026";
                Descendants<ComboBox>(view).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Dose da vacina").SelectedIndex = 3;
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-nova-vacina-claro"));
                InvokeButton(ButtonIn(view, "Registrar vacina")); PumpUntil(() => !health.IsBusy && health.Panel == HealthPanel.Lists);
                Require(health.Vaccines.Any(item => item.Name == "Hepatite B" && item.Dose == "booster") && vaccineList.IsVisible, "A vacina registrada deveria entrar na lista, com a dose escolhida.");

                // Anular: o motivo vem num formulário próprio, com a confirmação de sempre.
                vaccineList.SelectedItems.Clear(); examList.SelectedItems.Clear();
                vaccineList.SelectedItems.Add(vaccineList.Items.Cast<VaccineInfo>().Single(item => item.Name == "Hepatite B")); popup.UpdateLayout();
                InvokeButton(voidVaccine); popup.UpdateLayout();
                Require(health.Panel == HealthPanel.Void && VisibleTexts(view).Any(text => text.StartsWith("Vacina selecionada: Hepatite B")), "Anular abre o formulário do motivo, dizendo qual é a vacina.");
                var reason = Descendants<TextBox>(view).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Motivo da anulação");
                var voidButton = ButtonIn(view, "Anular registro");
                reason.Text = "ab"; asked.Clear(); InvokeButton(voidButton); PumpUntil(() => !health.IsBusy);
                Require(asked.Count == 0 && health.VoidReasonError.Contains("3 a 280") && fixture.Count("POST", "/void") == 0, "Motivo curto demais não chega à confirmação.");
                reason.Text = "Aplicada no paciente errado"; answer = false; InvokeButton(voidButton); PumpUntil(() => !health.IsBusy);
                Require(asked.Count == 1 && asked[0].Title == "Anular registro" && asked[0].Text.Contains("Hepatite B") && fixture.Count("POST", "/void") == 0 && health.Panel == HealthPanel.Void, "Recusar a confirmação mantém o formulário aberto e não anula.");
                Require(health.MessageKind != MessageKind.Error || !health.HasMessage, "Com o motivo corrigido, o erro da tentativa anterior não pode continuar na faixa.");
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-anular-claro"));
                answer = true; InvokeButton(voidButton); PumpUntil(() => !health.IsBusy && health.Panel == HealthPanel.Lists);
                Require(fixture.Count("POST", "/void") == 1 && health.Vaccines.All(item => item.Name != "Hepatite B"), "Confirmar anula com o motivo e tira o registro da lista.");

                // Ficha em linhas do registro (Enter / segundo clique / duplo clique): abre no lugar das listas, uma de cada vez.
                SelectExam("Hemograma");
                PressKey(examList, Key.Enter); PumpUntil(() => health.Panel == HealthPanel.Detail); popup.UpdateLayout();
                Require(health.PanelTitle == "Hemograma" && health.DetailFields.Any(field => field.Label == "Situação" && field.Value == "Realizado") && VisibleTexts(view).Contains("Hemograma") && VisibleTexts(view).Contains("Realizado em") && !examList.IsVisible, "A ficha do exame deveria abrir em linhas, no lugar das listas.");
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-registro-claro"));
                InvokeButton(ButtonIn(view, "Voltar às listas")); popup.UpdateLayout();
                Require(health.Panel == HealthPanel.Lists && examList.IsVisible, "Voltar às listas fecha a ficha do registro.");

                // Releitura automática (20 s): sem botão, sem piscar, e parada enquanto um formulário está aberto.
                SelectExam("Hemograma");
                var reads = fixture.Count("POST", "/health-records/query"); var flicker = 0; examBalloon.IsVisibleChanged += (_, _) => flicker++;
                Pump(health.AutoRefreshAsync());
                Require(fixture.Count("POST", "/health-records/query") == reads + 1 && flicker == 0 && examBalloon.IsVisible && health.SelectedExam?.Name == "Hemograma", "A releitura automática não pode piscar o balão nem derrubar a seleção.");
                fixture.AddExam(unit.Id, patient.Cpf, "Glicemia"); Pump(health.AutoRefreshAsync());
                Require(health.Exams.Any(item => item.Name == "Glicemia") && flicker == 0 && health.SelectedExam?.Name == "Hemograma" && examBalloon.IsVisible, "Um registro novo entra na lista sem derrubar a seleção.");
                InvokeButton(newExam); reads = fixture.Count("POST", "/health-records/query"); Pump(health.AutoRefreshAsync());
                Require(fixture.Count("POST", "/health-records/query") == reads, "Com o formulário aberto a releitura automática espera.");
                InvokeButton(ButtonIn(view, "Cancelar"));
                fixture.FailHealthRecords = true; reads = fixture.Count("POST", "/health-records/query"); Pump(health.AutoRefreshAsync());
                Require(health.Exams.Count >= 3 && !health.HasMessage, "Uma falha da releitura automática mantém a lista e não enche a tela de erro."); fixture.FailHealthRecords = false;

                // Capturas com dados, nos dois temas (lista com balão e a lista sem seleção).
                SelectExam("Raio-X de tórax"); popup.UpdateLayout();
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-exames-vacinas-balao-claro"));
                window.ViewModel.SetAppearance(Appearance.Dark); popup.UpdateLayout();
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-exames-vacinas-balao-escuro"));
                InvokeButton(newExam); popup.UpdateLayout();
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-novo-exame-escuro"));
                InvokeButton(ButtonIn(view, "Cancelar"));
                window.ViewModel.SetAppearance(Appearance.Light);
                popup.Close(); popup = null;

                // Cadastro desativado: só consulta, com o aviso no visual de NoticeItem e sem ações.
                var inactive = vm.ObsRegistry.Single(item => item.Cpf == "11144477735");
                var readOnly = vm.CreateHealthRecords(inactive)!;
                popup = new ItemDetailsWindow(vm.BuildDetails(inactive)!, new ClipboardFixture(), readOnly) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                popup.Show(); popup.UpdateLayout();
                InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Aba Exames e vacinas")); PumpUntil(() => !readOnly.IsBusy); popup.UpdateLayout();
                var readView = Descendants<HealthRecordsView>(popup).Single(item => item.IsVisible);
                Require(!readOnly.CanRegister && !ButtonIn(readView, "Novo exame").IsEnabled && !ButtonIn(readView, "Nova vacina").IsEnabled && Descendants<ContentControl>(readView).Any(item => item.IsVisible && item.Style == (Style)application.FindResource("NoticeItem")), "Cadastro desativado: botões novos desabilitados e o aviso em destaque.");
                SavePopupScrolled(popup, Path.Combine(previewRoot, "obs-ficha-exames-vacinas-vazio-claro"));
                popup.Close(); popup = null;
            }
            finally { Confirmation.Handler = null; popup?.Close(); CloseWindow(window); }
        });

        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{Cfa}: {profile.Name}: Dados do perfil no pop-up da marca, com nome, e-mail e função em linhas e a troca de senha num bloco próprio", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Perfil");
            var window = OpenProfile(profile, fixture, storageRoot, "cfa-profile-" + profile.Id, Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var overlay = (FrameworkElement)window.FindName("DialogOverlay");
                var dialog = new StaffProfileWindow(vm) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                dialog.Show();
                try
                {
                    var fields = (FrameworkElement)dialog.FindName("ProfileFields");
                    PumpUntil(() => fields.IsEnabled); dialog.UpdateLayout();
                    var content = (FrameworkElement)dialog.Content;
                    Require(dialog.WindowStyle == WindowStyle.None && dialog.AllowsTransparency && dialog.Title == "Dados do perfil", "Dados do perfil deveria ser o pop-up da marca, sem moldura do Windows.");
                    Require(overlay.Visibility == Visibility.Visible, "O fundo da janela principal deveria escurecer enquanto o pop-up está aberto.");
                    var close = Descendants<Button>(content).Single(button => AutomationProperties.GetName(button) == "Fechar dados do perfil");
                    Require(close.TranslatePoint(new Point(0, 0), dialog).X > dialog.ActualWidth - 90 && close.TranslatePoint(new Point(0, 0), dialog).Y < 70, "O X fica no canto superior direito.");
                    var texts = VisibleTexts(content);
                    foreach (var label in new[] { "Dados do perfil", "Nome completo", "E-mail", "Função", "Alterar senha" }) Require(texts.Contains(label), $"Faltou \"{label}\" no pop-up.");
                    Require(!HasCardBorder(content) && !HasRefreshButton(content), "O pop-up não tem card dentro.");
                    Require(((TextBox)dialog.FindName("Email")).IsReadOnly && Descendants<FormGrid>(content).Count(grid => grid.IsVisible) == 1, "O e-mail é só leitura e a troca de senha usa a grade de cadastro.");
                    var saveName = Descendants<Button>(content).Single(button => button.Content as string == "Salvar nome");
                    var savePassword = Descendants<Button>(content).Single(button => button.Content as string == "Salvar nova senha");
                    Require(saveName.TranslatePoint(new Point(0, 0), dialog).X > dialog.ActualWidth / 2 && savePassword.TranslatePoint(new Point(0, 0), dialog).X > dialog.ActualWidth / 2, "Os botões primários ficam à direita.");
                    foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                    {
                        window.ViewModel.SetAppearance(mode); dialog.UpdateLayout();
                        // A janela inteira (com a margem da sombra): o conteúdo sozinho sai deslocado pela margem e cortado à direita e embaixo.
                        SavePreview(dialog, Path.Combine(previewRoot, $"{profile.Id}-dados-do-perfil-{mode.ToString().ToLowerInvariant()}.png"));
                    }
                    window.ViewModel.SetAppearance(Appearance.Light);
                    InvokeButton(close);
                    Require(!dialog.IsVisible && overlay.Visibility == Visibility.Collapsed, "O X fecha o pop-up e o fundo volta ao normal.");
                }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }
            finally { CloseWindow(window); }
        });

        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{Cfa}: {profile.Name}: troca obrigatória de senha sem card nem título repetido, com a grade e o Sair à esquerda do primário", () =>
        {
            using var fixture = new ApiFixture { MustChangePassword = true };
            fixture.AddUnit("UBS Provisória");
            var window = OpenProfile(profile, fixture, storageRoot, "cfa-required-" + profile.Id, Appearance.Light);
            try
            {
                LoginObs(window);
                var content = Layout(window, 1280, 900);
                var view = Descendants<PasswordChangeRequiredView>(content).Single(item => item.IsVisible);
                Require(!HasCardBorder(view) && !VisibleTexts(view).Contains("Defina uma nova senha") && window.ViewModel.PageLabel == "Definir nova senha", "O nome da tela fica só no cabeçalho, e a tela não é um card.");
                Require(VisibleTexts(view).Any(text => text.StartsWith("Sua senha é provisória", StringComparison.Ordinal)) && Descendants<FormGrid>(view).Count(grid => grid.IsVisible) == 1, "Instrução curta e formulário em grade.");
                var logout = Descendants<Button>(view).Single(button => button.Content as string == "Sair"); var save = Descendants<Button>(view).Single(button => button.Content as string == "Salvar nova senha");
                Require(logout.TranslatePoint(new Point(0, 0), view).X < save.TranslatePoint(new Point(0, 0), view).X && Math.Abs(logout.TranslatePoint(new Point(0, 0), view).Y - save.TranslatePoint(new Point(0, 0), view).Y) < 8, "Sair fica à esquerda do primário, na mesma linha.");
                var boxes = Descendants<PasswordBox>(view).Where(box => box.IsVisible).ToArray();
                Require(boxes.Length == 3 && boxes.Select(box => Math.Round(box.ActualWidth)).Distinct().Count() == 1, "Os três campos têm a mesma largura.");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    window.ViewModel.SetAppearance(mode); content = Layout(window, 1280, 900);
                    SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-troca-obrigatoria-{mode.ToString().ToLowerInvariant()}.png"));
                }
                content = Layout(window, 980, 640);
                SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-troca-obrigatoria-janela-minima.png"));
                Require(Descendants<PasswordBox>(content).Where(box => box.IsVisible).All(box => box.TranslatePoint(new Point(box.ActualWidth, 0), content).X <= 980), "Na janela mínima nada passa da borda.");
                var session = window.ViewModel.Workspace;
                InvokeButton(Descendants<Button>(Descendants<PasswordChangeRequiredView>(content).Single(item => item.IsVisible)).Single(button => button.Content as string == "Sair")); Settle(session);
                Require(session.IsSignedOut && !session.MustChangePassword, "O botão Sair da troca obrigatória deveria encerrar a sessão.");
            }
            finally { CloseWindow(window); }
        });

        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{Cfa}: {profile.Name}: tela de acesso centralizada, com os botões da janela, o texto do login salvo e o hover azul bebê, nos dois temas e na janela mínima", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Acesso");
            var storage = Path.Combine(storageRoot, "cfa-access-" + profile.Id);
            var window = new ShellWindow(profile, storage, new NoraApiClient(fixture), new ObsDeviceStore(storage), compactAccess: true);
            window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false; window.ViewModel.Workspace.Endpoint = "https://fixture.invalid";
            PrepareWindow(window);
            try
            {
                foreach (var (mode, width, height, label) in new[] { (Appearance.Light, 1080d, 740d, "claro"), (Appearance.Dark, 1080d, 740d, "escuro"), (Appearance.Light, 980d, 640d, "janela-minima") })
                {
                    window.ViewModel.SetAppearance(mode);
                    var content = Layout(window, width, height);
                    var access = Descendants<DesktopAccessView>(content).Single(item => item.IsVisible);
                    var card = Descendants<Border>(access).First(border => ReferenceEquals(border.Style, Application.Current.FindResource("Card")));
                    var center = card.TranslatePoint(new Point(card.ActualWidth / 2, 0), content).X;
                    Require(Math.Abs(center - width / 2) < 3 && card.ActualWidth == 420, $"{label}: o cartão do login deveria ficar centralizado.");
                    Require(ReferenceEquals(access.Background, Application.Current.FindResource("BackgroundGradientBrush")), $"{label}: a tela de acesso pinta o próprio fundo, para a marca d'água da janela não aparecer atrás do cartão.");
                    Require(card.TranslatePoint(new Point(0, 0), content).Y >= 35 && card.TranslatePoint(new Point(0, card.ActualHeight), content).Y <= height, $"{label}: o login não pode passar da janela.");
                    var enter = Descendants<Button>(access).Single(button => button.Content as string == "Entrar");
                    var help = VisibleTexts(access).Single(text => text.StartsWith("Sem marcar", StringComparison.Ordinal));
                    Require(help == "Sem marcar, você continua conectado por 1 dia neste computador." && enter.ActualWidth > 300 && enter.TranslatePoint(new Point(enter.ActualWidth, 0), content).X <= card.TranslatePoint(new Point(card.ActualWidth, 0), content).X, $"{label}: o texto do login salvo e o botão cabem no cartão.");
                    Require(new[] { "MinimizeButton", "MaximizeButton", "CloseButton" }.All(name => ((FrameworkElement)window.FindName(name)).IsVisible) && !((FrameworkElement)window.FindName("TitleTools")).IsVisible, $"{label}: só os botões da janela aparecem na barra de título.");
                    Require(!VisibleTexts((FrameworkElement)window.FindName("TitleBar")).Contains(profile.Name), $"{label}: o nome do programa não é desenhado na barra de título.");
                    SavePreview(content, Path.Combine(previewRoot, $"{profile.Id}-acesso-{label}.png"));
                }
            }
            finally { CloseWindow(window); }
        });

        Check($"{Cfa}: aviso de UBS no visual de NoticeItem, com o botão Escolher UBS; tela protegida sem sessão sem título repetido", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Norte", "Norte"); fixture.AddUnit("UBS Sul", "Sul");
            var window = OpenOperational(fixture, storageRoot, "cfa-notice", Appearance.Light);
            try
            {
                var signedOut = Show(window, "patients", 1280, 900);
                var access = Descendants<OperationalAccessView>(signedOut).Single(item => item.IsVisible);
                Require(!HasCardBorder(access) && !VisibleTexts(access).Contains("Pacientes da unidade") && VisibleTexts(access).Contains("Entre no Painel da UBS para carregar os pacientes autorizados da sua unidade.") && Descendants<Button>(access).Any(button => button.Content as string == "Ir para acesso"),
                    "A tela protegida sem sessão mostra só o aviso e o caminho para entrar, sem o título repetido.");
                SavePreview(signedOut, Path.Combine(previewRoot, "obs-acesso-protegido-claro.png"));
                var vm = LoginObs(window);
                var agenda = Show(window, "agenda", 1280, 900);
                var notice = Descendants<ObsUnitNoticeView>(agenda).Single(item => item.IsVisible);
                var item = Descendants<ContentControl>(notice).Single(control => control.IsVisible && control.Style == (Style)application.FindResource("NoticeItem"));
                var choose = Descendants<Button>(notice).Single(button => button.IsVisible && button.Content as string == "Escolher UBS");
                Require(vm.NeedsObsUnitChoice && VisibleTexts(notice).Any(text => text.Contains("2 UBS", StringComparison.Ordinal)) && !HasCardBorder(notice) && choose.TranslatePoint(new Point(0, 0), notice).X > notice.ActualWidth / 2, "O aviso deveria ser um NoticeItem curto, com o botão à direita.");
                SavePreview(agenda, Path.Combine(previewRoot, "obs-aviso-ubs-claro.png"));
                window.ViewModel.SetAppearance(Appearance.Dark);
                SavePreview(Layout(window, 1280, 900), Path.Combine(previewRoot, "obs-aviso-ubs-escuro.png"));
            }
            finally { CloseWindow(window); }
        });
    }
}
