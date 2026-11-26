using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Pop-up de cadastro (ShowForm), ficha em linhas, alternador de segmentos, painel de gráfico (Linha | Pizza), gráfico de linhas e rosca,
/// agrupamento do eixo, pop-up do indicador e alternador cards / gráficos com a escolha lembrada por tela.
/// </summary>
internal static partial class Program
{
    private sealed class ToggleState : INotifyPropertyChanged
    {
        private string? value;
        public string? Value { get => value; set { this.value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class ModeState : INotifyPropertyChanged
    {
        private ViewMode mode;
        public ViewMode Mode { get => mode; set { mode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>Uma janela dona fora da tela, para os pop-ups modais dos testes.</summary>
    private static Window OffScreenOwner()
    {
        var owner = new Window { Width = 900, Height = 600, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        owner.Show();
        owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        return owner;
    }

    /// <summary>Abre o pop-up modal por <paramref name="open"/> e roda <paramref name="inspect"/> dentro do laço dele (o pop-up é achado entre as janelas da dona).</summary>
    private static void InspectModal<T>(Window owner, Action<T> inspect, Action open) where T : Window
    {
        var observed = false; string? error = null; var attempts = 0;
        void Run()
        {
            var dialog = owner.OwnedWindows.OfType<T>().SingleOrDefault();
            if (dialog == null)
            {
                if (++attempts < 3000) owner.Dispatcher.BeginInvoke(Run, DispatcherPriority.ApplicationIdle);
                else error = "O pop-up não abriu.";
                return;
            }
            observed = true;
            try { inspect(dialog); }
            catch (Exception failure) { error = failure.Message; }
            finally { if (dialog.IsVisible) dialog.Close(); }
        }
        owner.Dispatcher.BeginInvoke(Run, DispatcherPriority.ApplicationIdle);
        open();
        Require(observed && error == null, error ?? "Nenhum pop-up foi aberto.");
    }

    private static void RaiseKeyDown(UIElement element, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element)!, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent, Source = element };
        element.RaiseEvent(args);
    }

    /// <summary>Um campo de formulário de teste: rótulo e controle.</summary>
    private static StackPanel Field(string label, FrameworkElement input)
    {
        var field = new StackPanel();
        field.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        field.Children.Add(input);
        return field;
    }

    private static readonly DependencyPropertyKey? MouseOverKey = typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as DependencyPropertyKey;

    private static void RunChartAndDialogChecks(Application application, ThemeService themes, string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("ShowForm: o primário fecha com verdadeiro; com falso o pop-up fica aberto mostrando o erro, e enquanto roda o botão fica desabilitado", () =>
        {
            var owner = OffScreenOwner();
            try
            {
                var gates = new Queue<TaskCompletionSource<bool>>();
                var calls = 0;
                var content = new FormGrid();
                var nameBox = new TextBox { Width = 300, MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center };
                content.Children.Add(Field("Nome completo", nameBox)); content.Children.Add(Field("Conselho profissional", new TextBox { MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center })); content.Children.Add(Field("Especialidade", new ComboBox { MinHeight = 38 }));
                var saved = false;
                Task<bool> Save() { calls++; var gate = new TaskCompletionSource<bool>(); gates.Enqueue(gate); return gate.Task; }
                InspectModal<FormDialog>(owner, dialog =>
                {
                    try
                    {
                        Require(dialog.Title == "Novo cadastro" && dialog.IsVisible, "O pop-up não abriu com o título.");
                        var buttons = Descendants<Button>(dialog).ToArray();
                        var primary = buttons.Single(button => button.Content as string == "Cadastrar");
                        var close = buttons.Single(button => AutomationProperties.GetName(button) == "Fechar sem salvar");
                        var secondary = buttons.Single(button => button.Content as string == "Sugerir");
                        Require(primary.IsDefault && !close.IsDefault && primary.TranslatePoint(new Point(0, 0), dialog).X > secondary.TranslatePoint(new Point(0, 0), dialog).X, "O primário deveria ser o botão padrão e ficar à direita do secundário.");
                        Require(close.TranslatePoint(new Point(0, 0), dialog).X > dialog.ActualWidth - 90 && close.TranslatePoint(new Point(0, 0), dialog).Y < 70, "O X deveria ficar no canto superior direito.");
                        Require(Descendants<ScrollViewer>(dialog).Any(scroll => scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && Descendants<TextBox>(scroll).Contains(nameBox)), "O formulário deveria ficar numa área com rolagem.");
                        Require(!Descendants<Border>(dialog).Any(border => FindAncestor<TextBox>(border) is null && border.Background is SolidColorBrush brush && brush.Color == ThemeBrush(application, "SurfaceAltBrush").Color && border.CornerRadius.TopLeft >= 8 && border.ActualWidth > 100), "Não deveria haver card dentro do pop-up.");
                        foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                        {
                            themes.Apply(mode); dialog.UpdateLayout(); dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                            dialog.ShowError(mode == Appearance.Dark ? "Informe o nome." : null);
                            dialog.UpdateLayout(); SavePreview(dialog, Path.Combine(previewRoot, $"pop-up-cadastro-{mode.ToString().ToLowerInvariant()}.png"));
                        }
                        themes.Apply(Appearance.Light); dialog.ShowError(null);
                        InvokeButton(primary);
                        Require(calls == 1 && dialog.IsBusy && !primary.IsEnabled && !close.IsEnabled && !secondary.IsEnabled, "Enquanto a função roda os botões deveriam ficar desabilitados.");
                        Require(Descendants<ProgressBar>(dialog).Single().IsVisible, "O pop-up deveria mostrar o andamento enquanto salva.");
                        Require(dialog.SubmitAsync().IsCompleted && calls == 1, "Um segundo pedido durante o salvamento não deveria chamar a função de novo.");
                        gates.Dequeue().SetResult(false);
                        PumpUntil(() => !dialog.IsBusy);
                        var errorBox = Descendants<Border>(dialog).Single(border => AutomationProperties.GetName(border) == "Erro ao salvar");
                        Require(dialog.IsVisible && !dialog.Saved && errorBox.IsVisible && dialog.ErrorMessage == "Informe o nome." && primary.IsEnabled && close.IsEnabled, "Com falso o pop-up deveria continuar aberto com a mensagem de erro e os botões de volta.");
                        InvokeButton(primary);
                        Require(!errorBox.IsVisible && calls == 2, "Salvar de novo deveria limpar a mensagem e chamar a função.");
                        gates.Dequeue().SetResult(true);
                        PumpUntil(() => !dialog.IsVisible);
                        saved = dialog.Saved;
                    }
                    finally { while (gates.Count > 0) gates.Dequeue().TrySetResult(false); }
                }, () => Require(DialogService.ShowForm(owner, "Novo cadastro", content, Save, "Cadastrar", "Sugerir", () => Task.CompletedTask, () => "Informe o nome.") && saved, "ShowForm deveria devolver verdadeiro depois do primário verdadeiro."));
            }
            finally { owner.Close(); }
        });

        Check("ShowForm: X e Esc fecham sem salvar; Enter avança de campo e, no último, aciona o primário; erro inesperado da função vira mensagem no pop-up", () =>
        {
            var owner = OffScreenOwner();
            try
            {
                var calls = 0;
                Task<bool> Save() { calls++; return Task.FromResult(true); }
                InspectModal<FormDialog>(owner, dialog => InvokeButton(Descendants<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Fechar sem salvar")),
                    () => Require(!DialogService.ShowForm(owner, "Sair sem salvar", new TextBox(), Save), "O X não deveria salvar."));
                InspectModal<FormDialog>(owner, dialog =>
                {
                    AccessKeyManager.ProcessKey(PresentationSource.FromVisual(dialog)!, "\u001b", false);
                    Require(!dialog.IsVisible && !dialog.Saved, "Esc não fechou o pop-up.");
                }, () => Require(!DialogService.ShowForm(owner, "Esc", new TextBox(), Save), "Esc não deveria salvar."));
                Require(calls == 0, "Fechar sem salvar chamou a função de salvar.");

                var first = new TextBox(); var second = new TextBox();
                EnterNavigation.SetGroup(first, "pop-up-teste"); EnterNavigation.SetGroup(second, "pop-up-teste");
                var form = new StackPanel(); form.Children.Add(first); form.Children.Add(second);
                InspectModal<FormDialog>(owner, dialog =>
                {
                    Require(first.Focus(), "Não foi possível focar o primeiro campo.");
                    PressEnter(first);
                    Require(second.IsKeyboardFocused && calls == 0, "Enter deveria avançar para o campo seguinte sem salvar.");
                    PressEnter(second);
                    PumpUntil(() => !dialog.IsVisible);
                    Require(calls == 1 && dialog.Saved, "Enter no último campo deveria acionar o primário.");
                }, () => Require(DialogService.ShowForm(owner, "Enter", form, Save), "Enter no último campo deveria salvar."));

                InspectModal<FormDialog>(owner, dialog =>
                {
                    InvokeButton(Descendants<Button>(dialog).Single(button => button.Content as string == "Salvar"));
                    PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && dialog.ErrorMessage.Length > 0 && !dialog.ErrorMessage.Contains("boom", StringComparison.Ordinal), "Uma exceção deveria virar mensagem amigável dentro do pop-up (sem o texto técnico).");
                }, () => Require(!DialogService.ShowForm(owner, "Falha", new TextBox(), () => throw new InvalidOperationException("boom")), "Com exceção o pop-up não deveria devolver verdadeiro."));
            }
            finally { owner.Close(); }
        });

        Check("Ficha em linhas: rótulo à esquerda, valor à direita em destaque, fio entre as linhas, seções discretas, sem cards, cópia e área de ações no rodapé", () =>
        {
            var owner = OffScreenOwner();
            var clipboard = new ClipboardFixture();
            var details = new ItemDetails("Maria Souza", "Paciente da UBS", [new("Nome", "Maria Souza", true, "Identificação"), new("CPF", "529.982.247-25", true, "Identificação"), new("Situação", "Ativo", false, "Cadastro")]);
            var popup = new ItemDetailsWindow(details, clipboard) { Owner = owner, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
            try
            {
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    themes.Apply(mode);
                    if (!popup.IsVisible) popup.Show();
                    popup.UpdateLayout(); popup.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Require(Descendants<Border>(popup).Any(border => AutomationProperties.GetName(border) == "Ficha de detalhes") && Descendants<Button>(popup).Any(button => AutomationProperties.GetName(button) == "Fechar detalhes"), $"{mode}: a ficha perdeu os nomes de automação.");
                    var fields = Descendants<ItemsControl>(popup).Single();
                    var rows = Descendants<Border>(fields).Where(border => border.BorderThickness.Bottom == 1 && border.BorderThickness.Top == 0).ToArray();
                    Require(rows.Length == 3, $"{mode}: deveria haver uma linha por campo ({rows.Length}).");
                    foreach (var (row, field) in rows.Zip(details.Fields))
                    {
                        Require(((SolidColorBrush)row.BorderBrush).Color == ThemeBrush(application, "DividerBrush").Color, $"{mode}: o fio entre as linhas não usa a cor do tema.");
                        var label = Descendants<TextBlock>(row).Single(text => text.Text == field.Label);
                        var value = Descendants<TextBlock>(row).Single(text => text.Text == field.Value);
                        Require(((SolidColorBrush)label.Foreground).Color == ThemeBrush(application, "SecondaryBrush").Color && ((SolidColorBrush)value.Foreground).Color == ThemeBrush(application, "TextBrush").Color && value.FontWeight == FontWeights.SemiBold, $"{mode}: rótulo em cor secundária e valor em destaque.");
                        Require(label.TranslatePoint(new Point(0, 0), row).X < value.TranslatePoint(new Point(0, 0), row).X && value.HorizontalAlignment == HorizontalAlignment.Right && value.FontSize > label.FontSize, $"{mode}: o rótulo fica à esquerda e o valor à direita.");
                    }
                    var titles = VisibleTexts(fields);
                    Require(titles.Count(text => text == "Identificação") == 1 && titles.Count(text => text == "Cadastro") == 1, $"{mode}: cada seção deveria ter um título.");
                    Require(!Descendants<Border>(fields).Any(border => border.Background is SolidColorBrush brush && brush.Color == ThemeBrush(application, "SurfaceAltBrush").Color && border.CornerRadius.TopLeft >= 8), $"{mode}: a ficha não deveria ter cards.");
                    Require(Descendants<Button>(fields).Count(button => button.IsVisible && AutomationProperties.GetName(button).StartsWith("Copiar ", StringComparison.Ordinal)) == 2, $"{mode}: só os campos copiáveis têm o botão de copiar.");
                    SavePreview(popup, Path.Combine(previewRoot, $"ficha-em-linhas-{mode.ToString().ToLowerInvariant()}.png"));
                }
                clipboard.Values.Clear();
                InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Copiar CPF"));
                Require(clipboard.Values.SequenceEqual(["529.982.247-25"]), "O botão de copiar não copiou o valor do campo.");
                Require(popup.Actions is null && Descendants<ContentControl>(popup).Single(control => control.Name == "ActionsHost").Visibility == Visibility.Collapsed, "Sem ações o rodapé não deveria reservar a área.");
                var edit = new Button { Content = "Editar ficha" };
                popup.Actions = edit; popup.UpdateLayout();
                Require(edit.IsVisible && edit.TranslatePoint(new Point(0, 0), popup).X > popup.ActualWidth / 2, "A área de ações deveria mostrar o botão à direita, no rodapé.");
                themes.Apply(Appearance.Light);
            }
            finally { popup.Close(); owner.Close(); }
        });

        Check("SegmentedToggle: troca por clique e por setas, liga a SelectedItem nos dois sentidos, tem nomes de automação e hover azul bebê", () =>
        {
            var toggle = new SegmentedToggle { ItemsSource = new[] { "Dia", "Semana", "Mês", "Ano" } };
            AutomationProperties.SetName(toggle, "Período");
            var state = new ToggleState { Value = "Semana" };
            BindingOperations.SetBinding(toggle, System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(ToggleState.Value)) { Source = state, Mode = BindingMode.TwoWay });
            var window = new Window { Content = toggle, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            window.Show();
            try
            {
                window.UpdateLayout();
                Require(toggle.SelectedIndex == 1 && toggle.SelectionMode == SelectionMode.Single, "A seleção inicial deveria vir da ligação.");
                ListBoxItem Item(int index) => (ListBoxItem)toggle.ItemContainerGenerator.ContainerFromIndex(index);
                Require(Enumerable.Range(0, 4).Select(index => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(Item(index)).GetName()).SequenceEqual(["Dia", "Semana", "Mês", "Ano"]) && AutomationProperties.GetName(toggle) == "Período", "Cada segmento e o alternador precisam de nome para o leitor de tela.");
                // Clique.
                var target = Item(3);
                target.RaiseEvent(MouseArgs(Mouse.PreviewMouseDownEvent, target)); target.RaiseEvent(MouseArgs(Mouse.MouseDownEvent, target));
                target.RaiseEvent(MouseArgs(Mouse.PreviewMouseUpEvent, target)); target.RaiseEvent(MouseArgs(Mouse.MouseUpEvent, target));
                Require(toggle.SelectedIndex == 3 && state.Value == "Ano", "O clique deveria selecionar o segmento e levar o valor à ligação.");
                // A ligação também manda para o alternador.
                state.Value = "Dia";
                Require(toggle.SelectedIndex == 0, "Mudar o valor na ligação deveria mudar o segmento.");
                // Teclado.
                RaiseKeyDown(toggle, Key.Right);
                Require(toggle.SelectedIndex == 1 && state.Value == "Semana", "A seta para a direita deveria ir ao segmento seguinte.");
                RaiseKeyDown(toggle, Key.End);
                Require(toggle.SelectedIndex == 3, "End deveria ir ao último segmento.");
                RaiseKeyDown(toggle, Key.Right);
                Require(toggle.SelectedIndex == 3, "A seta no último segmento não deveria dar a volta.");
                RaiseKeyDown(toggle, Key.Left); RaiseKeyDown(toggle, Key.Left);
                Require(toggle.SelectedIndex == 1, "A seta para a esquerda deveria voltar um segmento por vez.");
                RaiseKeyDown(toggle, Key.Home);
                Require(toggle.SelectedIndex == 0, "Home deveria ir ao primeiro segmento.");
                // Visual: o selecionado se destaca e o hover é azul bebê.
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    themes.Apply(mode); window.UpdateLayout();
                    var selected = (Border)Item(0).Template.FindName("Frame", Item(0));
                    var other = Item(2); var otherFrame = (Border)other.Template.FindName("Frame", other);
                    Require(((SolidColorBrush)selected.BorderBrush).Color == ThemeBrush(application, "BrandBrush").Color && Item(0).FontWeight == FontWeights.SemiBold, $"{mode}: o segmento selecionado deveria ter contorno da marca e texto em destaque (não só cor).");
                    other.SetValue(MouseOverKey!, true); other.UpdateLayout();
                    Require(((SolidColorBrush)otherFrame.Background).Color == ThemeBrush(application, "ButtonHoverBrush").Color, $"{mode}: o hover do segmento deveria ser o azul bebê.");
                    other.SetValue(MouseOverKey!, false);
                }
                themes.Apply(Appearance.Light);
            }
            finally { window.Close(); }
        });

        Check("Agrupamento do eixo dos gráficos: dia até 92 dias, semana até 400 e mês acima; os totais nunca mudam", () =>
        {
            Require(ChartData.For(30) == ChartGrouping.Day && ChartData.For(92) == ChartGrouping.Day && ChartData.For(93) == ChartGrouping.Week && ChartData.For(365) == ChartGrouping.Week && ChartData.For(400) == ChartGrouping.Week && ChartData.For(401) == ChartGrouping.Month && ChartData.For(1826) == ChartGrouping.Month, "Os limites de agrupamento mudaram.");
            var start = new DateOnly(2026, 1, 5); // uma segunda-feira
            var days = Enumerable.Range(0, 120).Select(index => new DailyPoint(start.AddDays(index), index % 5, index % 3, index % 2)).ToArray();
            var daily = ChartData.Group(days, ChartGrouping.Day);
            Require(daily.Count == 120 && daily.Select(bucket => bucket.Description).SequenceEqual(days.Select(day => day.Description)), "Cada dia deveria ter a mesma dica de sempre.");
            var weeks = ChartData.Group(days, ChartGrouping.Week);
            Require(weeks.Count == 18 && weeks.All(bucket => bucket.Start.DayOfWeek == DayOfWeek.Monday) && weeks[0].Start == start && weeks[^1].End == days[^1].Date, $"Semanas incorretas: {weeks.Count}.");
            Require(weeks.Sum(bucket => bucket.Completed) == days.Sum(day => day.Completed) && weeks.Sum(bucket => bucket.Missed) == days.Sum(day => day.Missed) && weeks.Sum(bucket => bucket.Cancelled) == days.Sum(day => day.Cancelled), "Os totais por semana não batem com os dias.");
            Require(weeks[0].Description.StartsWith("Semana de 05/01 a 11/01/2026: ", StringComparison.Ordinal), "A dica da semana deveria dizer o intervalo: " + weeks[0].Description);
            var months = ChartData.Group(days, ChartGrouping.Month);
            Require(months.Count == 5 && months[0].Start == new DateOnly(2026, 1, 1) && months[0].Label.StartsWith("jan", StringComparison.Ordinal) && months[0].Label.EndsWith("26", StringComparison.Ordinal) && months.Sum(bucket => bucket.Completed) == days.Sum(day => day.Completed), $"Meses incorretos: {string.Join(", ", months.Select(bucket => bucket.Label))}.");
            Require(months[0].Description.StartsWith("Janeiro de 2026: ", StringComparison.Ordinal), "A dica do mês deveria trazer o nome do mês: " + months[0].Description);
            Require(ChartData.Group(days).Count == 18 && ChartData.Group(days.Take(30).ToArray()).Count == 30 && ChartData.Group(Enumerable.Range(0, 200).Select(index => new DailyPoint(start.AddDays(index), 1, 0, 0)).ToArray()).Count == 29, "Sem o agrupamento pedido, a escolha deveria seguir o tamanho da série.");
            var lines = ChartData.Evolution(days, ChartGrouping.Week);
            Require(lines.Select(line => line.Name).SequenceEqual(["Concluídas", "Faltas", "Canceladas"]) && lines.All(line => line.Points.Count == 18) && lines[0].Dash is null && lines[1].Dash is not null && lines[2].Dash is not null, "As três linhas deveriam ter 18 pontos, com faltas e canceladas tracejadas.");
            Require(lines[2].BrushKey == "StatusCancelledBrush" && lines[0].BrushKey == "StatusCompletedBrush" && lines[1].BrushKey == "StatusMissedBrush", "As linhas deveriam usar as cores de situação do tema.");
            Require(ChartData.Outcomes([new(start, 0, 0, 0)]).Count == 0 && ChartData.Outcomes(days).Select(slice => slice.Label).SequenceEqual(["Concluídas", "Faltas", "Canceladas"]), "A pizza de consultas encerradas está incorreta.");
        });

        Check("Gráfico de linhas: linha grossa arredondada, área em degradê, pontos, dica por ponto, cores do tema; período longo agrupa por semana", () =>
        {
            var series = new ChartSeries();
            var start = DateOnly.FromDateTime(DateTime.Today).AddDays(-29);
            var thirty = Enumerable.Range(0, 30).Select(index => new DailyPoint(start.AddDays(index), index % 4, index % 3, index % 2)).ToArray();
            series.Apply(thirty, thirty.Where(day => day.Completed + day.Missed + day.Cancelled > 0).ToArray(), "sem dados");
            var chart = new EvolutionChartView { Data = series, Width = 760 };
            var window = new Window { Content = chart, SizeToContent = SizeToContent.Height, Width = 800, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            window.Show();
            try
            {
                window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var polylines = Descendants<Polyline>(chart).ToArray();
                Require(polylines.Length == 3 && polylines.All(line => line.StrokeThickness >= 3 && line.StrokeLineJoin == PenLineJoin.Round && line.StrokeStartLineCap == PenLineCap.Round), "As linhas deveriam ser grossas, com cantos arredondados.");
                var areas = Descendants<Polygon>(chart).ToArray();
                Require(areas.Length == 3 && areas.All(area => area.OpacityMask is LinearGradientBrush), "Cada linha deveria ter uma área em degradê.");
                Require(Descendants<Ellipse>(chart).Count() == 90, "Com 30 pontos cada linha deveria ter os pontos marcados.");
                Require(Descendants<Rectangle>(chart).Count(rectangle => rectangle.ToolTip is string) == 30, "Cada dia deveria ter uma dica.");
                Require(Descendants<Line>(chart).Count(line => (line.StrokeDashArray?.Count ?? 0) == 0 && line.StrokeThickness == 1) >= 3, "A grade discreta deveria estar desenhada.");
                Require(!Descendants<Expander>(chart).Any(), "O gráfico não deveria ter o expansor da tabela.");
                Require(chart.FindName("StepText") is TextBlock { IsVisible: false }, "Com um ponto por dia não há nota do passo.");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    themes.Apply(mode); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Require(((SolidColorBrush)polylines[2].Stroke).Color == ThemeBrush(application, "StatusCancelledBrush").Color && ((SolidColorBrush)polylines[0].Stroke).Color == ThemeBrush(application, "StatusCompletedBrush").Color, $"{mode}: as linhas deveriam acompanhar as cores de situação do tema.");
                }
                themes.Apply(Appearance.Light);
                // Período longo: um ponto por semana.
                var long120 = Enumerable.Range(0, 120).Select(index => new DailyPoint(start.AddDays(index), 1, 0, 0)).ToArray();
                series.Apply(long120, long120, "sem dados"); window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var weekly = Descendants<Polyline>(chart).ToArray();
                Require(weekly.Length == 3 && weekly.All(line => line.Points.Count == 18) && Descendants<Rectangle>(chart).Count(rectangle => rectangle.ToolTip is string) == 18, $"Com 120 dias o eixo deveria ter um ponto por semana ({weekly[0].Points.Count}).");
                Require(chart.FindName("StepText") is TextBlock { IsVisible: true } step && step.Text.Contains("semana", StringComparison.Ordinal), "A nota deveria dizer que cada ponto é uma semana.");
                series.Clear("Nenhuma consulta no período.");
                window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(Descendants<Polyline>(chart).Count() == 0 && VisibleTexts(chart).Contains("Nenhuma consulta no período."), "Sem dados deveria aparecer o motivo.");
            }
            finally { window.Close(); }
        });

        Check("ChartPanel: título com o alternador Linha | Pizza à direita; alterna os dois gráficos; com só um tipo de dado o alternador some; rosca com legenda de valor e percentual", () =>
        {
            var panel = new ChartPanel { Title = "Consultas por situação", Width = 620, Height = 360 };
            var host = new Border { Child = panel, Padding = new Thickness(16) };
            host.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            var window = new Window { Content = host, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            window.Show();
            void Settle() { window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
            // A troca de gráfico faz um fade de 180 ms: as prévias esperam ele terminar.
            void Preview(string name) { var end = DateTime.UtcNow.AddMilliseconds(300); PumpUntil(() => DateTime.UtcNow > end); SavePreview(host, Path.Combine(previewRoot, name)); }
            try
            {
                var line = new ChartLine("Concluídas", "StatusCompletedBrush", [new("01/06", 3), new("02/06", 5), new("03/06", 4)]);
                var slices = new ChartPoint[] { new("Concluídas", 12, "StatusCompletedBrush"), new("Faltas", 6, "StatusMissedBrush"), new("Canceladas", 2, "StatusCancelledBrush"), new("Sem consultas", 0) };
                panel.Lines = [line]; panel.Slices = slices; Settle();
                var title = Descendants<TextBlock>(panel).First(text => text.Text == "Consultas por situação");
                Require(panel.Toggle.IsVisible && panel.Toggle.TranslatePoint(new Point(0, 0), panel).X > title.TranslatePoint(new Point(0, 0), panel).X + title.ActualWidth - 1 && Math.Abs(panel.Toggle.TranslatePoint(new Point(0, 0), panel).Y - title.TranslatePoint(new Point(0, 0), panel).Y) < 30, "O alternador deveria ficar à direita do título, na mesma linha.");
                Require(panel.Toggle.Items.Cast<string>().SequenceEqual(["Linha", "Pizza"]) && panel.Kind == ChartKind.Line && panel.Toggle.SelectedIndex == 0, "Deveria começar em Linha.");
                var lineView = Descendants<LineChartView>(panel).Single(); var pie = Descendants<OutcomePie>(panel).Single();
                Require(lineView.IsVisible && !pie.IsVisible && Descendants<Polyline>(lineView).Count() == 1, "No modo Linha só a linha aparece.");
                panel.Lines = [line, new ChartLine("Faltas", "StatusMissedBrush", [new("01/06", 1), new("02/06", 2), new("03/06", 0)], [5, 3]), new ChartLine("Canceladas", "StatusCancelledBrush", [new("01/06", 0), new("02/06", 1), new("03/06", 1)], [1.5, 3])];
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark }) { themes.Apply(mode); Settle(); Preview($"grafico-linha-{mode.ToString().ToLowerInvariant()}.png"); }
                panel.Lines = [line];
                panel.Toggle.SelectedIndex = 1; Settle();
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark }) { themes.Apply(mode); Settle(); Preview($"grafico-pizza-{mode.ToString().ToLowerInvariant()}.png"); }
                themes.Apply(Appearance.Light); Settle();
                Require(panel.Kind == ChartKind.Pie && pie.IsVisible && !lineView.IsVisible && pie.HoleRatio >= 0.5, "Escolher Pizza deveria mostrar a rosca.");
                var legend = VisibleTexts((FrameworkElement)panel.FindName("PieLegend"));
                Require(legend.SequenceEqual(["Concluídas", "12", "60%", "Faltas", "6", "30%", "Canceladas", "2", "10%"]), "A legenda deveria trazer nome, valor e percentual (sem a fatia zerada): " + string.Join(" | ", legend));
                Require(Descendants<Ellipse>((FrameworkElement)panel.FindName("PieLegend")).Select(dot => ((SolidColorBrush)dot.Fill).Color).SequenceEqual(new[] { "StatusCompletedBrush", "StatusMissedBrush", "StatusCancelledBrush" }.Select(key => ThemeBrush(application, key).Color)), "A legenda deveria usar as cores das fatias.");
                panel.Toggle.SelectedIndex = 0; Settle();
                Require(panel.Kind == ChartKind.Line && lineView.IsVisible && !pie.IsVisible, "Voltar a Linha deveria mostrar a linha.");
                panel.Kind = ChartKind.Pie; Settle();
                Require(panel.Toggle.SelectedIndex == 1 && pie.IsVisible, "Mudar o Kind por código também deveria alternar.");
                // Só um tipo de dado: o alternador some e vale o que existe.
                panel.Slices = []; Settle();
                Require(!panel.Toggle.IsVisible && panel.Kind == ChartKind.Line && lineView.IsVisible && !pie.IsVisible, "Só com linha o alternador some e a linha aparece.");
                panel.Lines = []; panel.Slices = slices; Settle();
                Require(!panel.Toggle.IsVisible && panel.Kind == ChartKind.Pie && pie.IsVisible, "Só com pizza o alternador some e a pizza aparece.");
                panel.Slices = []; panel.LineNote = "Sem dados de evolução para este indicador."; Settle();
                Require(!panel.Toggle.IsVisible && !lineView.IsVisible && !pie.IsVisible && VisibleTexts(panel).Contains("Sem dados de evolução para este indicador."), "Sem nenhum dado deveria aparecer a nota explicando.");
            }
            finally { window.Close(); }
        });

        Check("Rosca: cores das situações no lugar certo (canceladas em amarelo), fatias separadas, buraco no meio e acompanha a troca de tema", () =>
        {
            var pie = new OutcomePie { Width = 200, Height = 200, HoleRatio = 0.6, Slices = [new("Concluídas", 1, "StatusCompletedBrush"), new("Faltas", 1, "StatusMissedBrush"), new("Canceladas", 1, "StatusCancelledBrush")] };
            var window = new Window { Content = pie, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            window.Show();
            try
            {
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    themes.Apply(mode); window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var bitmap = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(pie);
                    Color Pixel(double angleDegrees, double radius)
                    {
                        var radians = angleDegrees * Math.PI / 180;
                        var x = (int)Math.Round(100 + radius * Math.Sin(radians)); var y = (int)Math.Round(100 - radius * Math.Cos(radians));
                        var data = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), data, 4, 0);
                        return Color.FromArgb(data[3], data[2], data[1], data[0]);
                    }
                    static bool Close(Color a, Color b) => Math.Abs(a.R - b.R) < 6 && Math.Abs(a.G - b.G) < 6 && Math.Abs(a.B - b.B) < 6 && a.A > 250;
                    Require(Close(Pixel(60, 80), ThemeBrush(application, "StatusCompletedBrush").Color) && Close(Pixel(180, 80), ThemeBrush(application, "StatusMissedBrush").Color) && Close(Pixel(300, 80), ThemeBrush(application, "StatusCancelledBrush").Color), $"{mode}: as fatias deveriam usar as cores de situação do tema, na ordem.");
                    var seam = Pixel(120, 80);
                    Require(!Close(seam, ThemeBrush(application, "StatusCompletedBrush").Color) && !Close(seam, ThemeBrush(application, "StatusMissedBrush").Color), $"{mode}: entre duas fatias deveria haver um fio de separação.");
                    Require(Pixel(0, 50).A == 0 || Close(Pixel(0, 50), ThemeBrush(application, "SurfaceBrush").Color), $"{mode}: a rosca deveria ter o buraco no meio.");
                }
                var yellow = ThemeBrush(application, "StatusCancelledBrush").Color;
                Require(yellow.R > 200 && yellow.G > 150 && yellow.B < 120, "As canceladas deveriam ser amarelas.");
                themes.Apply(Appearance.Light);
                Require(AutomationProperties.GetName(pie).StartsWith("Composição: Concluídas 1", StringComparison.Ordinal), "A rosca deveria descrever as fatias para o leitor de tela.");
            }
            finally { window.Close(); }
        });

        Check("Pop-up do gráfico do card: só o título e o alternador Linha | Pizza (sem \"Valor atual\"), com o X, e a pizza usa as cores das situações", () =>
        {
            var owner = OffScreenOwner();
            try
            {
                var chart = new IndicatorChart("Consultas concluídas", [new("01/06", 3), new("02/06", 5)], "Evolução diária no período do filtro.", [new("Concluídas", 8, "StatusCompletedBrush"), new("Faltas", 2, "StatusMissedBrush")], "Composição das consultas encerradas no período.");
                var window = new IndicatorChartWindow(chart) { Owner = owner, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                try
                {
                    window.Show(); window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var texts = VisibleTexts(window);
                    Require(texts.Contains("Consultas concluídas") && !texts.Any(text => text.Contains("Valor atual", StringComparison.Ordinal)), "O pop-up não deveria mais mostrar o \"Valor atual\": " + string.Join(" | ", texts));
                    Require(window.Chart.Toggle.IsVisible && window.Chart.Toggle.Items.Cast<string>().SequenceEqual(["Linha", "Pizza"]) && Descendants<Button>(window).Any(button => AutomationProperties.GetName(button) == "Fechar gráfico"), "O pop-up deveria ter o alternador e o X.");
                    Require(texts.Contains("Evolução diária no período do filtro."), "A nota de origem dos dados da linha deveria continuar.");
                    window.Chart.Toggle.SelectedIndex = 1; window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Require(Descendants<OutcomePie>(window).Single().IsVisible && VisibleTexts(window).Contains("80%") && !VisibleTexts(window).Any(text => text.Contains("Valor atual", StringComparison.Ordinal)), "A pizza deveria aparecer com a legenda de percentuais.");
                    InvokeButton(Descendants<Button>(window).Single(button => AutomationProperties.GetName(button) == "Fechar gráfico"));
                    Require(!window.IsVisible, "O X deveria fechar o pop-up.");
                    // Indicador só com a composição: sem alternador.
                    var onlyPie = new IndicatorChartWindow(new IndicatorChart("Faltas", [], "Sem dados de evolução para este indicador.", [new("Faltas", 4, "StatusMissedBrush")], "Composição."));
                    try
                    {
                        onlyPie.Owner = owner; onlyPie.WindowStartupLocation = WindowStartupLocation.Manual; onlyPie.Left = -10000; onlyPie.Top = -10000; onlyPie.ShowActivated = false;
                        onlyPie.Show(); onlyPie.UpdateLayout();
                        Require(!onlyPie.Chart.Toggle.IsVisible && onlyPie.Chart.Kind == ChartKind.Pie, "Só com pizza o pop-up não deveria mostrar o alternador.");
                    }
                    finally { onlyPie.Close(); }
                }
                finally { if (window.IsVisible) window.Close(); }
            }
            finally { owner.Close(); }
        });

        foreach (var profile in new[] { DesktopProfile.Operational, DesktopProfile.Municipal })
        Check($"{profile.Name}: ViewModeToggle alterna cards e gráficos com ícone, dica e nome do modo atual, e lembra a escolha por tela nas preferências", () =>
        {
            var name = "view-mode-" + profile.Id;
            var window = OpenHidden(profile, storageRoot, name);
            try
            {
                var container = (Panel)window.FindName("PageContainer");
                var home = new ViewModeToggle { ScreenKey = "home" }; var indicators = new ViewModeToggle { ScreenKey = "indicators" };
                container.Children.Add(home); container.Children.Add(indicators);
                Layout(window, 1280, 740); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Geometry? Icon(ViewModeToggle toggle) => ((System.Windows.Shapes.Path)toggle.Template.FindName("Icon", toggle)).Data;
                Require(home.Mode == ViewMode.Cards && ReferenceEquals(Icon(home), application.FindResource("IconCardsGeometry")), "Sem escolha anterior a tela abre em cards, com o ícone de cards.");
                Require(home.ToolTip is string tipCards && tipCards.Contains("cards", StringComparison.Ordinal) && tipCards.Contains("gráficos", StringComparison.Ordinal) && AutomationProperties.GetName(home).Contains("cards", StringComparison.Ordinal) && AutomationProperties.GetName(home).Contains("gráficos", StringComparison.Ordinal), "A dica e o nome deveriam dizer o modo atual e o que o clique faz.");
                InvokeButton(home);
                Require(home.Mode == ViewMode.Charts && indicators.Mode == ViewMode.Cards && ReferenceEquals(Icon(home), application.FindResource("IconChartGeometry")), "Clicar deveria trocar para gráficos, com o ícone de gráfico, só nesta tela.");
                Require(AutomationProperties.GetName(home).StartsWith("Alternar visualização: em gráficos", StringComparison.Ordinal) && ((string)home.ToolTip).StartsWith("Visualização em gráficos", StringComparison.Ordinal), "A dica e o nome deveriam acompanhar o novo modo.");
                var saved = new PreferencesService(profile.Id, Path.Combine(storageRoot, name)).Load();
                Require(saved.ViewModes.Count == 1 && saved.ViewModes["home"] == "charts", "A escolha deveria ter sido gravada por tela nas preferências.");
                Require(window.ViewModel.GetViewMode("home") == ViewMode.Charts && window.ViewModel.GetViewMode("indicators") == ViewMode.Cards, "O ViewModel deveria responder por tela.");
                // O mesmo botão ligado por ElementName (como as telas usam) segue o Mode.
                var follower = new TextBlock(); BindingOperations.SetBinding(follower, TextBlock.TagProperty, new Binding(nameof(ViewModeToggle.Mode)) { Source = home });
                Require(Equals(follower.Tag, ViewMode.Charts), "Quem liga no Mode do botão deveria enxergar o modo.");
                CloseWindow(window);
                // Nova abertura do programa: lembra.
                window = OpenHidden(profile, storageRoot, name);
                container = (Panel)window.FindName("PageContainer");
                home = new ViewModeToggle { ScreenKey = "home" }; indicators = new ViewModeToggle { ScreenKey = "indicators" };
                container.Children.Add(home); container.Children.Add(indicators);
                Layout(window, 1280, 740); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(home.Mode == ViewMode.Charts && indicators.Mode == ViewMode.Cards && ReferenceEquals(Icon(home), application.FindResource("IconChartGeometry")), "Ao reabrir, cada tela deveria voltar ao modo que ficou.");
                // Sem ScreenKey vale a rota aberta.
                window.ViewModel.Navigate("indicators"); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var routeKey = window.ViewModel.Page;
                var byRoute = new ViewModeToggle(); container.Children.Add(byRoute);
                Layout(window, 1280, 740); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                InvokeButton(byRoute);
                Require(routeKey == "indicators" && window.ViewModel.GetViewMode(routeKey) == ViewMode.Charts, "Sem chave, o botão deveria lembrar o modo da rota aberta.");
                InvokeButton(home); InvokeButton(byRoute);
                saved = new PreferencesService(profile.Id, Path.Combine(storageRoot, name)).Load();
                Require(saved.ViewModes.Count == 0 && home.Mode == ViewMode.Cards, "Voltar a cards deveria tirar a tela do arquivo (cards é o padrão).");
            }
            finally { CloseWindow(window); }
        });

        Check("Preferências: modos de tela desconhecidos ou vazios voltam ao padrão (cards) e o botão fora da janela principal só alterna", () =>
        {
            var folder = Path.Combine(storageRoot, "view-mode-invalid");
            var service = new PreferencesService("obs", folder);
            Directory.CreateDirectory(Path.GetDirectoryName(service.FilePath)!);
            File.WriteAllText(service.FilePath, "{\"ViewModes\":{\"home\":\"charts\",\"indicators\":\"xyz\",\"\":\"charts\",\"agenda\":\"cards\"}}");
            var loaded = service.Load();
            Require(loaded.ViewModes.Count == 1 && loaded.ViewModes["home"] == "charts", "Só valores conhecidos deveriam sobreviver: " + string.Join(", ", loaded.ViewModes.Select(pair => pair.Key + "=" + pair.Value)));
            File.WriteAllText(service.FilePath, "{\"ViewModes\":null}");
            Require(service.Load().ViewModes.Count == 0 && service.Error == null, "ViewModes nulo deveria voltar ao padrão sem erro.");
            var alone = new ViewModeToggle { ScreenKey = "home" };
            var bound = new ViewModeToggle { ScreenKey = "home" };
            var source = new ModeState();
            BindingOperations.SetBinding(bound, ViewModeToggle.ModeProperty, new Binding(nameof(ModeState.Mode)) { Source = source, Mode = BindingMode.TwoWay });
            var host = new StackPanel(); host.Children.Add(alone); host.Children.Add(bound);
            var window = new Window { Content = host, Width = 200, Height = 120, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            window.Show();
            try
            {
                InvokeButton(alone); Require(alone.Mode == ViewMode.Charts, "Fora da janela principal o botão deveria só alternar."); InvokeButton(alone); Require(alone.Mode == ViewMode.Cards, "E voltar.");
                InvokeButton(bound);
                Require(source.Mode == ViewMode.Charts && BindingOperations.IsDataBound(bound, ViewModeToggle.ModeProperty), "O clique deveria levar o modo à ligação sem desfazê-la.");
                source.Mode = ViewMode.Cards;
                Require(bound.Mode == ViewMode.Cards, "Mudar o modo na ligação deveria mudar o botão.");
            }
            finally { window.Close(); }
        });
    }
}
