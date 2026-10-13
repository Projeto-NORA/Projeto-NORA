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
        var previewArgument = Array.IndexOf(args, "--preview-dir");
        var previewRoot = previewArgument >= 0 && previewArgument + 1 < args.Length ? args[previewArgument + 1] : Path.Combine(AppContext.BaseDirectory, "previews");
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
        Check("Listagens OBS: pacientes, fila e vagas compartilham itens arredondados e não copiam a linha", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "listing-pattern"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                foreach (var (route, name) in new[] { ("patients", "Pacientes cadastrados"), ("queue", "Fila de Vaga Relâmpago"), ("slots", "Vagas abertas") })
                {
                    window.ViewModel.Navigate(route);
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
            finally { window.Close(); }
        });
        Check("Paciente: e-mail ocupa até metade da linha e preserva CPF/telefone visíveis", () =>
        {
            using var fixture = new ApiFixture();
            var patient = new PatientAccount(Guid.NewGuid(), "Paciente com nome longo " + new string('N', 120), "52998224725", new string('a', 64) + "@" + new string('b', 60) + "." + new string('c', 60) + "." + new string('d', 62) + ".test", "(48) 99900-0123", true);
            fixture.AddPatient(patient);
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "contact-columns"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                window.ViewModel.Navigate("patients");
                var content = Layout(window, 980, 640);
                var grid = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Pacientes cadastrados");
                grid.ScrollIntoView(patient);
                Layout(window, 980, 640);
                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(patient);
                Require(row != null, "Paciente longo não foi renderizado.");
                var email = Descendants<TextBlock>(row!).SingleOrDefault(text => text.Text == patient.Email);
                Require(email != null && email.TextWrapping == TextWrapping.NoWrap && email.TextTrimming == TextTrimming.CharacterEllipsis && email.ActualWidth <= row!.ActualWidth / 2 + 1, "E-mail longo não tem limite de metade da linha e reticências sem quebra.");
                var cpf = Descendants<TextBlock>(row!).SingleOrDefault(text => text.Text.Contains("529.982.247-25", StringComparison.Ordinal));
                var phone = Descendants<TextBlock>(row!).SingleOrDefault(text => text.Text.Contains(patient.Phone!, StringComparison.Ordinal));
                Require(cpf != null && phone != null && cpf.ActualWidth > 0 && phone.ActualWidth > 0, "CPF completo ou telefone desapareceram depois do e-mail longo.");
                Require(cpf!.TextTrimming == TextTrimming.None && phone!.TextTrimming == TextTrimming.None, "CPF/telefone recebem reticências indevidas.");
                var name = Descendants<TextBlock>(row!).Single(text => text.Text == patient.FullName);
                Require(name.TextTrimming == TextTrimming.CharacterEllipsis && !Descendants<TextBox>(row!).Any(), "Nome longo não permanece texto de leitura com reticências.");
            }
            finally { window.Close(); }
        });
        Check("Listagens: nomes selecionados mantêm contraste nos três temas", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "selected-list-contrast"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.System })
                foreach (var (route, name) in new[] { ("patients", "Pacientes cadastrados"), ("queue", "Fila de Vaga Relâmpago"), ("slots", "Vagas abertas") })
                {
                    window.ViewModel.SetAppearance(mode); window.ViewModel.Navigate(route);
                    var content = Layout(window, 980, 640);
                    var grid = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == name);
                    grid.SelectedItem = grid.Items[0]; Layout(window, 980, 640);
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                    var title = vm.BuildDetails(grid.SelectedItem!)!.Title;
                    var text = Descendants<TextBlock>(row).SingleOrDefault(text => text.Text == title) ?? throw new InvalidOperationException("Nome/descrição da linha ausente: " + route);
                    Require(((SolidColorBrush)text.Foreground).Color == ((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color, $"{route}/{mode}: nome selecionado não usa o contraste TextBrush.");
                    SavePreview(content, Path.Combine(previewRoot, $"obs-{route}-selected-{mode.ToString().ToLowerInvariant()}.png"));
                }
            }
            finally { window.Close(); }
        });
        Check("Listagens: seleção mostra ações em sobreposição; duplo clique e Enter abrem o item correto", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "listing-interactions"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                PrepareWindow(window);
                window.ViewModel.SetAppearance(Appearance.Light);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password"));
                foreach (var (route, name) in new[] { ("patients", "Pacientes cadastrados"), ("queue", "Fila de Vaga Relâmpago"), ("slots", "Vagas abertas") })
                {
                    window.ViewModel.Navigate(route);
                    var content = Layout(window, 980, 640);
                    var grid = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == name);
                    var overlay = Descendants<Border>(content).Single(border => AutomationProperties.GetName(border) == "Ações do item selecionado");
                    var scrolling = (ScrollViewer)window.FindName("PageScroll");
                    Require(overlay.Visibility == Visibility.Collapsed, "Barra vazia permanece na tela: " + route);
                    var viewportHeight = scrolling.ViewportHeight;
                    var gridHeight = grid.ActualHeight;
                    var position = grid.TranslatePoint(new Point(0, 0), content);
                    var item = grid.Items[0];
                    var expected = vm.BuildDetails(item) ?? throw new InvalidOperationException("Tipo da listagem sem detalhes: " + route);
                    grid.SelectedItem = item;
                    Layout(window, 980, 640);
                    Require(overlay.IsVisible && overlay.VerticalAlignment == VerticalAlignment.Bottom, "Clique/seleção não abriu ações na parte inferior: " + route);
                    Require(scrolling.ViewportHeight == viewportHeight && grid.ActualHeight == gridHeight && grid.TranslatePoint(new Point(0, 0), content) == position, "Barra empurrou a listagem ou reservou faixa de espaço: " + route);
                    Require(window.OwnedWindows.Count == 0, "Seleção simples já abriu detalhes: " + route);
                    Pump(vm.RefreshAutomaticallyAsync());
                    Layout(window, 980, 640);
                    Require(overlay.IsVisible && grid.SelectedItem != null && vm.ActionSelectionLabel == expected.Title, "Atualização sem mudanças perdeu a seleção/barra: " + route);
                    SavePreview(content, Path.Combine(previewRoot, $"obs-{route}-selected-actions-light.png"));
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
                    AssertModalDetails(window, expected, () => grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = row }), useEscape: false);
                    AssertModalDetails(window, expected, () => grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent }), useEscape: true);
                    var clear = Descendants<Button>(overlay).Single(button => AutomationProperties.GetName(button) == "Desfazer seleção");
                    InvokeButton(clear);
                    Layout(window, 980, 640);
                    Require(overlay.Visibility == Visibility.Collapsed && grid.SelectedItem == null, "Desfazer seleção deixou barra ou item selecionado: " + route);
                }
            }
            finally { window.Close(); }
        });
        Check("Popup paciente: quatro cópias nativas, rolagem/conteúdo longo e fechamento nos três temas", () =>
        {
            using var fixture = new ApiFixture();
            var shell = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "detail-themes"), new NoraApiClient(fixture));
            var patient = new PatientAccount(Guid.NewGuid(), "Paciente Fixture " + new string('N', 144), "52998224725", new string('e', 64) + "@" + new string('d', 60) + "." + new string('f', 60) + "." + new string('g', 62) + ".test", "(48) 99900-0123", true, true, DateTimeOffset.Parse("2026-10-12T12:00:00Z"));
            try
            {
                shell.ViewModel.ConfirmClose = false;
                PrepareWindow(shell);
                shell.ViewModel.Workspace.Endpoint = "https://fixture.invalid"; shell.ViewModel.Workspace.Identifier = "fixture@example.invalid";
                Pump(shell.ViewModel.Workspace.LoginAsync("isolated-test-password"));
                Layout(shell, 980, 640);
                var details = shell.ViewModel.Workspace.BuildDetails(patient)!;
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
                var missingPhone = shell.ViewModel.Workspace.BuildDetails(patient with { Phone = null })!;
                Require(!missingPhone.Fields.Single(field => field.Label == "Telefone").CanCopy, "Telefone não informado ganhou cópia de texto substituto.");
                shell.ViewModel.SetAppearance(Appearance.Light);
                var usualDetails = shell.ViewModel.Workspace.BuildDetails(patient with { FullName = "Paciente de Demonstração", Email = "paciente@example.invalid" })!;
                var usualPopup = new ItemDetailsWindow(usualDetails, new ClipboardFixture()) { Owner = shell, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
                try
                {
                    usualPopup.Show(); usualPopup.UpdateLayout();
                    SavePreview(usualPopup, Path.Combine(previewRoot, "obs-patient-details-usual-light.png"));
                }
                finally { usualPopup.Close(); }
                using var municipalApi = new NoraApiClient(new ApiFixture());
                var manager = new Nora.Desktop.ViewModels.WorkspaceViewModel(DesktopProfile.Municipal, municipalApi);
                try { Require(manager.BuildDetails(patient) == null, "Painel de totais recebeu ficha nominal de paciente."); }
                finally { manager.Dispose(); }
            }
            finally { shell.Close(); }
        });
        Check("OBS: administrador e atendente confirmam desativar/reativar; cancelar não altera conta", () =>
        {
            foreach (var role in new[] { "administrator", "attendant" })
            {
                using var fixture = new ApiFixture { ObsRole = role, PatientOnline = true };
                var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "patient-active-action", role), new NoraApiClient(fixture));
                try
                {
                    window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                    PrepareWindow(window);
                    var vm = window.ViewModel.Workspace;
                    vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                    Pump(vm.LoginAsync("isolated-test-password"));
                    window.ViewModel.Navigate("patients");
                    var content = Layout(window, 980, 640);
                    var table = Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Pacientes cadastrados");
                    table.SelectedItem = vm.Patients.Single(); Layout(window, 980, 640);
                    var action = Descendants<Button>(content).Single(button => AutomationProperties.GetName(button) == "Alterar status da conta do paciente");
                    Require(action.IsEnabled && action.Content?.ToString() == "Desativar paciente", "Perfil não recebeu a ação real para conta ativa: " + role);
                    AssertPatientAccessConfirmation(window, action, "Desativar", confirm: false);
                    Require(fixture.ActiveChanges.Count == 0 && vm.Patients.Single().IsActive, "Cancelar confirmou desativação: " + role);
                    AssertPatientAccessConfirmation(window, action, "Desativar", confirm: true);
                    PumpUntil(() => !vm.IsBusy); Layout(window, 980, 640);
                    Require(fixture.ActiveChanges.SequenceEqual(new[] { false }) && !vm.Patients.Single().IsActive && !vm.Patients.Single().IsOnline, "Desativação não publicou estado correto da conta/presença: " + role);
                    table.SelectedItem = vm.Patients.Single(); Layout(window, 980, 640);
                    Require(action.IsEnabled && action.Content?.ToString() == "Reativar paciente", "Conta desativada não oferece reativar: " + role);
                    AssertPatientAccessConfirmation(window, action, "Reativar", confirm: true);
                    PumpUntil(() => !vm.IsBusy); Layout(window, 980, 640);
                    Require(fixture.ActiveChanges.SequenceEqual(new[] { false, true }) && vm.Patients.Single().IsActive, "Reativação não enviou o mesmo contrato/identidade: " + role);
                    table.SelectedItem = vm.Patients.Single(); Layout(window, 980, 640);
                    vm.PatientStatusFilter = "Desativados"; Layout(window, 980, 640);
                    Require(!vm.FilteredPatients.Any() && table.SelectedItem == null && !vm.HasPatientActionSelection, "Filtro de conta manteve ação/seleção de paciente fora do resultado: " + role);
                }
                finally { window.Close(); }
            }
        });
        Check("OBS: confirmação de status aguarda polling pendente antes de autorizar a alteração", () =>
        {
            using var fixture = new ApiFixture();
            var window = new ShellWindow(DesktopProfile.Operational, Path.Combine(storageRoot, "confirm-during-poll"), new NoraApiClient(fixture));
            try
            {
                window.ViewModel.ConfirmClose = false; window.ViewModel.Animations = false;
                PrepareWindow(window);
                var vm = window.ViewModel.Workspace;
                vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
                Pump(vm.LoginAsync("isolated-test-password")); window.ViewModel.Navigate("patients");
                var content = Layout(window, 980, 640);
                Descendants<DataGrid>(content).Single(grid => AutomationProperties.GetName(grid) == "Pacientes cadastrados").SelectedItem = vm.Patients.Single();
                Layout(window, 980, 640);
                var action = Descendants<Button>(content).Single(button => AutomationProperties.GetName(button) == "Alterar status da conta do paciente");
                string? error = null;
                var observed = false;
                window.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().Single();
                    Task? refresh = null;
                    observed = true;
                    try
                    {
                        var confirm = Descendants<Button>(dialog).Single(button => button.Content?.ToString() == "Desativar paciente");
                        Require(confirm.IsEnabled, "Confirmação começou indisponível sem operação pendente.");
                        fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        refresh = vm.RefreshAutomaticallyAsync();
                        PumpUntil(() => vm.IsBusy); dialog.UpdateLayout();
                        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        Require(!confirm.IsEnabled, "Confirmação permanece habilitada durante polling e pode descartar silenciosamente a alteração aprovada.");
                        fixture.PauseReads.TrySetResult(); Pump(refresh); dialog.UpdateLayout();
                        Require(confirm.IsEnabled, "Confirmação não voltou após atualização concluída.");
                        InvokeButton(confirm);
                    }
                    catch (Exception failure) { error = failure.Message; }
                    finally
                    {
                        fixture.PauseReads?.TrySetResult();
                        try { if (refresh != null) Pump(refresh); }
                        catch (Exception cleanupFailure) { error ??= "Limpeza do polling: " + cleanupFailure.Message; }
                        if (dialog.IsVisible) dialog.DialogResult = false;
                    }
                }, DispatcherPriority.ApplicationIdle);
                InvokeButton(action);
                Require(observed && error == null, error ?? "Confirmação não foi aberta.");
                PumpUntil(() => !vm.IsBusy);
                Require(fixture.ActiveChanges.SequenceEqual(new[] { false }), "Confirmação após polling não publicou a alteração autorizada.");
            }
            finally { window.Close(); }
        });
    }
    private static void AssertPatientAccessConfirmation(ShellWindow window, Button action, string operation, bool confirm)
    {
        var observed = false;
        string? error = null;
        window.Dispatcher.BeginInvoke(() =>
        {
            var dialog = window.OwnedWindows.Cast<Window>().SingleOrDefault();
            if (dialog == null) { error = "Ação alterou conta sem a confirmação nativa."; return; }
            observed = true;
            try
            {
                Require(dialog.Title == operation + " paciente", "Confirmação abriu outra operação.");
                Require(Descendants<TextBlock>(dialog).Any(text => operation == "Desativar" ? text.Text.Contains("sessões", StringComparison.Ordinal) && text.Text.Contains("histórico", StringComparison.Ordinal) : text.Text.Contains("não retorna automaticamente", StringComparison.Ordinal)), "Confirmação não explica sessões/histórico/fila.");
                InvokeButton(Descendants<Button>(dialog).Single(button => button.Content?.ToString() == (confirm ? operation + " paciente" : "Cancelar")));
            }
            catch (Exception failure) { error = failure.Message; }
            finally { if (dialog.IsVisible) dialog.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        InvokeButton(action);
        Require(observed && error == null, error ?? "Nenhuma confirmação foi apresentada.");
    }
    private static void AssertModalDetails(ShellWindow window, ItemDetails expected, Action open, bool useEscape)
    {
        var observed = false;
        string? error = null;
        window.Dispatcher.BeginInvoke(() =>
        {
            var popup = window.OwnedWindows.OfType<ItemDetailsWindow>().SingleOrDefault();
            if (popup == null) { error = "Duplo clique/Enter não abriu popup de detalhes."; return; }
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
        }, DispatcherPriority.ApplicationIdle);
        open();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
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
