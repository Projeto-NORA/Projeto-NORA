using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Redesenho do OBS: Vaga Relâmpago (Em andamento e Encerradas), Alertas e Comunicados. Listagem aberta na tela inteira, sem título nem totais nem botão Atualizar,
/// filtros no cabeçalho, ficha em linhas, balão de "Marcar como lido" em lote e as capturas dos dois temas (com dados e vazio).
/// </summary>
internal static partial class Program
{
    private const string RedesignGroup = "Redesenho OBS (vagas, alertas e comunicados): ";

    /// <summary>Lê os comunicados por inteiro, como a tela faz ao ser aberta (sai da tela e volta).</summary>
    private static void ReadAnnouncementsFully(ShellWindow window, WorkspaceViewModel vm)
    {
        window.ViewModel.Navigate("home"); Settle(vm);
        window.ViewModel.Navigate("announcements"); Settle(vm);
    }

    /// <summary>Deixa a última avaliação dos alertas "velha" e roda o ciclo automático, que é o que a tela de alertas faz sozinha.</summary>
    private static void RefreshAlertsNow(WorkspaceViewModel vm)
    {
        AgeAlertsRead(vm, TimeSpan.FromMinutes(10));
        Pump(vm.RefreshAutomaticallyAsync()); Settle(vm);
    }

    /// <summary>Deixa o dispatcher trabalhar por um tempo (os temporizadores da tela correm).</summary>
    private static void PumpFor(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        PumpUntil(() => DateTime.UtcNow >= end);
    }

    /// <summary>Seleciona itens da lista pelo mesmo caminho do usuário (a coleção de itens selecionados) e deixa o layout e o balão se ajustarem.</summary>
    private static void SelectRows(ShellWindow window, ListBox list, double width, double height, params object[] items)
    {
        list.SelectedItems.Clear();
        foreach (var item in items) list.SelectedItems.Add(item);
        PumpFor(TimeSpan.FromMilliseconds(60));
        Layout(window, width, height);
    }

    private static void CaptureBoth(ShellWindow window, string folder, string name, double width, double height)
    {
        foreach (var (mode, suffix) in new[] { (Appearance.Light, "claro"), (Appearance.Dark, "escuro") })
        {
            window.ViewModel.SetAppearance(mode);
            var content = Layout(window, width, height);
            Require(Descendants<PasswordBox>(content).All(box => box.Password.Length == 0), "Captura recusada: campo de senha preenchido.");
            SavePreview(content, Path.Combine(folder, $"{name}-{suffix}.png"));
        }
        window.ViewModel.SetAppearance(Appearance.Light);
    }

    private static void CapturePopupBoth(ShellWindow window, Window popup, string folder, string name)
    {
        foreach (var (mode, suffix) in new[] { (Appearance.Light, "claro"), (Appearance.Dark, "escuro") })
        {
            window.ViewModel.SetAppearance(mode);
            popup.UpdateLayout();
            PumpFor(TimeSpan.FromMilliseconds(40));
            popup.UpdateLayout();
            SavePreview(popup, Path.Combine(folder, $"{name}-{suffix}.png"));
        }
        window.ViewModel.SetAppearance(Appearance.Light);
    }

    private static Window ShowDetails(ShellWindow window, ItemDetails details)
    {
        var popup = new ItemDetailsWindow(details, new ClipboardFixture()) { Owner = window, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
        popup.Show(); popup.UpdateLayout();
        return popup;
    }

    private static string RedesignFolder(string[] args)
    {
        var folder = Path.Combine(PreviewRoot(args), "vagas-alertas-comunicados");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void RunVagasAlertasComunicadosChecks(string storageRoot, string[] args)
    {
        var folder = RedesignFolder(args);
        RunRedesignSlotChecks(storageRoot, folder);
        RunRedesignAlertChecks(storageRoot, folder);
        RunRedesignAnnouncementChecks(storageRoot, folder);
    }

    // ---------------------------------------------------------------- Vaga Relâmpago

    private static void RunRedesignSlotChecks(string storageRoot, string folder)
    {
        Check(RedesignGroup + "Vaga Relâmpago em andamento e encerradas: listagem aberta na tela inteira, sem título, totais nem Atualizar, filtros no cabeçalho, só leitura", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Centro", "Centro");
            var day = DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(12);
            var open = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Cardiologia", "Dra. Helena Prado", day, day.AddMinutes(30), "open", DateTimeOffset.UtcNow.AddMinutes(-4)));
            var urgent = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Pediatria", "Dr. Marcos Vieira", day.AddHours(1), day.AddHours(1).AddMinutes(30), "offered", DateTimeOffset.UtcNow.AddMinutes(-20)));
            var calm = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Clínica geral", "Dra. Lúcia Matos", day.AddHours(2), day.AddHours(2).AddMinutes(30), "offered", DateTimeOffset.UtcNow.AddMinutes(-9)));
            var accepted = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Ortopedia", "Dr. Rafael Nunes", day.AddHours(3), day.AddHours(3).AddMinutes(30), "accepted", DateTimeOffset.UtcNow.AddHours(-3)));
            var tooLate = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Dermatologia", "Dra. Ana Beltrão", day.AddHours(-9), day.AddHours(-9).AddMinutes(30), "closed", DateTimeOffset.UtcNow.AddHours(-5), ClosedReason: "too-late"));
            var conflict = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Oftalmologia", "Dr. Caio Mendes", day.AddHours(-8), day.AddHours(-8).AddMinutes(30), "closed", DateTimeOffset.UtcNow.AddHours(-6), ClosedReason: "conflict"));
            var now = DateTimeOffset.UtcNow;
            fixture.Offers[urgent.Id] = [new(Guid.NewGuid(), "Maria Souza", "declined", now.AddMinutes(-8)), new(Guid.NewGuid(), "Paulo Ribeiro", "pending", now.AddSeconds(90))];
            fixture.Offers[calm.Id] = [new(Guid.NewGuid(), "Beatriz Lima", "pending", now.AddMinutes(8))];
            fixture.Offers[accepted.Id] = [new(Guid.NewGuid(), "Carlos Pires", "expired", now.AddHours(-2)), new(Guid.NewGuid(), "Joana Alves", "accepted", now.AddHours(-1))];
            fixture.Offers[tooLate.Id] = [new(Guid.NewGuid(), "Renata Dias", "expired", now.AddHours(-4))];
            var window = OpenOperational(fixture, storageRoot, "redesign-slots", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots:live", 1280, 740); PumpFor(TimeSpan.FromMilliseconds(400)); content = Layout(window, 1280, 740);
                var view = Descendants<UnitLightningSlotsView>(content).Single(item => item.IsVisible);
                var list = ListNamed(view, "Vagas da UBS");
                // Sem título, sem a faixa de totais, sem tabela e sem botão Atualizar.
                var texts = FullTexts(view);
                Require(!texts.Contains("Vaga Relâmpago") && !texts.Contains("Vagas abertas") && !texts.Contains("Recusadas") && !texts.Contains("Vencidas") && !texts.Contains("Aceitas"), "A tela repetiu o título ou mostrou a faixa de totais: " + string.Join(" | ", texts.Where(text => text.Length < 30)));
                Require(!Descendants<DataGrid>(view).Any() && !Descendants<Button>(view).Any(button => AutomationProperties.GetName(button).StartsWith("Atualizar", StringComparison.Ordinal)), "A tela manteve a tabela ou o botão Atualizar.");
                Require(!Descendants<Border>(view).Any(border => border.Style == (Style)Application.Current.FindResource("Card")), "A listagem não pode ficar dentro de um card.");
                // Em andamento: só as vagas abertas e ofertadas, a que vence primeiro no topo.
                Require(list.Items.Count == 3 && vm.SlotPanelScope == "live", $"Em andamento deveria ter 3 vagas (tem {list.Items.Count}).");
                var rows = list.Items.Cast<SlotPanelRow>().ToArray();
                Require(rows[0].Slot.Id == open.Id && rows[1].Slot.Id == urgent.Id && rows[2].Slot.Id == calm.Id, "A ordem deveria ser pelo horário da vaga.");
                Require(rows[0].ItemTitle == $"{open.TimeDisplay} · Cardiologia" && texts.Contains(rows[0].ItemTitle), "O título deveria ser o horário e a especialidade.");
                Require(texts.Contains("Dra. Helena Prado | Procurando paciente…"), "Vaga aberta: profissional e paciente na linha de dados: " + string.Join(" | ", texts.Where(text => text.StartsWith("Dr", StringComparison.Ordinal))));
                Require(texts.Any(text => text.StartsWith("Dr. Marcos Vieira | Paulo Ribeiro | prazo 01:", StringComparison.Ordinal)) && texts.Any(text => text.StartsWith("Dra. Lúcia Matos | Beatriz Lima | prazo 0", StringComparison.Ordinal)), "A linha de dados deveria ser profissional | paciente | prazo: " + string.Join(" | ", texts.Where(text => text.StartsWith("Dr", StringComparison.Ordinal))));
                Require(new[] { "Aberta", "Em oferta" }.All(tag => texts.Contains(tag)) && !texts.Contains("Encerrada"), "As etiquetas de situação não aparecem (ou há de encerrada em andamento).");
                // Contagem regressiva em vermelho só nos últimos 2 minutos.
                var urgentRun = ((TextBlock)FindTextBlock(list, "Dr. Marcos Vieira")).Inlines.OfType<Run>().Last();
                var calmRun = ((TextBlock)FindTextBlock(list, "Dra. Lúcia Matos")).Inlines.OfType<Run>().Last();
                Require(rows[1].IsUrgent && !rows[2].IsUrgent && ((SolidColorBrush)urgentRun.Foreground).Color == ((SolidColorBrush)Application.Current.FindResource("ErrorBrush")).Color && ((SolidColorBrush)calmRun.Foreground).Color != ((SolidColorBrush)Application.Current.FindResource("ErrorBrush")).Color, "O prazo dos últimos 2 minutos deveria ficar em vermelho; os outros, não.");
                // A tela ocupa a altura da área de conteúdo e a rolagem da página fica desligada; os filtros vão para o botão Filtros do cabeçalho.
                var scroll = (ScrollViewer)window.FindName("PageScroll");
                Require(scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled && ((RowDefinition)window.FindName("PageRow")).Height.IsStar && list.ActualHeight > 250, $"A listagem deveria ocupar a tela inteira (rolagem {scroll.VerticalScrollBarVisibility}, lista {list.ActualHeight:F0}).");
                Require(((Button)window.FindName("FiltersButton")).IsVisible, "Os filtros deveriam estar no botão Filtros do cabeçalho.");
                Require(!Descendants<ComboBox>(view).Any(box => box.IsVisible), "Os filtros não deveriam ficar dentro da tela.");
                var specialty = Combo(content, "Filtrar vagas por especialidade");
                Require(specialty.Items.Count == 7 && Combo(content, "Filtrar vagas por situação").Items.Count == 7 && PageField<TextBox>(content, box => AutomationProperties.GetName(box) == "Pesquisar vagas por paciente ou profissional") is not null, "O popup de filtros deveria trazer especialidade, situação e pesquisa.");
                SavePreview(OpenPageFilters(window), Path.Combine(folder, "vagas-filtros-claro.png"));
                ((Popup)window.FindName("FiltersPopup")).IsOpen = false;
                CaptureBoth(window, folder, "vagas-em-andamento", 1280, 740);
                // Só leitura: o balão fica escondido mesmo com itens selecionados; Esc desfaz a seleção.
                var balloon = (SelectionBalloon)view.FindName("NoBalloon");
                SelectRows(window, list, 1280, 740, rows[1]);
                Require(!balloon.IsVisible && list.SelectedItems.Count == 1, "Vaga relâmpago é só leitura: não deveria haver balão de ações.");
                CaptureBoth(window, folder, "vagas-em-andamento-selecionada", 1280, 740);
                // O temporizador de 1 s e o ciclo de 20 s não recriam a lista nem derrubam a seleção.
                var container = list.ItemContainerGenerator.ContainerFromIndex(1);
                var changes = 0; ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, _) => changes++;
                var before = rows[1].DeadlineText;
                PumpFor(TimeSpan.FromMilliseconds(2300));
                Require(rows[1].DeadlineText != before && changes == 0 && list.SelectedItems.Count == 1 && ReferenceEquals(list.ItemContainerGenerator.ContainerFromIndex(1), container), $"O prazo deveria andar a cada segundo sem mexer na lista ({before} → {rows[1].DeadlineText}; {changes} mudanças).");
                for (var cycle = 0; cycle < 2; cycle++) { Pump(vm.RefreshAutomaticallyAsync()); PumpFor(TimeSpan.FromMilliseconds(300)); }
                Require(changes == 0 && list.SelectedItems.Count == 1 && ReferenceEquals(list.SelectedItem, rows[1]) && ReferenceEquals(list.ItemContainerGenerator.ContainerFromIndex(1), container), $"O ciclo de 20 s não deveria recriar a lista nem derrubar a seleção ({changes} mudanças).");
                // Se a vaga muda de situação, a seleção volta para ela pela chave (a linha é nova, o Id é o mesmo).
                fixture.Offers[urgent.Id] = [new(Guid.NewGuid(), "Paulo Ribeiro", "declined", now.AddMinutes(-1)), new(Guid.NewGuid(), "Edna Rocha", "pending", now.AddMinutes(4))];
                Pump(vm.RefreshAutomaticallyAsync()); PumpFor(TimeSpan.FromMilliseconds(500));
                Require(list.SelectedItems.Count == 1 && ((SlotPanelRow)list.SelectedItem).Id == urgent.Id && ((SlotPanelRow)list.SelectedItem).PatientName == "Edna Rocha", "A seleção deveria acompanhar a vaga pela chave quando as ofertas mudam.");
                // Enter e o segundo clique abrem a ficha da vaga selecionada.
                // A ficha de verdade (a que a tela usa): abre a ficha da vaga, com as ofertas, e fecha ao fim da leitura.
                var realHandler = ListingBehavior.GetOpenHandler(list) ?? throw new InvalidOperationException("A lista deveria ter o abridor de ficha da vaga.");
                string? realTitle = null; var offerLines = 0;
                var closer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                closer.Tick += (_, _) =>
                {
                    if (Application.Current.Windows.OfType<ItemDetailsWindow>().FirstOrDefault() is not { } dialog) return;
                    realTitle = dialog.Title; offerLines = Descendants<TextBlock>(dialog).Count(text => text.Text.Contains("· prazo ", StringComparison.Ordinal));
                    closer.Stop(); dialog.Close();
                };
                closer.Start();
                try { Pump(realHandler(rows[1])); } finally { closer.Stop(); }
                Require(realTitle == "Pediatria" && offerLines == 2, $"A ficha da lista deveria ser a da vaga, com as duas ofertas ({realTitle}; {offerLines} ofertas).");
                var opened = new List<Guid>();
                ListingBehavior.SetOpenHandler(list, item => { opened.Add(((SlotPanelRow)item).Id); return Task.CompletedTask; });
                Require(PressKey(list, Key.Enter), "Enter deveria abrir a ficha.");
                PumpUntil(() => opened.Count == 1);
                Require(opened[0] == urgent.Id, "A ficha aberta não é a da vaga selecionada.");
                // O duplo clique (um clique na linha já selecionada e o segundo logo depois) abre a ficha uma vez só.
                var selectedRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
                foreach (var clicks in new[] { 1, 2 })
                {
                    selectedRow.RaiseEvent(MouseArgs(Mouse.PreviewMouseDownEvent, selectedRow, clicks)); selectedRow.RaiseEvent(MouseArgs(Mouse.MouseDownEvent, selectedRow, clicks));
                    selectedRow.RaiseEvent(MouseArgs(Mouse.PreviewMouseUpEvent, selectedRow, clicks)); selectedRow.RaiseEvent(MouseArgs(Mouse.MouseUpEvent, selectedRow, clicks));
                }
                PumpUntil(() => opened.Count == 2); PumpFor(TimeSpan.FromMilliseconds(700));
                Require(opened.Count == 2 && opened[1] == urgent.Id, $"O duplo clique deveria abrir a ficha uma vez ({opened.Count - 1} aberturas).");
                var ficha = vm.BuildDetailsAsync(urgent); Pump(ficha);
                Require(ficha.Result!.Fields.Any(field => field.Label == "Profissional" && field.Value == "Dr. Marcos Vieira") && ficha.Result.Fields.Count(field => field.Group == "Ofertas da vaga") == 2, "A ficha deveria trazer o resumo da vaga e a sequência de ofertas.");
                var popup = ShowDetails(window, ficha.Result);
                try { CapturePopupBoth(window, popup, folder, "vagas-ficha"); }
                finally { popup.Close(); }
                // Encerradas: o resto, as mais recentes primeiro; etiquetas do desfecho; sem prazo correndo.
                content = Show(window, "slots:closed", 1280, 740); PumpFor(TimeSpan.FromMilliseconds(400)); content = Layout(window, 1280, 740);
                list = ListNamed(Descendants<UnitLightningSlotsView>(content).Single(item => item.IsVisible), "Vagas da UBS");
                texts = FullTexts(content);
                Require(list.Items.Count == 3 && texts.Contains("Aceita") && texts.Contains("Menos de 10 min") && texts.Contains("Horário ocupado") && !texts.Any(text => text.Contains("prazo ", StringComparison.Ordinal)), "Encerradas deveria listar a aceita e as duas fechadas, sem prazo.");
                Require(texts.Any(text => text.StartsWith("Dr. Rafael Nunes | Joana Alves", StringComparison.Ordinal)), "A vaga aceita deveria mostrar quem aceitou: " + string.Join(" | ", texts.Where(text => text.StartsWith("Dr", StringComparison.Ordinal))));
                CaptureBoth(window, folder, "vagas-encerradas", 1280, 740);
                // Filtros: um filtro que esconde tudo é "nenhuma vaga encontrada para esta busca", diferente de "nenhuma vaga encerrada".
                vm.SlotSearch = "zzzz"; content = Layout(window, 1280, 740);
                Require(vm.SlotsEmptyText == "Nenhuma vaga encontrada para esta busca." && FullTexts(content).Contains(vm.SlotsEmptyText), "A busca sem resultado deveria dizer isso na lista: " + vm.SlotsEmptyText);
                CaptureBoth(window, folder, "vagas-busca-vazia", 1280, 740);
                Pump(vm.ClearPageFiltersAsync(new HashSet<string> { "slots" }));
                Require(vm.SlotSearch == "" && vm.SlotStatusFilter == "Todas" && vm.SlotSpecialtyFilter == ViewModels.WorkspaceViewModel.AllSpecialties && list.Items.Count == 3, "Limpar filtros deveria restaurar a lista.");
                // Largura mínima da janela.
                content = Layout(window, 980, 640);
                CaptureBoth(window, folder, "vagas-encerradas-janela-minima", 980, 640);
                Show(window, "slots:live", 980, 640); PumpFor(TimeSpan.FromMilliseconds(300));
                CaptureBoth(window, folder, "vagas-em-andamento-janela-minima", 980, 640);
            }
            finally { CloseWindow(window); }
        });
        Check(RedesignGroup + "Vaga Relâmpago encerradas: o histórico longo continua com o botão Carregar mais histórico, num rodapé discreto abaixo da lista", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true, HistoryPageSize = 2 };
            var unit = fixture.AddUnit("UBS Histórico", "Sul");
            var start = DateTimeOffset.UtcNow.AddDays(-2);
            for (var index = 0; index < 5; index++)
                fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Cardiologia", "Dra. Helena Prado", start.AddHours(index), start.AddHours(index).AddMinutes(30), "closed", DateTimeOffset.UtcNow.AddDays(-2).AddHours(index)));
            var window = OpenOperational(fixture, storageRoot, "redesign-slots-history", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots:closed", 1280, 740); PumpFor(TimeSpan.FromMilliseconds(300)); content = Layout(window, 1280, 740);
                var list = ListNamed(content, "Vagas da UBS");
                Require(list.Items.Count == 2 && vm.CanLoadMoreSlotsHistory && FullTexts(content).Any(text => text.Contains("Carregar mais histórico", StringComparison.Ordinal)), "Com histórico a mais, a lista mostra a primeira página e o rodapé com o botão.");
                CaptureBoth(window, folder, "vagas-encerradas-historico", 1280, 740);
                Click(window, content, "Carregar mais histórico das vagas", 1280, 740); PumpFor(TimeSpan.FromMilliseconds(300));
                Click(window, Layout(window, 1280, 740), "Carregar mais histórico das vagas", 1280, 740); PumpFor(TimeSpan.FromMilliseconds(300));
                content = Layout(window, 1280, 740);
                Require(list.Items.Count == 5 && !vm.CanLoadMoreSlotsHistory && !Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Carregar mais histórico das vagas"), $"O histórico inteiro deveria estar na lista e o botão sumir (lista com {list.Items.Count}).");
            }
            finally { CloseWindow(window); }
        });
        Check(RedesignGroup + "Vaga Relâmpago: estado vazio (nenhuma vaga cadastrada), nos dois temas", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Vazia", "Norte");
            var window = OpenOperational(fixture, storageRoot, "redesign-slots-empty", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots:live", 1280, 740); PumpFor(TimeSpan.FromMilliseconds(300)); content = Layout(window, 1280, 740);
                Require(vm.SlotsEmptyText == "Nenhuma vaga em andamento." || vm.SlotsEmptyText == "Nenhuma vaga cadastrada nesta UBS até agora.", "Sem vagas a lista deveria dizer que não há nenhuma: " + vm.SlotsEmptyText);
                Require(vm.SlotsEmptyText == "Nenhuma vaga cadastrada nesta UBS até agora." && FullTexts(content).Contains(vm.SlotsEmptyText) && !FullTexts(content).Contains("Nenhuma vaga encontrada para esta busca."), "Sem nenhuma vaga na UBS o estado é 'cadastrada', não 'encontrada para esta busca'.");
                CaptureBoth(window, folder, "vagas-vazio", 1280, 740);
            }
            finally { CloseWindow(window); }
        });
        Check(RedesignGroup + "Vaga Relâmpago encerradas: etiqueta Recusada ou Vencida pelo desfecho da última oferta; a leitura que falha não deixa o indicador girando", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Desfechos", "Leste");
            var start = DateTimeOffset.UtcNow.AddDays(-1);
            var declined = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Cardiologia", "Dra. Helena Prado", start, start.AddMinutes(30), "closed", start.AddHours(-3)));
            var expired = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Pediatria", "Dr. Marcos Vieira", start.AddHours(1), start.AddHours(1).AddMinutes(30), "closed", start.AddHours(-2)));
            var silent = fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Ortopedia", "Dr. Rafael Nunes", start.AddHours(2), start.AddHours(2).AddMinutes(30), "closed", start.AddHours(-1)));
            fixture.Offers[declined.Id] = [new(Guid.NewGuid(), "Carlos Pires", "expired", start.AddHours(-2)), new(Guid.NewGuid(), "Joana Alves", "declined", start.AddHours(-1))];
            fixture.Offers[expired.Id] = [new(Guid.NewGuid(), "Maria Souza", "declined", start.AddHours(-2)), new(Guid.NewGuid(), "Paulo Ribeiro", "expired", start.AddHours(-1))];
            var window = OpenOperational(fixture, storageRoot, "redesign-slots-outcomes", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "slots:closed", 1280, 740); PumpFor(TimeSpan.FromMilliseconds(400)); content = Layout(window, 1280, 740);
                var view = Descendants<UnitLightningSlotsView>(content).Single(item => item.IsVisible);
                var list = ListNamed(view, "Vagas da UBS");
                var rows = list.Items.Cast<SlotPanelRow>().ToDictionary(row => row.Id);
                Require(rows.Count == 3 && rows[declined.Id] is { StatusDisplay: "Recusada", StatusTone: Tone.Pink } && rows[expired.Id] is { StatusDisplay: "Vencida", StatusTone: Tone.Violet } && rows[silent.Id] is { StatusDisplay: "Encerrada", StatusTone: Tone.Neutral },
                    "A etiqueta da vaga encerrada sem aceite deveria ser o desfecho da última oferta: " + string.Join(", ", rows.Values.Select(row => row.StatusDisplay)));
                var texts = FullTexts(content);
                Require(new[] { "Recusada", "Vencida", "Encerrada" }.All(texts.Contains), "As etiquetas Recusada, Vencida e Encerrada deveriam aparecer na lista.");
                CaptureBoth(window, folder, "vagas-encerradas-desfechos", 1280, 740);
                // A primeira leitura das vagas ainda não chegou: indicador com o passo escrito. Se ela falha, a lista diz que falhou em vez de girar para sempre.
                var spinner = Descendants<StackPanel>(view).Single(panel => AutomationProperties.GetName(panel) == "Lendo as vagas");
                var failed = (TextBlock)view.FindName("SlotsFailed");
                fixture.FailReads = true;
                typeof(WorkspaceViewModel).GetMethod("ClearUnitLightningData", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
                Layout(window, 1280, 740);
                Require(vm.IsSlotsLoading && spinner.IsVisible && !failed.IsVisible, "Antes da leitura chegar a tela deveria mostrar o indicador com 'Lendo as vagas da UBS…'.");
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm); content = Layout(window, 1280, 740);
                Require(vm.IsSlotsLoading && vm.IsDataStale && failed.IsVisible && !spinner.IsVisible, "Com a leitura falhando, a lista deveria dizer que não conseguiu ler, sem o indicador girando.");
                CaptureBoth(window, folder, "vagas-falha", 1280, 740);
                fixture.FailReads = false;
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm); PumpFor(TimeSpan.FromMilliseconds(300)); content = Layout(window, 1280, 740);
                Require(!vm.IsSlotsLoading && !failed.IsVisible && !spinner.IsVisible && list.Items.Count == 3, "Com a leitura de volta, a lista deveria voltar sem o aviso de falha.");
            }
            finally { CloseWindow(window); }
        });
    }

    private static TextBlock FindTextBlock(DependencyObject root, string startsWith) =>
        Descendants<TextBlock>(root).First(text => new TextRange(text.ContentStart, text.ContentEnd).Text.StartsWith(startsWith, StringComparison.Ordinal));

    // ---------------------------------------------------------------- Alertas

    private static void RunRedesignAlertChecks(string storageRoot, string folder)
    {
        Check(RedesignGroup + "Alertas: só a listagem (tipo e especialidade, valor | limite | desde, etiqueta do tipo), pesquisa em linha, sem título nem Atualizar, estados distintos", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Centro", "Centro");
            foreach (var alert in SampleAlerts(unit)) fixture.AddAlert(unit.Id, alert);
            var window = OpenOperational(fixture, storageRoot, "redesign-alerts", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "alerts", 1280, 740);
                var view = Descendants<UnitAlertsView>(content).Single(item => item.IsVisible);
                var list = ListNamed(view, "Alertas da UBS ativa");
                var texts = FullTexts(view);
                Require(!texts.Contains("O que passou do limite") && !texts.Contains("Central de alertas") && !texts.Any(text => text.StartsWith("Avaliado às", StringComparison.Ordinal)) && !Descendants<Button>(view).Any(button => AutomationProperties.GetName(button).StartsWith("Atualizar", StringComparison.Ordinal)), "A tela não deveria ter título, texto explicativo nem Atualizar.");
                Require(!Descendants<Border>(view).Any(border => border.Style == (Style)Application.Current.FindResource("Card")), "A listagem não pode ficar dentro de um card.");
                Require(list.Items.Count == 5 && list.ActualHeight > 250 && ((ScrollViewer)window.FindName("PageScroll")).VerticalScrollBarVisibility == ScrollBarVisibility.Disabled, "A listagem deveria ocupar a tela inteira.");
                // Pesquisa em linha no cabeçalho (sem botão Filtros: a tela só tem pesquisa).
                var search = Descendants<TextBox>(window).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Pesquisar: Alertas da unidade");
                Require(!((Button)window.FindName("FiltersButton")).IsVisible, "A tela só tem pesquisa: sem botão Filtros.");
                CaptureBoth(window, folder, "alertas", 1280, 740);
                // Só leitura: selecionar não mostra balão; o ciclo relê a avaliação e, com o valor mudando, a seleção volta ao mesmo alerta pela chave.
                var queue = list.Items.Cast<AlertRow>().Single(row => row.Kind == "high-queue");
                SelectRows(window, list, 1280, 740, queue);
                Require(list.SelectedItems.Count == 1 && !((SelectionBalloon)view.FindName("NoBalloon")).IsVisible, "Alerta é só leitura: não deveria haver balão de ações.");
                CaptureBoth(window, folder, "alertas-selecionado", 1280, 740);
                fixture.Reset(unit.Id, alerts: true);
                foreach (var alert in SampleAlerts(unit)) fixture.AddAlert(unit.Id, alert.Kind == "high-queue" ? alert with { Value = 31 } : alert);
                RefreshAlertsNow(vm); PumpFor(TimeSpan.FromMilliseconds(100)); Layout(window, 1280, 740);
                Require(list.SelectedItems.Count == 1 && list.SelectedItem is AlertRow { Kind: "high-queue" } reread && reread.Value != queue.Value, "O ciclo de 20 s não deveria derrubar a seleção do alerta quando o valor dele muda.");
                // Enter abre a ficha em linhas (a da janela principal), uma vez.
                string? alertTitle = null; var alertTexts = Array.Empty<string>();
                var alertCloser = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                alertCloser.Tick += (_, _) =>
                {
                    if (Application.Current.Windows.OfType<ItemDetailsWindow>().FirstOrDefault() is not { } dialog) return;
                    alertTitle = dialog.Title; alertTexts = Descendants<TextBlock>(dialog).Select(text => text.Text).ToArray();
                    alertCloser.Stop(); dialog.Close();
                };
                alertCloser.Start();
                try { Require(PressKey(list, Key.Enter), "Enter deveria abrir a ficha do alerta."); PumpUntil(() => alertTitle is not null); }
                finally { alertCloser.Stop(); }
                Require(alertTitle == "Fila alta · Cardiologia" && new[] { "Tipo", "Especialidade", "Valor", "Limite", "Cardiologia" }.All(alertTexts.Contains) && !alertTexts.Contains("Desde"), "A ficha do alerta deveria trazer tipo, especialidade, valor e limite (sem 'desde' quando não existe): " + string.Join(" | ", alertTexts));
                var alertDetails = vm.BuildDetails(list.SelectedItem!) ?? throw new InvalidOperationException("Alerta sem ficha.");
                var alertPopup = ShowDetails(window, alertDetails);
                try { CapturePopupBoth(window, alertPopup, folder, "alertas-ficha"); }
                finally { alertPopup.Close(); }
                list.UnselectAll();
                search.Text = "Cardio"; PumpFor(TimeSpan.FromMilliseconds(80)); content = Layout(window, 1280, 740);
                Require(list.Items.Count == 1 && FullTexts(content).Contains("Fila alta · Cardiologia"), "A pesquisa em linha deveria filtrar a lista.");
                search.Text = "zzzz"; PumpFor(TimeSpan.FromMilliseconds(80)); content = Layout(window, 1280, 740);
                Require(list.Items.Count == 0 && FullTexts(content).Contains("Nenhum alerta encontrado para esta busca.") && !FullTexts(content).Any(text => text.StartsWith("Nenhum alerta. Nenhum critério", StringComparison.Ordinal)), "Busca sem resultado é diferente de 'nenhum alerta'.");
                CaptureBoth(window, folder, "alertas-busca-vazia", 1280, 740);
                search.Text = "";
                // Sem alerta: estado da lista com a hora da avaliação (a tela reavalia sozinha, sem botão).
                fixture.Reset(unit.Id, alerts: true); RefreshAlertsNow(vm); content = Layout(window, 1280, 740);
                Require(vm.UnitAlertsAllClear && FullTexts(content).Any(text => text.StartsWith("Nenhum alerta. Nenhum critério", StringComparison.Ordinal) && text.Contains(':')), "Sem alertas a lista deveria dizer 'Nenhum alerta' com a hora da avaliação.");
                CaptureBoth(window, folder, "alertas-nenhum", 1280, 740);
                // Falha de leitura é outro estado: nunca vira 'nenhum alerta'.
                fixture.FailAlerts = true; RefreshAlertsNow(vm); content = Layout(window, 1280, 740);
                Require(!vm.UnitAlertsAllClear && FullTexts(content).Contains(vm.UnitAlertsState) && vm.UnitAlertsState.Contains("não concluiu") && !FullTexts(content).Any(text => text.StartsWith("Nenhum alerta. Nenhum critério", StringComparison.Ordinal)), "A falha deveria aparecer como estado próprio da lista: " + vm.UnitAlertsState);
                CaptureBoth(window, folder, "alertas-falha", 1280, 740);
                // Lista mantida com a última avaliação falhando: o aviso fica acima dos itens.
                fixture.FailAlerts = false; fixture.AddAlert(unit.Id, SampleAlerts(unit)[0]); fixture.AddAlert(unit.Id, SampleAlerts(unit)[1]); RefreshAlertsNow(vm);
                fixture.FailAlerts = true; RefreshAlertsNow(vm); content = Layout(window, 1280, 740);
                Require(vm.UnitAlerts.Count == 2 && vm.HasUnitAlertsNotice && FullTexts(content).Contains(vm.UnitAlertsState), "Com a lista anterior mantida o aviso da falha deveria ficar acima dela.");
                CaptureBoth(window, folder, "alertas-lista-com-aviso", 1280, 740);
                // Janela mínima.
                fixture.FailAlerts = false; foreach (var alert in SampleAlerts(unit).Skip(2)) fixture.AddAlert(unit.Id, alert); RefreshAlertsNow(vm);
                CaptureBoth(window, folder, "alertas-janela-minima", 980, 640);
            }
            finally { CloseWindow(window); }
        });
    }

    // ---------------------------------------------------------------- Comunicados

    private static void RunRedesignAnnouncementChecks(string storageRoot, string folder)
    {
        Check(RedesignGroup + "Comunicados: só a listagem, balão Marcar como lido em lote, ficha com o texto, pesquisa em linha, sem título nem Atualizar, estados distintos", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Centro", "Centro");
            fixture.AddAnnouncement("Campanha de vacinação contra a gripe", "A campanha começa na segunda-feira.\nTodas as UBS devem separar uma sala.\n\nEquipes de plantão: confirmar a escala até sexta.", expiresAt: DateTimeOffset.UtcNow.AddDays(20));
            fixture.AddAnnouncement("Reunião das equipes", "Sexta, às 10h, no auditório da Secretaria.");
            fixture.AddAnnouncement("Atualização do sistema", "Linha 1 com <b>html</b> e &amp; texto", expiresAt: DateTimeOffset.UtcNow.AddDays(3));
            var window = OpenOperational(fixture, storageRoot, "redesign-announcements", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var nav = window.ViewModel.Navigation.Single(item => item.Id == "announcements");
                var content = Show(window, "announcements", 1280, 740);
                var view = Descendants<AnnouncementsView>(content).Single(item => item.IsVisible);
                var list = ListNamed(view, "Comunicados recebidos");
                var texts = FullTexts(view);
                Require(!texts.Contains("Comunicados") && !texts.Any(text => text.Contains("não lido", StringComparison.OrdinalIgnoreCase) && text.Contains("comunicados em vigor")) && !texts.Any(text => text.StartsWith("Atualizado às", StringComparison.Ordinal)) && !Descendants<Button>(view).Any(button => AutomationProperties.GetName(button).StartsWith("Atualizar", StringComparison.Ordinal)), "A tela não deveria ter título, resumo nem Atualizar: " + string.Join(" | ", texts.Where(text => text.Length < 60)));
                Require(!Descendants<Border>(view).Any(border => border.Style == (Style)Application.Current.FindResource("Card")), "A listagem não pode ficar dentro de um card.");
                Require(list.Items.Count == 3 && list.ActualHeight > 250 && ((ScrollViewer)window.FindName("PageScroll")).VerticalScrollBarVisibility == ScrollBarVisibility.Disabled, "A listagem deveria ocupar a tela inteira.");
                var rows = vm.Announcements.ToArray();
                Require(texts.Count(text => text == "Não lido") == 3 && texts.Any(text => text.StartsWith("Publicado em ", StringComparison.Ordinal) && text.Contains(" | Vale até ", StringComparison.Ordinal)) && texts.Any(text => text.StartsWith("Publicado em ", StringComparison.Ordinal) && !text.Contains(" | ", StringComparison.Ordinal)), "O item deveria trazer título, publicação | validade (só o que existe) e a etiqueta Não lido.");
                Require(!Descendants<Button>(view).Any(button => button.IsVisible && button.Content?.ToString() == "Marcar como lido") && !Descendants<SelectionBalloon>(view).Any(item => item.IsVisible), "Sem seleção não há balão nem ação.");
                Require(nav.Badge == 0 && vm.UnreadAnnouncementCount == 3, "O distintivo de não vistos do menu continua: abrir a aba o zera, o contador de não lidos fica.");
                CaptureBoth(window, folder, "comunicados", 1280, 740);
                // Um item selecionado: balão com "Marcar como lido".
                SelectRows(window, list, 1280, 740, rows[0]);
                var balloon = Descendants<SelectionBalloon>(view).Single();
                Require(balloon.IsVisible && balloon.Label == rows[0].Title && ButtonIn(view, "Marcar como lido").IsVisible, "Com um comunicado não lido selecionado o balão deveria oferecer Marcar como lido.");
                CaptureBoth(window, folder, "comunicados-selecionado", 1280, 740);
                // Vários: uma só ação marca todos.
                SelectRows(window, list, 1280, 740, rows[0], rows[1]);
                Require(balloon.Label == "2 comunicados selecionados", "O rótulo deveria contar os selecionados: " + balloon.Label);
                CaptureBoth(window, folder, "comunicados-varios-selecionados", 1280, 740);
                var posts = fixture.Count("POST", "/read");
                Click(window, content = Layout(window, 1280, 740), "Marcar como lido", 1280, 740);
                Require(fixture.Count("POST", "/read") == posts + 2 && vm.UnreadAnnouncementCount == 1 && rows[0].IsRead && rows[1].IsRead && vm.Message == "2 comunicados marcados como lidos.", $"Uma ação deveria marcar os dois ({fixture.Count("POST", "/read") - posts} chamadas; mensagem '{vm.Message}').");
                // A seleção e o balão continuam; sem não lido entre os selecionados o botão some.
                Require(list.SelectedItems.Count == 2 && balloon.IsVisible && !Descendants<Button>(view).Any(button => button.IsVisible && button.Content?.ToString() == "Marcar como lido"), "Depois de marcar, a seleção fica e o botão some (todos lidos).");
                SelectRows(window, list, 1280, 740, rows[1], rows[2]);
                Require(ButtonIn(view, "Marcar como lido").IsVisible, "Com um não lido no meio dos selecionados o botão volta.");
                // Ficha: texto completo, texto puro, quebras de linha preservadas.
                var details = vm.BuildDetails(rows[2]) ?? throw new InvalidOperationException("Comunicado sem ficha.");
                var popup = ShowDetails(window, details);
                try
                {
                    var bodyTexts = Descendants<TextBlock>(popup).Where(text => text.IsVisible).Select(text => text.Text).ToArray();
                    Require(bodyTexts.Contains("A campanha começa na segunda-feira.\nTodas as UBS devem separar uma sala.\n\nEquipes de plantão: confirmar a escala até sexta.") && bodyTexts.Contains("Comunicado da Secretaria") && bodyTexts.Contains("Campanha de vacinação contra a gripe"), "A ficha deveria mostrar o título e o texto completo com as quebras de linha: " + string.Join(" | ", bodyTexts));
                    CapturePopupBoth(window, popup, folder, "comunicados-ficha");
                }
                finally { popup.Close(); }
                // Texto longo: a ficha não passa da janela e o texto rola, com as quebras de linha e sem cortar.
                var longBody = string.Join("\n", Enumerable.Range(1, 60).Select(index => $"Parágrafo {index}: o texto do comunicado pode ter até duas mil letras e a ficha precisa mostrar tudo, rolando quando passar da altura da janela."));
                popup = ShowDetails(window, new ItemDetails("Comunicado com texto longo", "Comunicado da Secretaria", [new("Situação", "Não lido"), new("Texto", longBody, true, "Comunicado", Block: true)]));
                try
                {
                    var scroller = Descendants<ScrollViewer>(popup).First(viewer => viewer.IsVisible);
                    Require(popup.ActualHeight <= window.ActualHeight && scroller.ScrollableHeight > 0 && Descendants<TextBlock>(popup).Any(text => text.Text == longBody), $"O texto longo deveria rolar dentro da ficha (ficha {popup.ActualHeight:F0}, janela {window.ActualHeight:F0}, rolagem {scroller.ScrollableHeight:F0}).");
                    CapturePopupBoth(window, popup, folder, "comunicados-ficha-texto-longo");
                }
                finally { popup.Close(); }
                // Pesquisa em linha e o estado "nenhum encontrado para esta busca", diferente de "nenhum em vigor".
                var search = Descendants<TextBox>(window).Single(box => box.IsVisible && AutomationProperties.GetName(box) == "Pesquisar: Comunicados recebidos");
                search.Text = "reunião"; PumpFor(TimeSpan.FromMilliseconds(80)); content = Layout(window, 1280, 740);
                Require(list.Items.Count == 1 && ((AnnouncementRow)list.Items[0]).Title == "Reunião das equipes", "A pesquisa em linha deveria filtrar a lista.");
                search.Text = "zzzz"; PumpFor(TimeSpan.FromMilliseconds(80)); content = Layout(window, 1280, 740);
                Require(FullTexts(content).Contains("Nenhum comunicado encontrado para esta busca.") && !FullTexts(content).Contains("Nenhum comunicado em vigor para as UBS desta conta."), "Busca sem resultado é diferente de 'nenhum comunicado em vigor'.");
                CaptureBoth(window, folder, "comunicados-busca-vazia", 1280, 740);
                search.Text = "";
                // Falha de leitura com a lista na tela: aviso acima dela, sem erro na faixa.
                fixture.FailAnnouncementFeed = true; Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 740);
                Require(vm.AnnouncementsFootnote.Contains("não concluiu", StringComparison.Ordinal) && FullTexts(content).Contains(vm.AnnouncementsFootnote) && list.Items.Count == 3, "A falha deveria avisar acima da lista, mantendo os comunicados.");
                CaptureBoth(window, folder, "comunicados-lista-com-aviso", 1280, 740);
                fixture.FailAnnouncementFeed = false; Pump(vm.RefreshAutomaticallyAsync());
                // Janela mínima.
                SelectRows(window, list, 980, 640, rows[2]);
                CaptureBoth(window, folder, "comunicados-janela-minima", 980, 640);
            }
            finally { CloseWindow(window); }
        });
        Check(RedesignGroup + "Comunicados: lista vazia (nenhum em vigor) e leitura que falhou, como estados da lista", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Centro", "Centro");
            var window = OpenOperational(fixture, storageRoot, "redesign-announcements-empty", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "announcements", 1280, 740);
                Require(FullTexts(content).Contains("Nenhum comunicado em vigor para as UBS desta conta."), "A lista vazia deveria dizer que nenhum comunicado está em vigor.");
                CaptureBoth(window, folder, "comunicados-vazio", 1280, 740);
                fixture.FailAnnouncementFeed = true;
                vm.LogoutCommand.Execute(null); Settle(vm);
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid"; Pump(vm.LoginAsync("isolated-test-password"));
                content = Show(window, "announcements", 1280, 740);
                Require(vm.AnnouncementsState.StartsWith("Não foi possível ler os comunicados agora.", StringComparison.Ordinal) && FullTexts(content).Contains(vm.AnnouncementsState), "A primeira leitura que falhou deveria ser um estado da lista: " + vm.AnnouncementsState);
                CaptureBoth(window, folder, "comunicados-falha", 1280, 740);
            }
            finally { CloseWindow(window); }
        });
    }
}
