using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Agenda do OBS no padrão novo: cabeçalho, as quatro visões (Dia, Semana, Mês, Ano), o agendamento em pop-up, as ações em lote e a leitura do ano.</summary>
internal static partial class Program
{
    private static DateTime MondayOf(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>O clique inteiro numa consulta da lista ou da grade, pelos mesmos eventos que o Windows entrega (dois cliques abrem a ficha na hora).</summary>
    private static void ClickAppointment(ListBox list, AppointmentInfo item, int clicks = 1)
    {
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
        row.RaiseEvent(MouseArgs(System.Windows.Input.Mouse.PreviewMouseDownEvent, row, clicks));
        row.RaiseEvent(MouseArgs(System.Windows.Input.Mouse.MouseDownEvent, row, clicks));
        row.RaiseEvent(MouseArgs(System.Windows.Input.Mouse.PreviewMouseUpEvent, row, clicks));
        row.RaiseEvent(MouseArgs(System.Windows.Input.Mouse.MouseUpEvent, row, clicks));
    }

    private static Rect BoundsIn(FrameworkElement element, FrameworkElement ancestor) => element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static Color BrushColor(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;

    private static void RunAgendaViewChecks(Application application, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Agenda OBS: o cabeçalho tem pesquisa, o alternador Dia | Semana | Mês | Ano e + Agendar nova consulta à direita; sem título repetido e sem Atualizar", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Cabeçalho");
            var today = DateTime.Today;
            fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(today.AddHours(9)), "52998224725", "Ana Souza");
            fixture.AddAppointment(unit.Id, "called", new DateTimeOffset(today.AddHours(10)), "11144477735", "Bruno Lima");
            var window = OpenOperational(fixture, storageRoot, "agenda-header", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Layout(window, 1280, 740); Settle(vm);
                var content = Layout(window, 1280, 740);
                var search = Descendants<TextBox>(content).Single(box => box.IsVisible && AutomationProperties.GetName(box).StartsWith("Pesquisar", StringComparison.Ordinal));
                var toggle = Descendants<SegmentedToggle>(content).Single(item => item.IsVisible);
                var create = FindActionButton(content, "Agendar nova consulta");
                Require(toggle.Items.Cast<string>().SequenceEqual(["Dia", "Semana", "Mês", "Ano"]) && toggle.SelectedItem as string == "Mês", "O alternador deveria ter Dia, Semana, Mês e Ano, com Mês escolhido.");
                var searchX = search.TranslatePoint(new Point(0, 0), content).X; var toggleX = toggle.TranslatePoint(new Point(0, 0), content).X; var createX = create.TranslatePoint(new Point(0, 0), content).X;
                Require(searchX < toggleX && toggleX < createX && createX + create.ActualWidth > content.ActualWidth - 60, "O cabeçalho deveria trazer a pesquisa, o período e o botão de agendar, nessa ordem, com o botão à direita.");
                var texts = VisibleTexts(content);
                var screenTexts = VisibleTexts(Descendants<AgendaView>(content).Single());
                Require(!screenTexts.Contains("Agenda") && !texts.Any(text => text.StartsWith("Atualizar", StringComparison.Ordinal)), "A tela não deveria repetir o título (que fica só no cabeçalho) nem ter Atualizar.");
                Require(!Descendants<Border>(content).Any(border => border.IsVisible && border.Style == (Style)application.FindResource("Card")), "A Agenda não deveria ter card.");
                Require(create.ToolTip is string && AutomationProperties.GetName(create) == "Agendar nova consulta" && create.IsEnabled, "O botão de agendar precisa de nome acessível e estar habilitado com UBS ativa.");
                // A escolha do período vale na hora e fica nas preferências.
                toggle.SelectedItem = "Semana"; Settle(vm);
                Require(vm.IsAgendaWeek && window.ViewModel.GetAgendaPeriod() == "Semana", "Escolher Semana deveria mudar a visão e lembrar.");
                toggle.SelectedItem = "Mês"; Settle(vm);
                Require(vm.IsAgendaMonth && window.ViewModel.GetAgendaPeriod() == "Mês", "Voltar ao Mês deveria lembrar Mês.");
                toggle.SelectedItem = "Ano"; Settle(vm);
                Require(vm.IsAgendaYear && !search.IsVisible && window.ViewModel.GetAgendaPeriod() == "Ano", "No Ano a pesquisa some (não há lista) e a escolha é lembrada.");
                // Pesquisa filtra a lista do dia e distingue "sem consultas" de "nada encontrado".
                toggle.SelectedItem = "Dia"; Settle(vm); content = Layout(window, 1280, 740);
                var list = AgendaDayList(content); search = Descendants<TextBox>(content).Single(box => box.IsVisible && AutomationProperties.GetName(box).StartsWith("Pesquisar", StringComparison.Ordinal));
                Require(list.Items.Count == 2, "O dia deveria listar as duas consultas.");
                search.Text = "bruno"; content = Layout(window, 1280, 740);
                Require(list.Items.Count == 1 && ((AppointmentInfo)list.Items[0]).PatientName == "Bruno Lima", "A pesquisa deveria filtrar a lista do dia pelo nome.");
                search.Text = "zzz"; content = Layout(window, 1280, 740);
                Require(list.Items.Count == 0 && VisibleTexts(content).Contains("Nenhuma consulta encontrada para esta busca.") && !VisibleTexts(content).Contains("Nenhuma consulta neste dia."), "Sem resultado a tela deveria dizer que nada foi encontrado para a busca.");
                search.Text = ""; content = Layout(window, 1280, 740);
                Require(list.Items.Count == 2, "Limpar a pesquisa deveria devolver a lista.");
                vm.MoveAgenda(1); Settle(vm); content = Layout(window, 1280, 740);
                Require(VisibleTexts(content).Contains("Nenhuma consulta neste dia."), "Um dia sem consultas deveria dizer Nenhuma consulta neste dia.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-dia-vazio-light.png"));
            }
            finally { CloseWindow(window); }
            // Reabrir o programa: a visão lembrada volta.
            using var again = new ApiFixture(); again.AddUnit("UBS Cabeçalho");
            var reopened = OpenOperational(again, storageRoot, "agenda-header", Appearance.Light);
            try
            {
                var vm = LoginObs(reopened);
                reopened.ViewModel.Navigate("agenda"); Settle(vm); Layout(reopened, 1280, 740);
                Require(vm.AgendaViewMode == "Dia", $"Ao reabrir, a Agenda deveria voltar na visão que a pessoa usou (Dia): {vm.AgendaViewMode}, preferência {reopened.ViewModel.GetAgendaPeriod()}.");
            }
            finally { CloseWindow(reopened); }
        });

        Check("Agenda OBS: Dia lista as consultas do dia em ordem de horário, sem título de data, com nome, horário e a situação em etiqueta colorida", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Dia");
            var today = DateTime.Today;
            fixture.AddAppointment(unit.Id, "completed", new DateTimeOffset(today.AddHours(14)), "12345678909", "Carla Dias");
            fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(today.AddHours(8).AddMinutes(20)), "52998224725", "Ana Souza");
            fixture.AddAppointment(unit.Id, "missed", new DateTimeOffset(today.AddHours(11)), "11144477735", "Bruno Lima");
            fixture.AddAppointment(unit.Id, "cancelled", new DateTimeOffset(today.AddHours(16)), "39053344705", "Dora Nunes");
            fixture.AddAppointment(unit.Id, "called", new DateTimeOffset(today.AddHours(9)), "52998224725", "Eva Lopes");
            var window = OpenOperational(fixture, storageRoot, "agenda-day", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Layout(window, 1280, 740); Settle(vm);
                vm.AgendaViewMode = "Dia"; Settle(vm);
                var content = Layout(window, 1280, 740);
                var list = AgendaDayList(content);
                Require(vm.Appointments.Select(item => item.PatientName).SequenceEqual(["Ana Souza", "Eva Lopes", "Bruno Lima", "Carla Dias", "Dora Nunes"]), "A lista do dia deveria vir em ordem de horário.");
                var texts = VisibleTexts(list);
                Require(texts.Contains("08:20 às 08:50") && texts.Contains("14:00 às 14:30"), "O horário deveria aparecer como '08:20 às 08:50': " + string.Join(" | ", texts));
                foreach (var status in new[] { "Agendada", "Chamada", "Concluída", "Falta", "Cancelada" }) Require(texts.Contains(status), "Falta a etiqueta " + status);
                var tags = Descendants<StatusTag>(list).Where(tag => tag.IsVisible).ToDictionary(tag => tag.Text, tag => tag.Tone);
                Require(tags["Agendada"] == Tone.Blue && tags["Chamada"] == Tone.Violet && tags["Concluída"] == Tone.Green && tags["Falta"] == Tone.Pink && tags["Cancelada"] == Tone.Yellow, "Cada situação deveria ter o seu tom.");
                var header = VisibleTexts(content);
                Require(!header.Any(text => text.Contains(today.ToString("MMMM"), StringComparison.Ordinal) && text.Contains(today.Day.ToString(), StringComparison.Ordinal) && text != vm.AgendaRangeLabel), "A lista não deveria repetir o título da data.");
                Require(Descendants<Border>(list).All(border => border.Style != (Style)application.FindResource("Card")), "Sem card atrás da lista.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-dia-light.png"));
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 740);
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-dia-dark.png"));
                // Janela mínima: nada cortado.
                content = Layout(window, 980, 640);
                Require(Descendants<StatusTag>(AgendaDayList(content)).Where(tag => tag.IsVisible).All(tag => tag.TranslatePoint(new Point(tag.ActualWidth, 0), content).X <= content.ActualWidth), "As etiquetas não podem sair da janela mínima.");
                var legend = Descendants<Viewbox>(content).Single(panel => AutomationProperties.GetName(panel) == "Legenda das situações da agenda");
                Require(legend.ActualHeight < 22 && BoundsIn(legend, content).Right <= content.ActualWidth, $"A legenda deveria ficar pequena, numa linha só, mesmo na janela mínima ({legend.ActualHeight:F0} px de altura).");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-dia-minima-dark.png"));
            }
            finally { CloseWindow(window); }
        });

        Check("Agenda OBS: Semana é uma grade com os sete dias e as horas, blocos no dia e horário coloridos pela situação, simultâneas lado a lado, duplo clique abre a ficha", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Semana");
            var monday = MondayOf(DateTime.Today);
            DateTimeOffset At(int dayOffset, int hour, int minute = 0) => new(monday.AddDays(dayOffset).AddHours(hour).AddMinutes(minute));
            var ana = fixture.AddAppointment(unit.Id, "scheduled", At(0, 9), "52998224725", "Ana Souza");
            var bia = fixture.AddAppointment(unit.Id, "called", At(0, 9, 15), "11144477735", "Bia Lima");
            var cadu = fixture.AddAppointment(unit.Id, "scheduled", At(0, 9, 20), "12345678909", "Cadu Reis");
            var caio = fixture.AddAppointment(unit.Id, "completed", At(2, 14), "39053344705", "Caio Dias");
            var dani = fixture.AddAppointment(unit.Id, "missed", At(4, 8), "52998224725", "Dani Melo");
            var edu = fixture.AddAppointment(unit.Id, "cancelled", At(6, 17, 30), "11144477735", "Edu Pires");
            var window = OpenOperational(fixture, storageRoot, "agenda-week", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Layout(window, 1280, 740); Settle(vm);
                vm.AgendaViewMode = "Semana"; Settle(vm);
                var content = Layout(window, 1280, 740);
                Require(vm.AgendaWeekFirstHour == 8 && vm.AgendaWeekLastHour == 18 && vm.Appointments.Count == 6, $"A grade deveria cobrir só das 8h às 18h (as horas com consulta): {vm.AgendaWeekFirstHour}-{vm.AgendaWeekLastHour}, {vm.Appointments.Count} consultas, semana de {vm.AgendaWeekStart:dd/MM}.");
                var texts = VisibleTexts(content);
                foreach (var weekday in new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" }) Require(texts.Count(text => text == weekday) == 1, "O cabeçalho da semana deveria ter " + weekday);
                Require(vm.AgendaRangeLabel.StartsWith("Semana de ", StringComparison.Ordinal) && texts.Contains(vm.AgendaRangeLabel), "O rótulo do período deveria ficar no topo.");
                var list = AgendaWeekList(content); var panel = Descendants<AgendaWeekPanel>(list).Single();
                Rect Block(AppointmentInfo item) => BoundsIn((ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(vm.Appointments.Single(found => found.Id == item.Id)), panel);
                var column = (panel.ActualWidth - panel.GutterWidth) / 7;
                var (a, b, c) = (Block(ana), Block(bia), Block(cadu));
                Require(Math.Abs(a.Top - (panel.HourHeight + 1)) < 1.5 && Math.Abs(b.Top - (1.25 * panel.HourHeight + 1)) < 1.5, "O bloco deveria começar na altura do horário (09:00 e 09:15).");
                Require(a.X < b.X && b.X < c.X && Math.Abs(a.Width - b.Width) < 1 && Math.Abs(b.Width - c.Width) < 1 && a.Right <= b.X + 4 && c.Right <= panel.GutterWidth + column + 1, $"As três consultas simultâneas deveriam dividir a coluna de segunda ({a} {b} {c}).");
                var (d, e, f, g) = (Block(caio), Block(dani), Block(edu), Block(ana));
                Require(Math.Abs(d.Width - (column - 4)) < 1 && d.X > g.X + 1.5 * column && e.X > d.X && f.X > e.X && Math.Abs(f.X - (panel.GutterWidth + 6 * column + 2)) < 1.5, "Cada consulta deveria ficar na coluna do seu dia, inteira quando não há simultânea.");
                Require(Math.Abs(d.Height - (0.5 * panel.HourHeight - 2)) < 2 && Math.Abs(f.Top - (9.5 * panel.HourHeight + 1)) < 1.5, "A altura do bloco deveria acompanhar a duração e a posição, o horário.");
                Color Frame(AppointmentInfo item) { var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(vm.Appointments.Single(found => found.Id == item.Id)); return BrushColor(((Border)container.Template.FindName("Frame", container)).Background); }
                Color Theme(string key) => ((SolidColorBrush)application.FindResource(key)).Color;
                Require(Frame(ana) == Theme("ToneBlueSurfaceBrush") && Frame(bia) == Theme("ToneVioletSurfaceBrush") && Frame(caio) == Theme("ToneGreenSurfaceBrush") && Frame(dani) == Theme("TonePinkSurfaceBrush") && Frame(edu) == Theme("ToneYellowSurfaceBrush"), "Cada bloco deveria ter a cor da situação.");
                Require(texts.Contains("Ana Souza") && texts.Contains("14:00 – 14:30") && texts.Contains("Dani Melo") && texts.Contains("08:00 – 08:30"), "O bloco deveria trazer o nome e o horário (nas faixas estreitas das simultâneas só o nome, o resto está na dica): " + string.Join(" | ", texts));
                Require(Descendants<Border>(content).All(border => !border.IsVisible || border.Style != (Style)application.FindResource("Card")), "Sem card na grade.");
                // Clicar seleciona (e mostra o balão com as ações da situação); duplo clique abre a ficha.
                ClickAppointment(list, vm.Appointments.Single(item => item.Id == ana.Id)); window.UpdateLayout(); content = Layout(window, 1280, 740);
                Require(vm.SelectedAppointment?.Id == ana.Id && VisibleBalloon(content) is { } balloon && VisibleActions(balloon).SequenceEqual(["Chamar", "Registrar falta", "Cancelar consulta"]), "Clicar numa consulta deveria selecioná-la e mostrar o balão da situação.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-semana-light.png"));
                InspectModal<ItemDetailsWindow>(window, dialog => Require(FullTexts(dialog).Contains("Caio Dias") && FullTexts(dialog).Contains("Registro"), "O duplo clique deveria abrir a ficha da consulta."), () =>
                    {
                        // O primeiro clique seleciona; o segundo (ClickCount 2) abre a ficha na hora. A ficha abre depois do evento, então a fila de mensagens roda até ela abrir e fechar.
                        var target = vm.Appointments.Single(item => item.Id == caio.Id);
                        ClickAppointment(list, target); window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        ClickAppointment(list, target, 2); PumpFrames(60);
                    });
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 740);
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-semana-dark.png"));
                // A pesquisa do cabeçalho também filtra os blocos da grade, e apagar o texto traz todos de volta.
                var weekSearch = Descendants<TextBox>(content).Single(box => box.IsVisible && AutomationProperties.GetName(box).StartsWith("Pesquisar", StringComparison.Ordinal));
                weekSearch.Text = "Dani"; content = Layout(window, 1280, 740);
                Require(AgendaWeekList(content).Items.Count == 1 && VisibleTexts(content).Contains("Dani Melo") && !VisibleTexts(content).Contains("Caio Dias"), "A pesquisa deveria deixar na grade só a consulta encontrada.");
                weekSearch.Text = ""; content = Layout(window, 1280, 740);
                Require(AgendaWeekList(content).Items.Count == 6, "Apagar a pesquisa deveria trazer de volta todas as consultas da semana.");
                // Faixa de horas: o horário de atendimento da UBS também conta, e a grade nunca fica com menos de 6 horas.
                content = Layout(window, 980, 640);
                Require(Descendants<AgendaWeekPanel>(AgendaWeekList(content)).Single().ActualHeight == (vm.AgendaWeekLastHour - vm.AgendaWeekFirstHour) * 64, "A altura da grade deveria ser horas × altura da hora (a rolagem é da lista).");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-semana-minima-dark.png"));
            }
            finally { CloseWindow(window); }
        });

        Check("Agenda OBS: Mês marca o dia escolhido (fundo azul bem claro e círculo azul bebê no número), hoje com contorno, fora do mês esmaecido, e o clique mostra a lista do dia abaixo", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Mês");
            var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(first.AddDays(11).AddHours(9)), "52998224725", "Ana Souza");
            fixture.AddAppointment(unit.Id, "completed", new DateTimeOffset(first.AddDays(11).AddHours(10)), "11144477735", "Bruno Lima");
            fixture.AddAppointment(unit.Id, "missed", new DateTimeOffset(first.AddDays(15).AddHours(10)), "12345678909", "Carla Dias");
            var window = OpenOperational(fixture, storageRoot, "agenda-month-style", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Layout(window, 1280, 740); Settle(vm);
                var content = Layout(window, 1280, 740);
                Button DayButton(DateTime date) => Descendants<Button>(content).Single(button => button.DataContext is AgendaCalendarDay day && day.Date == date && button.IsVisible);
                Border Cell(Button button) => (Border)button.Template.FindName("Cell", button);
                System.Windows.Shapes.Ellipse Disc(Button button) => (System.Windows.Shapes.Ellipse)button.Template.FindName("Disc", button);
                Color Theme(string key) => ((SolidColorBrush)application.FindResource(key)).Color;
                var chosen = first.AddDays(11);
                InvokeButton(DayButton(chosen)); content = Layout(window, 1280, 740);
                var selected = DayButton(chosen);
                Require(vm.SelectedAgendaDateLabel.Contains(chosen.Day.ToString(), StringComparison.Ordinal) && vm.AgendaDays.Single(day => day.Date == chosen).IsSelected, "O dia clicado deveria ficar escolhido.");
                Require(BrushColor(Cell(selected).Background) == Theme("ToneBlueSurfaceBrush") && BrushColor(Disc(selected).Fill) == Theme("ButtonHoverBrush"), "O dia escolhido deveria ter o fundo azul bem claro na célula e o círculo azul bebê no número.");
                Require(Contrast(Theme("TextBrush"), Theme("ButtonHoverBrush")) >= 4.5 && Contrast(Theme("TextBrush"), Theme("ToneBlueSurfaceBrush")) >= 4.5, "O número precisa continuar legível sobre o círculo.");
                var plain = DayButton(first.AddDays(12));
                Require(BrushColor(Cell(plain).Background) == Colors.Transparent && BrushColor(Disc(plain).Fill) == Colors.Transparent, "Um dia comum não tem fundo nem círculo.");
                var todayButton = DayButton(DateTime.Today);
                Require(BrushColor(Cell(todayButton).BorderBrush) == Theme("BrandBrush") && Cell(todayButton).BorderThickness.Left >= 1.5, "Hoje deveria ter contorno.");
                var outside = Descendants<Button>(content).Where(button => button.DataContext is AgendaCalendarDay { IsOutsideMonth: true } && button.IsVisible).ToArray();
                Require(outside.Length > 0 && outside.All(button => button.Opacity < 0.6), "Os dias de fora do mês deveriam ficar esmaecidos.");
                // Hover em azul bebê.
                if (MouseOverKey is { } key)
                {
                    plain.SetValue(key, true); window.UpdateLayout();
                    Require(BrushColor(Cell(plain).Background) == Theme("HoverBrush"), "O hover do dia deveria ser azul bebê.");
                    plain.SetValue(key, false);
                }
                // A lista do dia aparece abaixo do calendário, sem título de data, e o dia sem consulta explica.
                var list = AgendaDayList(content);
                var lastDay = DayButton(first.AddMonths(1).AddDays(-1));
                Require(vm.IsAgendaMonth && list.Items.Count == 2 && BoundsIn(list, content).Top >= BoundsIn(lastDay, content).Bottom - 1, "A lista do dia deveria ficar abaixo do calendário com as duas consultas do dia 12.");
                InvokeButton(DayButton(first.AddDays(13))); content = Layout(window, 1280, 740);
                Require(VisibleTexts(content).Contains("Nenhuma consulta neste dia.") && AgendaDayList(content).Items.Count == 0, "Um dia sem consulta deveria dizer isso.");
                var dots = Descendants<System.Windows.Shapes.Ellipse>(DayButton(chosen)).Count(ellipse => ellipse.IsVisible && ellipse.Width == 7);
                Require(dots == 2, "As bolinhas de situação do dia 12 (agendada e concluída) deveriam continuar: " + dots);
                InvokeButton(DayButton(chosen)); content = Layout(window, 1280, 740);
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-mes-light.png"));
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 740);
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-mes-dark.png"));
                Require(BrushColor(Cell(DayButton(chosen)).Background) == Theme("ToneBlueSurfaceBrush") && Contrast(Theme("TextBrush"), Theme("ButtonHoverBrush")) >= 4.5, "No tema escuro o dia escolhido também precisa de fundo e círculo legíveis.");
                content = Layout(window, 980, 640);
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-mes-minima-dark.png"));
            }
            finally { CloseWindow(window); }
        });

        Check("Agenda OBS: Ano mostra os 12 meses em 4×3 com mini-calendários, lê o ano em pedidos de até 366 dias sem repetir leituras e abre o mês ou o dia clicado", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Ano");
            var year = DateTime.Today.Year;
            DateTimeOffset At(int month, int day, int hour) => new(new DateTime(year, month, day, hour, 0, 0));
            fixture.AddAppointment(unit.Id, "scheduled", At(1, 15, 9), "52998224725", "Ana Janeiro");
            fixture.AddAppointment(unit.Id, "completed", At(3, 3, 9), "11144477735", "Bruno Março");
            fixture.AddAppointment(unit.Id, "completed", At(3, 3, 10), "12345678909", "Carla Março");
            for (var hour = 8; hour < 15; hour++) fixture.AddAppointment(unit.Id, "completed", At(6, 20, hour), "39053344705", "Cheio " + hour);
            fixture.AddAppointment(unit.Id, "scheduled", At(12, 24, 9), "52998224725", "Dora Dezembro");
            fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(new DateTime(year + 1, 2, 10, 9, 0, 0)), "52998224725", "Edu Ano Seguinte");
            var window = OpenOperational(fixture, storageRoot, "agenda-year", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Layout(window, 1280, 740); Settle(vm);
                var ranges = new List<TimeSpan>();
                var before = fixture.Count("GET", "/appointments");
                vm.AgendaViewMode = "Ano"; Settle(vm);
                var content = Layout(window, 1280, 740);
                Require(fixture.Count("GET", "/appointments") == before + 2 && fixture.LastAppointmentRange.To - fixture.LastAppointmentRange.From <= TimeSpan.FromDays(366), "Ler o ano deveria fazer dois pedidos de 6 meses (nenhum acima de 366 dias).");
                Require(vm.AgendaMonths.Count == 12 && vm.AgendaMonths[2].AppointmentCount == 2 && vm.AgendaMonths[0].AppointmentCount == 1 && vm.AgendaMonths[11].AppointmentCount == 1 && vm.AgendaMonths[5].AppointmentCount == 7 && vm.AgendaMonths[1].AppointmentCount == 0, "Os meses deveriam marcar a quantidade de consultas.");
                Require(vm.AgendaMonths.All(month => month.Days.Count == 42) && vm.AgendaMonths[0].Name == "Janeiro" && vm.AgendaRangeLabel == year.ToString(), $"Cada mês traz o nome e o mini-calendário ({vm.AgendaMonths[0].Name}, {vm.AgendaRangeLabel}).");
                var january15 = vm.AgendaMonths[0].Days.Single(day => day.Date == new DateTime(year, 1, 15) && day.IsCurrentMonth);
                var june20 = vm.AgendaMonths[5].Days.Single(day => day.Date == new DateTime(year, 6, 20) && day.IsCurrentMonth);
                Require(january15.Intensity == 1 && vm.AgendaMonths[2].Days.Single(day => day.Date == new DateTime(year, 3, 3) && day.IsCurrentMonth).Intensity == 1 && june20.Intensity == 3 && vm.AgendaMonths[1].Days.Where(day => day.IsCurrentMonth).All(day => day.Intensity == 0), "Os dias com consulta deveriam ficar marcados pela quantidade.");
                var months = Descendants<Button>(content).Where(button => button.IsVisible && button.DataContext is AgendaYearMonth).Select(button => (Month: (AgendaYearMonth)button.DataContext, Position: button.TranslatePoint(new Point(0, 0), content))).ToArray();
                Require(months.Length == 12 && months.Select(item => Math.Round(item.Position.X)).Distinct().Count() == 4 && months.Select(item => Math.Round(item.Position.Y)).Distinct().Count() == 3, "Os 12 meses deveriam formar uma grade de 4 colunas por 3 linhas.");
                Require(!Descendants<Viewbox>(content).Single(panel => AutomationProperties.GetName(panel) == "Legenda das situações da agenda").IsVisible, "A legenda de situações não vale no Ano.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-ano-light.png"));
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 740);
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-ano-dark.png"));
                window.ViewModel.SetAppearance(Appearance.Light); content = Layout(window, 1280, 740);
                // O ciclo seguinte só relê o mês escolhido (o de hoje), não o ano.
                var cycle = fixture.Count("GET", "/appointments");
                Pump(vm.RefreshAutomaticallyAsync());
                Require(fixture.Count("GET", "/appointments") == cycle + 1 && fixture.LastAppointmentRange.To - fixture.LastAppointmentRange.From <= TimeSpan.FromDays(31), "O ciclo do Ano deveria reler só o mês de hoje, sem repetir o ano.");
                // Ano seguinte: lido uma vez; voltar ao ano atual não lê de novo (já está guardado).
                vm.MoveAgenda(1); Settle(vm);
                Require(fixture.Count("GET", "/appointments") == cycle + 3 && vm.AgendaRangeLabel == (year + 1).ToString() && vm.AgendaMonths[1].AppointmentCount == 1, "O ano seguinte deveria ser lido em dois pedidos e marcar a consulta de fevereiro.");
                vm.MoveAgenda(-1); Settle(vm);
                Require(fixture.Count("GET", "/appointments") == cycle + 3 && vm.AgendaMonths[2].AppointmentCount == 2, "Voltar ao ano já lido não deveria fazer nova leitura e deveria mostrar os dados guardados.");
                // Mês clicado abre a visão Mês nele; dia clicado abre a visão Dia nele.
                content = Layout(window, 1280, 740);
                var march = Descendants<Button>(content).Single(button => button.IsVisible && button.DataContext is AgendaYearMonth { Name: "Março" });
                InvokeButton(march); Settle(vm);
                Require(vm.IsAgendaMonth && vm.AgendaDays.First(day => day.IsCurrentMonth).Date == new DateTime(year, 3, 1), "Clicar no mês deveria abrir a visão Mês nele.");
                vm.AgendaViewMode = "Ano"; Settle(vm); content = Layout(window, 1280, 740);
                var day15 = Descendants<Button>(content).Single(button => button.IsVisible && button.DataContext is AgendaCalendarDay day && day.Date == new DateTime(year, 1, 15) && day.IsCurrentMonth);
                InvokeButton(day15); Settle(vm); content = Layout(window, 1280, 740);
                Require(vm.IsAgendaDay && vm.Appointments.Single().PatientName == "Ana Janeiro" && VisibleTexts(content).Contains("Ana Janeiro"), "Clicar no dia deveria abrir a visão Dia nele.");
            }
            finally { CloseWindow(window); }
        });

        Check("Agenda OBS: agendar abre um pop-up; o erro (horário ocupado, texto livre inválido) aparece dentro dele e o pop-up só fecha quando a consulta é marcada", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Pop-up", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana", [cardio.Id]); var room = fixture.AddRoom(unit.Id, "Consultório 1");
            fixture.AddRegistry(unit.Id, "Paciente Um", "52998224725");
            var day = DateTime.Today.AddDays(3);
            fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 9)); fixture.AddAvailableSlot(unit.Id, ana, cardio, room, LocalAt(day, 10));
            var window = OpenOperational(fixture, storageRoot, "agenda-popup", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Layout(window, 1280, 740); Settle(vm);
                var content = Layout(window, 1280, 740);
                var button = FindActionButton(content, "Agendar nova consulta");
                var errorsShown = new List<string>();
                InspectModal<FormDialog>(window, dialog =>
                {
                    var primary = Descendants<Button>(dialog).Single(item => item.Content as string == "Agendar");
                    Require(dialog.Title == "Agendar nova consulta" && primary.IsDefault, "O pop-up deveria ter o título e o botão Agendar como primário.");
                    // Sem paciente: o erro aparece dentro do pop-up e ele continua aberto.
                    InvokeButton(primary); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && dialog.ErrorMessage.Contains("Selecione o paciente", StringComparison.Ordinal), "Sem paciente o erro deveria aparecer no pop-up: " + dialog.ErrorMessage);
                    errorsShown.Add(dialog.ErrorMessage);
                    vm.AppointmentPatient = vm.ObsRegistry.Single(); vm.BookingSpecialty = vm.BookingSpecialties.Single(); Settle(vm);
                    vm.BookingSlot = vm.BookingSlots[0]; fixture.NextBookingConflict = "slot-unavailable";
                    InvokeButton(primary); PumpUntil(() => !dialog.IsBusy); Settle(vm);
                    Require(dialog.IsVisible && dialog.ErrorMessage.Contains("não está mais livre", StringComparison.Ordinal) && fixture.AppointmentsOf(unit.Id).Count == 0, "Horário ocupado: a mensagem própria deveria aparecer no pop-up e nada ser marcado: " + dialog.ErrorMessage);
                    errorsShown.Add(dialog.ErrorMessage);
                    vm.BookingSlot = vm.BookingSlots[0];
                    InvokeButton(primary); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && fixture.AppointmentsOf(unit.Id).Count == 1 && vm.MessageKind == MessageKind.Success, "Com o horário livre o pop-up deveria agendar e fechar: " + vm.Message);
                }, () => InvokeButton(button));
                Require(errorsShown.Count == 2 && vm.MessageKind == MessageKind.Success, "A mensagem de sucesso fica na tela depois de fechar.");
                // Fechar sem agendar não deixa o erro como aviso da tela.
                InspectModal<FormDialog>(window, dialog =>
                {
                    InvokeButton(Descendants<Button>(dialog).Single(item => item.Content as string == "Agendar")); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.ErrorMessage.Length > 0, "O erro deveria aparecer.");
                }, () => InvokeButton(button));
                Require(vm.Message.Length == 0, "Fechar o pop-up sem agendar não deveria deixar o erro na tela: " + vm.Message);
                InspectBooking(window, dialog => Require(Descendants<Button>(dialog).Any(item => item.Content as string == "Agendar"), "O pop-up reabre."));
            }
            finally { CloseWindow(window); }
        });

        Check("Agenda OBS: ação em lote muda várias consultas de uma vez, com uma só confirmação para cancelar (e nada acontece se a pessoa desistir)", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Lote");
            var today = DateTime.Today;
            var first = fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(today.AddHours(8)), "52998224725", "Ana Souza");
            var second = fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(today.AddHours(9)), "11144477735", "Bruno Lima");
            var third = fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(today.AddHours(10)), "12345678909", "Carla Dias");
            var window = OpenOperational(fixture, storageRoot, "agenda-batch", Appearance.Light);
            var asked = new List<(string Title, string Text)>(); var answer = false;
            Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return answer; };
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Layout(window, 1280, 740); Settle(vm);
                var content = Layout(window, 1280, 740); var list = AgendaDayList(content);
                SelectAppointments(window, list, [.. vm.Appointments]); content = Layout(window, 1280, 740);
                var balloon = VisibleBalloon(content);
                Require(balloon.Label == "3 consultas selecionadas" && vm.SelectedAppointmentCount == 3 && VisibleActions(balloon).SequenceEqual(["Chamar", "Registrar falta", "Cancelar consulta"]), "Com três consultas agendadas o balão deveria dizer quantas e oferecer as ações que valem para todas: " + balloon.Label);
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-lote-light.png"));
                // Cancelar pergunta uma vez; desistir não muda nada.
                InvokeButton(((Panel)balloon.Content).Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancelar consulta")); Settle(vm);
                Require(asked.Count == 1 && asked[0].Text.Contains("3 consultas", StringComparison.Ordinal) && fixture.Count("PATCH", "/status") == 0 && vm.Appointments.All(item => item.Status == "scheduled"), "Desistir da confirmação não deveria cancelar nada: " + string.Join(" | ", asked));
                // Chamar as três: sem confirmação, uma chamada por consulta, uma mensagem só, e a seleção continua (agora com Concluir).
                InvokeButton(((Panel)balloon.Content).Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Chamar")); Settle(vm); content = Layout(window, 1280, 740);
                Require(asked.Count == 1 && fixture.Count("PATCH", "/status") == 3 && vm.Appointments.All(item => item.Status == "called") && vm.Message == "Situação de 3 consultas atualizada." && vm.MessageKind == MessageKind.Success, "Chamar as três deveria mudar as três com uma mensagem só: " + vm.Message);
                balloon = VisibleBalloon(content);
                Require(vm.SelectedAppointmentCount == 3 && balloon.Label == "3 consultas selecionadas" && VisibleActions(balloon).SequenceEqual(["Concluir", "Registrar falta", "Cancelar consulta"]), "A seleção deveria continuar, agora com Concluir: " + string.Join("/", VisibleActions(balloon)));
                // Cancelar com confirmação: uma pergunta, três cancelamentos.
                answer = true;
                InvokeButton(((Panel)balloon.Content).Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancelar consulta")); Settle(vm);
                Require(asked.Count == 2 && fixture.Count("PATCH", "/status") == 6 && vm.Appointments.All(item => item.Status == "cancelled"), "Confirmado, as três deveriam ser canceladas com uma pergunta só.");
                // Uma consulta só: a mensagem e a confirmação continuam as de sempre.
                answer = false; asked.Clear();
                fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(today.AddHours(11)), "39053344705", "Dora Nunes");
                Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 740); list = AgendaDayList(content);
                SelectAppointments(window, list, vm.Appointments.Single(item => item.PatientName == "Dora Nunes")); content = Layout(window, 1280, 740);
                balloon = VisibleBalloon(content);
                InvokeButton(((Panel)balloon.Content).Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancelar consulta")); Settle(vm);
                Require(asked.Count == 1 && asked[0].Title == "Cancelar consulta" && asked[0].Text == "A consulta será cancelada e o horário pode ser oferecido como vaga relâmpago.", "A confirmação de uma consulta só deveria ser a de sempre: " + string.Join(" | ", asked));
                answer = true;
                InvokeButton(((Panel)balloon.Content).Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Chamar")); Settle(vm);
                Require(vm.SelectedAppointment?.Status == "called" && vm.Message == "Situação da consulta atualizada.", "Uma consulta só: a mensagem de sempre: " + vm.Message);
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Agenda OBS: o ciclo e as ações releem sozinhos, sem botão; carregando mostra o passo e o indicador na cor da marca; sem cor fixa nem restos do Atualizar nos fontes", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Ciclo");
            var today = DateTime.Today;
            fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(today.AddHours(8)), "52998224725", "Ana Souza");
            var window = OpenOperational(fixture, storageRoot, "agenda-cycle", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                window.ViewModel.Navigate("agenda"); Layout(window, 1280, 740); Settle(vm);
                var content = Layout(window, 1280, 740);
                Require(vm.Appointments.Count == 1, "A agenda carrega ao entrar.");
                // Outro computador marca uma consulta: o ciclo de 20 s a traz sem ninguém clicar.
                fixture.AddAppointment(unit.Id, "scheduled", new DateTimeOffset(today.AddHours(9)), "11144477735", "Bruno Lima");
                Pump(vm.RefreshAutomaticallyAsync());
                Require(vm.Appointments.Count == 2, "O ciclo deveria trazer a consulta nova.");
                // Enquanto a agenda não foi lida (troca de UBS, por exemplo) a tela mostra o passo escrito e o indicador na cor da marca. O transporte do teste responde na hora,
                // então o estado "ainda não lida" é montado esvaziando o que foi lido (o que a troca de UBS faz).
                typeof(WorkspaceViewModel).GetMethod("ClearAppointmentData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(vm, null);
                Require(vm.AgendaIsLoading && vm.AgendaStateText == "Carregando a agenda do mês…" && vm.Appointments.Count == 0, "Sem leitura a tela deveria dizer que está lendo: " + vm.AgendaStateText);
                content = Layout(window, 1280, 740);
                var progress = Descendants<ProgressBar>(content).Single(bar => bar.IsVisible && AutomationProperties.GetName(bar) == "Lendo a agenda");
                Require(progress.IsIndeterminate && BrushColor(progress.Foreground) == ((SolidColorBrush)application.FindResource("BrandBrush")).Color && VisibleTexts(content).Contains("Carregando a agenda do mês…"), "O indicador deveria ser da cor da marca, com o texto do passo.");
                SavePreview(content, Path.Combine(previewRoot, "obs-agenda-carregando-light.png"));
                Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 740);
                Require(!vm.AgendaIsLoading && vm.Appointments.Count == 2 && Descendants<ProgressBar>(content).All(bar => !bar.IsVisible || AutomationProperties.GetName(bar) != "Lendo a agenda"), "Lida a agenda, o indicador some e as consultas voltam.");
                vm.MoveAgenda(1); Settle(vm); vm.GoToAgendaToday(); Settle(vm);
                Require(vm.Appointments.Count == 2, "Hoje volta ao dia de hoje com as consultas.");
                // Nenhum estilo, comando ou método de "atualizar" sem uso.
                var root = AppContext.BaseDirectory;
                while (root is not null && !File.Exists(Path.Combine(root, "NORA.slnx"))) root = Path.GetDirectoryName(root);
                Require(root is not null, "Raiz do repositório não encontrada.");
                foreach (var file in new[] { "AgendaView.xaml", "AgendaBookingForm.xaml" })
                {
                    var source = File.ReadAllText(Path.Combine(root!, "Nora.Desktop", "Views", file));
                    Require(!Regex.IsMatch(source, "#[0-9A-Fa-f]{6,8}\\b") && !source.Contains("IconRefreshButton", StringComparison.Ordinal) && !source.Contains("Atualizar", StringComparison.Ordinal), $"{file} não deveria ter cor fixa nem restos de Atualizar.");
                }
                Require(!File.ReadAllText(Path.Combine(root!, "Nora.Desktop", "ViewModels", "WorkspaceViewModel.Appointments.cs")).Contains("LoadAppointmentsAsync", StringComparison.Ordinal), "O método que só o botão Atualizar usava não deveria existir.");
            }
            finally { CloseWindow(window); }
        });
    }
}
