using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunListingChecks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("OBS: cada aba operacional renderiza somente o conteúdo selecionado", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Abas");
            var window = OpenOperational(fixture, storageRoot, "exclusive-operational-pages");
            try
            {
                var vm = LoginObs(window);
                foreach (var (route, visibleText, hiddenText) in new[]
                {
                    ("home", "Resumo do dia", "Agenda da unidade"),
                    ("agenda", "Agenda da unidade", "Resumo do dia"),
                    ("patients", "Cadastro de pacientes da UBS", "Agenda da unidade"),
                    ("queue", "Fila da UBS", "Pacientes aguardando"),
                    ("slots", "Vagas Relâmpago da UBS", "Fila da UBS"),
                    ("indicators", "Indicadores da UBS", "Central de alertas"),
                    ("alerts", "Central de alertas", "Indicadores da UBS"),
                    ("unit-settings", "Informações da UBS", "Resumo do dia")
                })
                {
                    window.ViewModel.Navigate(route); Settle(vm);
                    var content = Layout(window, 1280, 740);
                    Require(Descendants<TextBlock>(content).Any(text => text.IsVisible && text.Text == visibleText), "A aba não mostrou seu conteúdo: " + route);
                    Require(!Descendants<TextBlock>(content).Any(text => text.IsVisible && text.Text == hiddenText), "A aba manteve conteúdo de outra tela visível: " + route);
                }
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("OBS desconectado: cada aba mantém sua própria orientação", () =>
        {
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "exclusive-operational-pages-signed-out"));
            try
            {
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                PrepareWindow(window);
                foreach (var (route, visibleText, repeatedLogin) in new[]
                {
                    ("agenda", "Agenda da unidade", false),
                    ("indicators", "Indicadores da UBS", false),
                    ("alerts", "Central de alertas", false),
                    ("queue", "Fila da unidade", false),
                    ("patients", "Pacientes da unidade", false),
                    ("slots", "Vagas Relâmpago", false),
                    ("unit-settings", "Informações da UBS", false)
                })
                {
                    window.ViewModel.Navigate(route);
                    var content = Layout(window, 1280, 740);
                    Require(Descendants<TextBlock>(content).Any(text => text.IsVisible && text.Text == visibleText), "A tela desconectada não explica a aba: " + route);
                    Require(repeatedLogin || !Descendants<TextBlock>(content).Any(text => text.IsVisible && text.Text == "Entre no seu espaço de trabalho"), "A aba desconectada repetiu o acesso: " + route);
                }
            }
            finally { window.Close(); }
        });
        Check("Agenda OBS: seleção de data é clicável e atualiza a visão diária", () =>
        {
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "agenda-calendar-selection"));
            try
            {
                window.ViewModel.ConfirmClose = false;
                window.ViewModel.Animations = false;
                PrepareWindow(window);
                window.ViewModel.Navigate("agenda");
                var content = Layout(window, 1280, 740);
                var dayButton = Descendants<Button>(content).FirstOrDefault(button => button.DataContext is AgendaCalendarDay day && day.IsCurrentMonth && !day.IsSelected)
                    ?? throw new InvalidOperationException("Calendário não apresentou um dia clicável.");
                var expected = (AgendaCalendarDay)dayButton.DataContext;
                InvokeButton(dayButton);
                Layout(window, 1280, 740);
                Require(expected.IsSelected && window.ViewModel.Workspace.IsAgendaDay, "Clique no dia não selecionou a data ou não abriu a visão diária.");
                Require(window.ViewModel.Workspace.SelectedAgendaDateLabel.Contains(expected.Date.ToString("yyyy"), StringComparison.Ordinal), "Resumo da agenda não acompanhou a data selecionada.");
            }
            finally { window.Close(); }
        });
        Check("Detalhes: cópia explícita preserva valores e trata clipboard indisponível sem revelar dados", () =>
        {
            var fields = new[]
            {
                new DetailField("Nome completo", "Paciente Fixture de detalhes", true),
                new DetailField("E-mail", "copy@example.invalid", true),
                new DetailField("CPF", "529.982.247-25", true),
                new DetailField("Telefone", "(48) 99900-0123", true),
                new DetailField("Presença no APPNORA", "Online", false)
            };
            var clipboard = new ClipboardFixture();
            var details = new ItemDetailsViewModel(new("Paciente", "Informações da conta", fields), clipboard);
            foreach (var field in fields.Where(field => field.CanCopy))
            {
                Require(details.CopyCommand.CanExecute(field), "Campo copiável desabilitado: " + field.Label);
                details.CopyCommand.Execute(field);
                Require(clipboard.Values[^1] == field.Value && details.CopyStatus == field.Label + " copiado.", "Cópia/feedback perderam o valor: " + field.Label);
            }
            Require(clipboard.Values.Count == 4, "Nem todos os quatro dados do paciente foram copiados pelo serviço isolado.");
            Require(!details.CopyCommand.CanExecute(fields[^1]) && !details.CopyCommand.CanExecute(new DetailField("Dado externo", "not-in-details", true)), "Cópia permite campo informativo ou alheio ao popup.");
            clipboard.Fail = true;
            details.CopyCommand.Execute(fields[0]);
            Require(details.CopyStatus.Contains("Não foi possível copiar", StringComparison.Ordinal) && !details.CopyStatus.Contains(fields[0].Value, StringComparison.Ordinal), "Falha do clipboard não foi tratada ou revelou valor pessoal.");
        });
        Check("Listagens OBS: fila e vagas compartilham itens arredondados e não copiam a linha", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Listagens");
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Cardiologia", DateTimeOffset.UtcNow.AddHours(-1));
            var start = DateTimeOffset.UtcNow.AddDays(1);
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Cardiologia", "Dra. Teste", start, start.AddMinutes(30), "open", DateTimeOffset.UtcNow));
            var window = OpenOperational(fixture, storageRoot, "listing-pattern");
            try
            {
                var vm = LoginObs(window);
                foreach (var (route, name) in new[] { ("queue", "Fila por especialidade da UBS"), ("slots", "Vagas da UBS") })
                {
                    window.ViewModel.Navigate(route); Settle(vm);
                    var content = Layout(window, 980, 640);
                    var grid = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == name);
                    Require(grid.IsReadOnly && grid.ClipboardCopyMode == DataGridClipboardCopyMode.None, "A listagem permite editar/copiar texto da linha: " + name);
                    Require(grid.Items.Count > 0, "Cenário não publicou registros na listagem: " + name);
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                    Require(row != null && row.Margin.Bottom == 3, "Item não preserva intervalo compacto de 3px: " + name);
                    Require(Descendants<Border>(row!).Any(border => border.CornerRadius == new CornerRadius(8) && border.ActualHeight > 0), "Item não apresenta raio discreto de 8px: " + name);
                    Require(!Descendants<Button>(row!).Any(button => AutomationProperties.GetName(button).StartsWith("Copiar", StringComparison.Ordinal)), "Linha ganhou cópia que deveria ficar somente nos detalhes: " + name);
                }
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Listagens OBS: itens selecionados mantêm contraste nos três temas", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Contraste");
            fixture.AddWaiting(unit.Id, "52998224725", "Ana Souza", "Cardiologia", DateTimeOffset.UtcNow.AddHours(-1));
            var start = DateTimeOffset.UtcNow.AddDays(1);
            fixture.AddSlot(unit.Id, new UnitLightningSlot(Guid.NewGuid(), Guid.NewGuid(), "Cardiologia", "Dra. Teste", start, start.AddMinutes(30), "open", DateTimeOffset.UtcNow));
            var window = OpenOperational(fixture, storageRoot, "selected-list-contrast");
            try
            {
                var vm = LoginObs(window);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                foreach (var (route, name, cell) in new[] { ("queue", "Fila por especialidade da UBS", "Ana Souza"), ("slots", "Vagas da UBS", "Dra. Teste") })
                {
                    window.ViewModel.SetAppearance(mode); window.ViewModel.Navigate(route); Settle(vm);
                    var content = Layout(window, 980, 640);
                    var grid = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == name);
                    grid.SelectedItem = grid.Items[0]; Layout(window, 980, 640);
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                    var text = Descendants<TextBlock>(row).SingleOrDefault(text => text.Text == cell) ?? throw new InvalidOperationException("Texto da linha ausente: " + route);
                    Require(((SolidColorBrush)text.Foreground).Color == ((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color, $"{route}/{mode}: texto selecionado não usa o contraste TextBrush.");
                    SavePreview(content, Path.Combine(previewRoot, $"obs-{route}-selected-{mode.ToString().ToLowerInvariant()}.png"));
                }
            }
            finally { window.ViewModel.ConfirmClose = false; window.Close(); }
        });
        Check("Popup paciente da UBS: quatro cópias nativas, rolagem/conteúdo longo e fechamento nos três temas", () =>
        {
            using var fixture = new ApiFixture();
            var unit = fixture.AddUnit("UBS Ficha");
            var patient = fixture.AddRegistry(unit.Id, new UnitPatientRecord("52998224725", "Paciente Fixture " + new string('N', 144), new string('e', 64) + "@" + new string('d', 60) + "." + new string('f', 60) + "." + new string('g', 62) + ".test", "(48) 99900-0123", null, true, true, DateTimeOffset.Parse("2026-10-12T12:00:00Z")));
            var shell = OpenOperational(fixture, storageRoot, "detail-themes");
            try
            {
                var workspace = LoginObs(shell);
                Layout(shell, 980, 640);
                var record = workspace.ObsRegistry.Single();
                var details = workspace.BuildDetails(record)!;
                Require(details.Fields.Count(field => field.CanCopy) == 4, "Paciente não oferece as quatro cópias explícitas.");
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                {
                    shell.ViewModel.SetAppearance(mode);
                    var clipboard = new ClipboardFixture();
                    var popup = new ItemDetailsWindow(details, clipboard) { Owner = shell, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                    try
                    {
                        popup.Show(); popup.UpdateLayout();
                        var frame = Descendants<Border>(popup).Single(border => AutomationProperties.GetName(border) == "Ficha de detalhes");
                        Require(frame.CornerRadius.TopLeft > 8 && popup.ActualWidth <= shell.ActualWidth && popup.ActualHeight <= shell.ActualHeight, "Popup não preserva cantos mais arredondados ou excede a janela mínima.");
                        Require(((SolidColorBrush)frame.Background).Color == ((SolidColorBrush)Application.Current.FindResource("SurfaceBrush")).Color, "Superfície do popup não acompanha o tema.");
                        var fieldsControl = Descendants<ItemsControl>(popup).Single();
                        foreach (var field in details.Fields)
                        {
                            var valueText = Descendants<TextBlock>(fieldsControl).Single(text => text.Text == field.Value);
                            Require(((SolidColorBrush)valueText.Foreground).Color == ((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color, $"{mode}: valor {field.Label} herdou cor fora do tema no popup.");
                        }
                        var copies = Descendants<Button>(popup).Where(button => button.IsVisible && AutomationProperties.GetName(button).StartsWith("Copiar ", StringComparison.Ordinal)).ToArray();
                        Require(copies.Length == 4, "Popup não apresenta quatro botões de cópia.");
                        foreach (var copy in copies)
                        {
                            Require(copy.CommandParameter is DetailField, "Botão copiar não identifica o campo.");
                            copy.BringIntoView(); popup.UpdateLayout();
                            InvokeButton(copy);
                        }
                        Require(clipboard.Values.SequenceEqual(details.Fields.Where(field => field.CanCopy).Select(field => field.Value)), "Invocação dos ícones não copiou os quatro valores completos.");
                        Require(Descendants<ScrollViewer>(popup).Single().HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "Popup exige rolagem horizontal para os dados.");
                        Descendants<ScrollViewer>(popup).Single().ScrollToTop(); popup.UpdateLayout();
                        SavePreview(popup, Path.Combine(previewRoot, $"obs-patient-details-{mode.ToString().ToLowerInvariant()}.png"));
                        var close = Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Fechar detalhes");
                        InvokeButton(close);
                        Require(!popup.IsVisible, "X não fechou o popup.");
                    }
                    finally { if (popup.IsVisible) popup.Close(); }
                }
                var missingPhone = workspace.BuildDetails(record with { Phone = null });
                Require(missingPhone == null, "Ficha foi montada para um registro que não está na listagem atual.");
                shell.ViewModel.SetAppearance(Appearance.Light);
                // Ficha com telefone ausente: o texto substituto nunca é copiável.
                var withoutPhone = fixture.AddRegistry(unit.Id, "Paciente Sem Telefone", "11144477735", "sem@example.test");
                Pump(workspace.LoadObsRegistryAsync());
                var noPhoneDetails = workspace.BuildDetails(workspace.ObsRegistry.Single(item => item.Cpf == withoutPhone.Cpf))!;
                Require(!noPhoneDetails.Fields.Single(field => field.Label == "Telefone").CanCopy, "Telefone não informado ganhou cópia de texto substituto.");
                var usualPopup = new ItemDetailsWindow(workspace.BuildDetails(workspace.ObsRegistry.Single(item => item.Cpf == withoutPhone.Cpf))!, new ClipboardFixture()) { Owner = shell, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                try
                {
                    usualPopup.Show(); usualPopup.UpdateLayout();
                    SavePreview(usualPopup, Path.Combine(previewRoot, "obs-patient-details-usual-light.png"));
                }
                finally { usualPopup.Close(); }
                using var municipalApi = new NoraApiClient(new ApiFixture());
                var manager = new Nora.Desktop.ViewModels.WorkspaceViewModel(DesktopProfile.Municipal, municipalApi);
                try { Require(manager.BuildDetails(record) == null, "Painel de totais recebeu ficha nominal de paciente."); }
                finally { manager.Dispose(); }
            }
            finally { shell.ViewModel.ConfirmClose = false; shell.Close(); }
        });
    }
    private static void AssertModalDetails(ShellWindow window, ItemDetails expected, Action open, bool useEscape)
    {
        var observed = false;
        string? error = null;
        // A ficha da Vaga Relâmpago abre depois de ler as ofertas: o inspetor tenta de novo até a janela existir (ou desiste).
        var attempts = 0;
        void Inspect()
        {
            var popup = window.OwnedWindows.OfType<ItemDetailsWindow>().SingleOrDefault();
            if (popup == null)
            {
                if (++attempts < 3000) window.Dispatcher.BeginInvoke(Inspect, DispatcherPriority.ApplicationIdle);
                else error = "Duplo clique/Enter não abriu popup de detalhes.";
                return;
            }
            observed = true;
            try
            {
                Require(popup.DataContext is ItemDetailsViewModel vm && vm.Details.Title == expected.Title && vm.Details.Subtitle == expected.Subtitle && vm.Details.Fields.SequenceEqual(expected.Fields), "Popup abriu dados de outro item.");
                if (useEscape)
                {
                    AccessKeyManager.ProcessKey(PresentationSource.FromVisual(popup)!, "\u001b", false);
                    Require(!popup.IsVisible, "Escape não fechou os detalhes.");
                }
                else InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Fechar detalhes"));
            }
            catch (Exception failure) { error = failure.Message; }
            finally { if (popup.IsVisible) popup.Close(); }
        }
        window.Dispatcher.BeginInvoke(Inspect, DispatcherPriority.ApplicationIdle);
        open();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!observed && error == null) PumpUntil(() => observed || error != null);
        Require(observed && error == null, error ?? "Nenhuma ficha foi aberta para o item selecionado.");
    }
    private sealed class ClipboardFixture : IClipboardService
    {
        public List<string> Values { get; } = [];
        public bool Fail { get; set; }
        public void SetText(string text)
        {
            if (Fail) throw new ExternalException("synthetic-clipboard-unavailable");
            Values.Add(text);
        }
    }
}
