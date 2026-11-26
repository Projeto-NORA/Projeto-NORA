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
/// Redesenho de Pacientes (listagem e cadastro), Família e Vínculos aprovados (OBS): listagem aberta na tela inteira com balão de ações, cadastro em grade, Corrigir e-mail em
/// pop-up, ficha em linhas, menu com "Vínculos aprovados" e rotas antigas, estados vazios e seleção que sobrevive ao ciclo de 20 s. Gera as capturas em
/// <c>--preview-dir</c> (claro e escuro, com dados e vazio) para conferência visual.
/// </summary>
internal static partial class Program
{
    private static readonly string[] PatientNames =
    [
        "Ana Beatriz Souza", "Bruno Henrique Lima", "Carla Menezes", "Daniel Ferreira Costa", "Eduarda Martins", "Fábio Nogueira",
        "Gabriela Torres Alves", "Heitor Ramos", "Isabela Cardoso", "João Pedro Barbosa", "Karen Vieira", "Lucas Andrade Pinto"
    ];

    private static UnitPatientRecord[] SeedPatients(ApiFixture fixture, Guid unitId)
    {
        var now = DateTimeOffset.UtcNow;
        return PatientNames.Select((name, index) => fixture.AddRegistry(unitId, new UnitPatientRecord($"{index + 1:00000000000}", name,
            $"{name.Split(' ')[0].ToLowerInvariant()}@example.test", index % 3 == 1 ? null : $"(48) 99{index:000}-{index + 10:0000}", null, index != 4 && index != 9, index % 2 == 0, now.AddMinutes(-index)))).ToArray();
    }

    private static void RunPatientsFamilyChecks(string storageRoot, string previewRoot)
    {
        var application = Application.Current;
        var cardStyle = (Style)application.FindResource("Card");
        Check("OBS Pacientes (listagem): lista aberta na tela inteira, itens com nome e CPF | e-mail | telefone, etiqueta Desativado, filtros no botão Filtros, balão de ações e estados vazios", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Redesenho", "Centro");
            var patients = SeedPatients(fixture, unit.Id);
            var window = OpenOperational(fixture, storageRoot, "pf-list", Appearance.Light);
            try
            {
                var vm = LoginObs(window); UnitPatientRecord Pick(int index) => vm.ObsRegistry.First(patient => patient.Cpf == patients[index].Cpf);
                var content = Show(window, "patients:list", 1280, 760);
                Require(window.ViewModel.PageLabel == "Listagem • Pacientes", "O título deveria ser \"Listagem • Pacientes\": " + window.ViewModel.PageLabel);
                var page = (FrameworkElement)window.FindName("PageContainer");
                var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Cadastros de referência da UBS");
                var scroll = (ScrollViewer)window.FindName("PageScroll");
                Require(list.Items.Count == 12 && scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled && Math.Abs(list.ActualHeight - (scroll.ViewportHeight - 48)) < 2, $"A listagem deveria ocupar a tela inteira ({list.ActualHeight:F0} de {scroll.ViewportHeight - 48:F0}) com {list.Items.Count} itens.");
                // Sem título repetido, sem botão de cadastro nem de atualizar, sem card atrás da lista.
                var texts = VisibleTexts(page);
                Require(!texts.Any(text => text is "Pacientes" or "Listagem") && !Descendants<Button>(page).Any(button => button.IsVisible && (button.Content?.ToString() == "+ Cadastrar paciente" || AutomationProperties.GetName(button).StartsWith("Atualizar", StringComparison.Ordinal))), "A listagem não deveria repetir o título nem trazer \"Cadastrar paciente\" ou \"Atualizar\": " + string.Join(" | ", texts));
                Require(!Descendants<Border>(page).Any(border => border.IsVisible && ReferenceEquals(border.Style, cardStyle)) && !Descendants<DataGrid>(page).Any(grid => grid.IsVisible), "A listagem não deveria ter card nem tabela.");
                // O item: nome; embaixo só o que foi informado (CPF | e-mail | telefone), sem ícone de pessoa; a etiqueta só no desativado.
                Require(texts.Contains("Ana Beatriz Souza") && texts.Contains("000.000.000-01 | ana@example.test | (48) 99000-0010") && texts.Contains("000.000.000-02 | bruno@example.test"), "O item deveria trazer \"CPF | e-mail | telefone\" só com o que foi informado: " + string.Join(" | ", texts));
                Require(texts.Contains("Desativado") && Descendants<StatusTag>(list).Count(tag => tag.IsVisible && tag.Text == "Desativado") == 2, "Só os cadastros desativados levam a etiqueta \"Desativado\".");
                Require(!Descendants<TextBlock>(list).Any(text => text.Text.Contains('')) && !texts.Any(text => text.StartsWith("Score", StringComparison.Ordinal) || text.StartsWith("Online", StringComparison.Ordinal)), "O item não deveria trazer ícone de pessoa, score nem presença.");
                Require(new UnitPatientRecord("52998224725", "Sem e-mail", "", null).MetaLine == "529.982.247-25" && new UnitPatientRecord("52998224725", "Só telefone", "", "48999990000").MetaLine == "529.982.247-25 | 48999990000", "A linha de dados deveria omitir o que não foi informado.");
                // Filtros (busca, situação, presença, ordenação) no botão Filtros.
                var filters = OpenPageFilters(window);
                Require(Descendants<TextBox>(filters).Any(box => AutomationProperties.GetName(box) == "Pesquisar cadastro da UBS") && Descendants<ComboBox>(filters).Select(AutomationProperties.GetName).OrderBy(name => name).SequenceEqual(["Filtrar presença na UBS", "Filtrar situação na UBS", "Ordenar cadastro da UBS"]), "Os filtros (busca, situação, presença e ordenação) deveriam estar no botão Filtros.");
                ((System.Windows.Controls.Primitives.Popup)window.FindName("FiltersPopup")).IsOpen = false;
                // Balão: aparece com o item selecionado e traz as ações do paciente.
                var balloon = Descendants<SelectionBalloon>(content).Single();
                Require(!balloon.IsVisible, "Sem item selecionado o balão não deveria aparecer.");
                string[] actions = ["Ver ficha", "Editar ficha", "Corrigir e-mail", "Desativar nesta UBS", "Reativar nesta UBS"];
                string[] VisibleActions() => Descendants<Button>(balloon).Where(button => button.IsVisible && actions.Contains(AutomationProperties.GetName(button))).Select(AutomationProperties.GetName).ToArray();
                vm.SelectedObsRegistry = Pick(0); content = Layout(window, 1280, 760);
                Require(balloon.IsVisible && balloon.Label == "Ana Beatriz Souza" && VisibleActions().SequenceEqual(["Ver ficha", "Editar ficha", "Corrigir e-mail", "Desativar nesta UBS"]), "Com um paciente ativo o balão deveria trazer Ver ficha, Editar ficha, Corrigir e-mail e Desativar: " + string.Join(", ", VisibleActions()));
                Require(ButtonIn(content, "Desativar nesta UBS").Style == (Style)application.FindResource("ListActionDangerButton"), "Desativar deveria ser o botão de perigo (lixeira branca em fundo vermelho).");
                SavePreview(content, Path.Combine(previewRoot, "pacientes-listagem-balao-claro.png"));
                vm.SelectedObsRegistry = Pick(4); content = Layout(window, 1280, 760);
                Require(balloon.IsVisible && VisibleActions().SequenceEqual(["Ver ficha", "Editar ficha", "Corrigir e-mail", "Reativar nesta UBS"]), "Com um paciente desativado o balão deveria trazer Reativar no lugar de Desativar: " + string.Join(", ", VisibleActions()));
                // Vários selecionados: o que não serve a vários some.
                list.SelectedItems.Clear(); list.SelectedItems.Add(Pick(0)); list.SelectedItems.Add(Pick(2)); content = Layout(window, 1280, 760);
                Require(balloon.IsVisible && balloon.Label == "2 pacientes selecionados" && VisibleActions().Length == 0, "Com dois pacientes o balão deveria mostrar só \"2 pacientes selecionados\": " + balloon.Label + " / " + string.Join(", ", VisibleActions()));
                // O ciclo de 20 s recria a lista e a seleção (e o balão) continuam, sem piscar.
                list.SelectedItems.Clear(); vm.SelectedObsRegistry = Pick(2); content = Layout(window, 1280, 760);
                var flips = 0; balloon.IsVisibleChanged += (_, _) => flips++;
                fixture.AddRegistry(unit.Id, "Mariana Novaes", "98765432100");
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm); content = Layout(window, 1280, 760);
                Require(list.Items.Count == 13 && vm.SelectedObsRegistry?.Cpf == patients[2].Cpf && balloon.IsVisible && flips == 0 && ListingBehavior.GetSelectedItems<UnitPatientRecord>(list).Single().Cpf == patients[2].Cpf, $"A seleção e o balão deveriam sobreviver à releitura sem piscar (piscadas: {flips}).");
                list.UnselectAll(); Layout(window, 1280, 760);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    window.ViewModel.SetAppearance(mode); content = Layout(window, 1280, 760);
                    list.SelectedItem = null; vm.SelectedObsRegistry = Pick(1); content = Layout(window, 1280, 760);
                    SavePreview(content, Path.Combine(previewRoot, $"pacientes-listagem-selecionado-{(mode == Appearance.Light ? "claro" : "escuro")}.png"));
                    vm.SelectedObsRegistry = null; content = Layout(window, 1280, 760);
                    SavePreview(content, Path.Combine(previewRoot, $"pacientes-listagem-{(mode == Appearance.Light ? "claro" : "escuro")}.png"));
                }
                // Largura mínima da janela (980).
                window.ViewModel.SetAppearance(Appearance.Light); vm.SelectedObsRegistry = Pick(0); content = Layout(window, 980, 640);
                Require(list.ActualWidth <= content.ActualWidth && balloon.IsVisible, "Em 980 de largura a listagem e o balão deveriam caber.");
                SavePreview(content, Path.Combine(previewRoot, "pacientes-listagem-980-claro.png"));
                // Busca sem resultado e depois sem nenhum cadastro: textos diferentes.
                vm.SelectedObsRegistry = null; vm.ObsRegistrySearch = "zzz-sem-resultado"; content = Layout(window, 1280, 760);
                Require(VisibleTexts(page).Contains("Nenhum paciente encontrado para esta busca.") && !VisibleTexts(page).Contains("Nenhum paciente cadastrado nesta UBS."), "A busca sem resultado deveria dizer \"encontrado para esta busca\": " + string.Join(" | ", VisibleTexts(page)));
                SavePreview(content, Path.Combine(previewRoot, "pacientes-listagem-busca-vazia-claro.png"));
                vm.ObsRegistrySearch = "";
            }
            finally { CloseWindow(window); }
            using var emptyFixture = new ApiFixture { UseWorkspaceIdentity = true };
            emptyFixture.AddUnit("UBS Sem Cadastros", "Centro");
            var emptyWindow = OpenOperational(emptyFixture, storageRoot, "pf-list-empty", Appearance.Light);
            try
            {
                var vm = LoginObs(emptyWindow);
                var content = Show(emptyWindow, "patients:list", 1280, 760);
                var page = (FrameworkElement)emptyWindow.FindName("PageContainer");
                Require(VisibleTexts(page).Contains("Nenhum paciente cadastrado nesta UBS.") && vm.ObsRegistryListState == "Nenhum paciente cadastrado nesta UBS." && !vm.ObsRegistryLoading, "Sem cadastro nenhum a listagem deveria dizer \"Nenhum paciente cadastrado nesta UBS.\" (e não \"Carregando\") depois da leitura.");
                SavePreview(content, Path.Combine(previewRoot, "pacientes-listagem-vazia-claro.png"));
                emptyWindow.ViewModel.SetAppearance(Appearance.Dark); content = Layout(emptyWindow, 1280, 760);
                SavePreview(content, Path.Combine(previewRoot, "pacientes-listagem-vazia-escuro.png"));
            }
            finally { CloseWindow(emptyWindow); }
        });

        Check("OBS Pacientes (cadastro): sem título e sem card, campos em grade (nome, CPF, e-mail e telefone lado a lado), endereço opcional em grade e botão Cadastrar paciente embaixo à direita", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Cadastro", "Centro");
            var window = OpenOperational(fixture, storageRoot, "pf-register", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                foreach (var (width, height, label) in new[] { (1280d, 900d, "1280"), (1366d, 768d, "1366"), (980d, 640d, "980") })
                {
                    var content = Show(window, "patients:register", width, height);
                    var page = (FrameworkElement)window.FindName("PageContainer");
                    Require(window.ViewModel.PageLabel == "Cadastro • Pacientes", "O título deveria ser \"Cadastro • Pacientes\": " + window.ViewModel.PageLabel);
                    var texts = VisibleTexts(page);
                    Require(!texts.Contains("Pacientes") && !texts.Contains("Cadastro") && texts.Contains("Cadastrar referência na UBS") && texts.Contains("Endereço (opcional)"), "O cadastro não deveria repetir o título da tela: " + string.Join(" | ", texts));
                    Require(!Descendants<Border>(page).Any(border => border.IsVisible && ReferenceEquals(border.Style, cardStyle)), "O cadastro não deveria ter card.");
                    var grids = Descendants<FormGrid>(page).ToArray();
                    Require(grids.Length == 2, "O cadastro deveria ter duas grades (referência e endereço): " + grids.Length);
                    Point At(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), page);
                    var name = Input(content, "Nome no cadastro da UBS"); var cpf = Input(content, "CPF no cadastro da UBS"); var email = Input(content, "E-mail no cadastro da UBS");
                    Require(Math.Abs(At(name).Y - At(cpf).Y) < 1 && At(cpf).X > At(name).X + 200 && Math.Abs(name.ActualWidth - cpf.ActualWidth) < 1, $"Nome e CPF deveriam ficar lado a lado, com a mesma largura (nome {At(name)} / CPF {At(cpf)}).");
                    var columns = FormGrid.ColumnsFor(grids[0].ActualWidth, grids[0].MinColumnWidth, grids[0].ColumnSpacing, grids[0].MaxColumns);
                    Require(columns >= 2 && (columns >= 3 ? Math.Abs(At(email).Y - At(cpf).Y) < 1 : At(email).Y > At(name).Y + 40), $"Em {label} de largura a grade (largura {grids[0].ActualWidth:F0}) deveria ter {columns} colunas (nome {At(name)}, CPF {At(cpf)}, e-mail {At(email)}).");
                    Console.WriteLine($"  cadastro em {label}: grade de {grids[0].ActualWidth:F0} px com {columns} colunas");
                    var save = FindActionButton(content, "Cadastrar paciente"); var edge = At(grids[1]).X + grids[1].ActualWidth;
                    Require(Math.Abs(At(save).X + save.ActualWidth - edge) < 2 && At(save).Y > At(grids[1]).Y + grids[1].ActualHeight, $"O botão Cadastrar paciente deveria ficar embaixo, à direita (botão {At(save)} + {save.ActualWidth:F0}, borda {edge:F0}).");
                    Require(!Descendants<Button>(page).Any(button => button.IsVisible && button.Content?.ToString() == "Cancelar edição"), "Sem edição não deveria haver \"Cancelar edição\".");
                    foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                    {
                        window.ViewModel.SetAppearance(mode);
                        SaveScrolled(window, Path.Combine(previewRoot, $"pacientes-cadastro-{label}-{(mode == Appearance.Light ? "claro" : "escuro")}.png"), width, height);
                    }
                    window.ViewModel.SetAppearance(Appearance.Light);
                }
                // O erro do campo aparece junto dele, e o CEP completo continua consultando o endereço sozinho.
                var form = Show(window, "patients:register", 1280, 900);
                Input(form, "E-mail no cadastro da UBS").Text = "ruim"; form = Layout(window, 1280, 900);
                Require(VisibleTexts(form).Any(text => text.StartsWith("E-mail inválido", StringComparison.Ordinal)), "O erro do e-mail deveria aparecer sob o campo.");
                SaveScrolled(window, Path.Combine(previewRoot, "pacientes-cadastro-erro-claro.png"), 1280, 900);
            }
            finally { CloseWindow(window); }
        });

        Check("OBS Pacientes: Corrigir e-mail em pop-up (com a confirmação), Editar ficha leva ao cadastro (com Cancelar edição) e a ficha em linhas abre pelo botão e pelo segundo clique", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Ações", "Centro");
            var patients = SeedPatients(fixture, unit.Id);
            fixture.AddExam(unit.Id, patients[0].Cpf, "Hemograma", "done", new DateOnly(2026, 3, 10));
            var asked = new List<(string Title, string Text)>(); var answer = false;
            Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return answer; };
            var window = OpenOperational(fixture, storageRoot, "pf-actions", Appearance.Light);
            try
            {
                var vm = LoginObs(window); UnitPatientRecord Pick(int index) => vm.ObsRegistry.First(patient => patient.Cpf == patients[index].Cpf);
                var content = Show(window, "patients:list", 1280, 760);
                var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Cadastros de referência da UBS");
                // Corrigir e-mail.
                vm.SelectedObsRegistry = Pick(0); content = Layout(window, 1280, 760);
                InspectModal<FormDialog>(window, dialog =>
                {
                    Require(dialog.Title == "Corrigir e-mail" && Descendants<Button>(dialog).Any(button => button.Content as string == "Corrigir e-mail" && button.IsDefault), "O pop-up deveria se chamar \"Corrigir e-mail\", com o botão primário.");
                    var box = Descendants<TextBox>(dialog).Single(input => AutomationProperties.GetName(input) == "E-mail corrigido da UBS");
                    Require(box.Text == "ana@example.test" && FullTexts(dialog).Contains("Ana Beatriz Souza"), "O pop-up deveria vir com o e-mail atual e o nome do paciente.");
                    var primary = Descendants<Button>(dialog).Single(button => button.Content as string == "Corrigir e-mail");
                    box.Text = "inválido"; dialog.UpdateLayout();
                    Require(FullTexts(dialog).Any(text => text.StartsWith("E-mail inválido", StringComparison.Ordinal)), "O erro do campo deveria aparecer no pop-up.");
                    SavePreview(dialog, Path.Combine(previewRoot, "pacientes-corrigir-email-erro-claro.png"));
                    InvokeButton(primary); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && asked.Count == 0 && fixture.Count("PATCH", "/registry/email") == 0 && dialog.ErrorMessage.StartsWith("E-mail inválido", StringComparison.Ordinal), "E-mail inválido não deveria pedir confirmação nem ser enviado: " + dialog.ErrorMessage);
                    box.Text = "ana.nova@example.test"; answer = false;
                    InvokeButton(primary); PumpUntil(() => !dialog.IsBusy);
                    Require(dialog.IsVisible && asked.Count == 1 && asked[0].Title == "Corrigir e-mail" && asked[0].Text.Contains("Ana Beatriz Souza", StringComparison.Ordinal) && fixture.Count("PATCH", "/registry/email") == 0 && dialog.ErrorMessage.Contains("cancelada", StringComparison.Ordinal), "Cancelar a confirmação deveria manter o pop-up aberto sem corrigir: " + dialog.ErrorMessage);
                    answer = true; asked.Clear();
                    InvokeButton(primary); PumpUntil(() => !dialog.IsVisible && !vm.IsBusy);
                }, () => InvokeButton(ButtonIn(content, "Corrigir e-mail")));
                Require(asked.Count == 1 && fixture.Count("PATCH", "/registry/email") == 1 && vm.ObsRegistry.First(patient => patient.Cpf == patients[0].Cpf).Email == "ana.nova@example.test" && vm.Message == "E-mail atualizado no cadastro da UBS.", "Confirmar deveria corrigir o e-mail e fechar o pop-up: " + vm.Message);
                // Fechar sem corrigir e abrir de novo: o campo volta ao e-mail atual do cadastro, não ao que foi digitado antes.
                vm.SelectedObsRegistry = Pick(0); content = Layout(window, 1280, 760);
                TextBox EmailBox(FormDialog dialog) => Descendants<TextBox>(dialog).Single(input => AutomationProperties.GetName(input) == "E-mail corrigido da UBS");
                InspectModal<FormDialog>(window, dialog => { EmailBox(dialog).Text = "digitado@example.test"; dialog.Close(); }, () => InvokeButton(ButtonIn(content, "Corrigir e-mail")));
                InspectModal<FormDialog>(window, dialog =>
                {
                    Require(EmailBox(dialog).Text == "ana.nova@example.test", "Reaberto, o pop-up deveria trazer o e-mail atual do cadastro: " + EmailBox(dialog).Text);
                    dialog.Close();
                }, () => InvokeButton(ButtonIn(content, "Corrigir e-mail")));
                Require(fixture.Count("PATCH", "/registry/email") == 1, "Fechar o pop-up não deveria corrigir o e-mail.");
                // Editar ficha: abre o cadastro com o paciente e o botão Cancelar edição.
                vm.SelectedObsRegistry = vm.ObsRegistry.First(patient => patient.Cpf == patients[2].Cpf); content = Layout(window, 1280, 760);
                InvokeButton(ButtonIn(content, "Editar ficha")); content = Layout(window, 1280, 900);
                Require(window.ViewModel.Route == "patients:register" && vm.IsEditingObsRegistry && vm.ObsRegistryName == "Carla Menezes" && Input(content, "CPF no cadastro da UBS").IsReadOnly, "Editar ficha deveria abrir o cadastro com o paciente, sem deixar mudar o CPF.");
                var save = FindActionButton(content, "Salvar alterações"); var cancel = FindActionButton(content, "Cancelar edição");
                Require(save.IsVisible && cancel.IsVisible && VisibleTexts(content).Contains("Editar cadastro da UBS"), "Em edição o cadastro deveria trazer \"Salvar alterações\" e \"Cancelar edição\".");
                SaveScrolled(window, Path.Combine(previewRoot, "pacientes-cadastro-edicao-claro.png"), 1280, 900);
                InvokeButton(cancel); content = Layout(window, 1280, 900);
                Require(!vm.IsEditingObsRegistry && vm.ObsRegistryName == "" && FindActionButton(content, "Cadastrar paciente").IsVisible, "Cancelar edição deveria voltar a um cadastro novo, em branco.");
                // Ficha em linhas (com a aba Exames e vacinas): pelo botão Ver ficha e pelo segundo clique na linha.
                content = Show(window, "patients:list", 1280, 760); list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Cadastros de referência da UBS");
                vm.SelectedObsRegistry = vm.ObsRegistry.First(patient => patient.Cpf == patients[0].Cpf); content = Layout(window, 1280, 760);
                OpenFicha(window, () => InvokeButton(ButtonIn(content, "Ver ficha")), popup =>
                {
                    Require(popup.Title == "Ana Beatriz Souza" && Descendants<Button>(popup).Any(button => AutomationProperties.GetName(button) == "Aba Exames e vacinas") && FullTexts(popup).Contains("CPF"), "A ficha deveria abrir em linhas, com a aba Exames e vacinas.");
                    popup.UpdateLayout(); SavePreview(popup, Path.Combine(previewRoot, "pacientes-ficha-claro.png"));
                    window.ViewModel.SetAppearance(Appearance.Dark); popup.UpdateLayout(); SavePreview(popup, Path.Combine(previewRoot, "pacientes-ficha-escuro.png")); window.ViewModel.SetAppearance(Appearance.Light);
                    InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Fechar detalhes"));
                });
                list.UnselectAll(); content = Layout(window, 1280, 760);
                var second = vm.ObsRegistry.First(patient => patient.Cpf == patients[0].Cpf);
                OpenFicha(window, () => { ClickListRow(list, ItemIndex(list, second), 1); ClickListRow(list, ItemIndex(list, second), 2); }, popup =>
                {
                    Require(popup.Title == "Ana Beatriz Souza", "O segundo clique deveria abrir a ficha do paciente selecionado.");
                    InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Fechar detalhes"));
                });
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("OBS Família: menu com Vínculos aprovados, rotas antigas valem, estados vazios, pesquisa sem resultado, seleção e balão no ciclo e ficha em linhas", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Família Layout", "Centro");
            var lia = fixture.AddRegistry(unit.Id, "Lia Santos", "52998224725"); var davi = fixture.AddRegistry(unit.Id, "Davi Souza", "11144477735");
            var now = DateTimeOffset.UtcNow;
            var pending = fixture.AddFamilyRequest(unit.Id, "Maria Santos", "12345678909", lia.Cpf, requestedAt: now.AddDays(-2));
            fixture.AddFamilyRequest(unit.Id, "João Souza", "98765432100", davi.Cpf, requestedAt: now.AddDays(-1));
            var window = OpenOperational(fixture, storageRoot, "pf-family", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var area = window.ViewModel.Navigation.Single(item => item.Id == "patients");
                Require(area.Children.Select(item => item.Title).SequenceEqual(["Listagem", "Cadastro", "Família", "Vínculos aprovados"]) && area.Children.Select(item => item.Route).SequenceEqual(["patients:list", "patients:register", "family:requests", "family:approved"]), "O menu de Pacientes deveria ter Listagem, Cadastro, Família e, abaixo, Vínculos aprovados: " + string.Join(", ", area.Children.Select(item => item.Title + "=" + item.Route)));
                // A rota antiga "family" (atalho e preferência salva) continua abrindo Família.
                window.ViewModel.Navigate("family");
                Require(window.ViewModel.Route == "family:requests" && window.ViewModel.PageLabel == "Família • Pacientes" && !window.ViewModel.ShowsAllSubPages, "O atalho \"family\" deveria abrir Família: " + window.ViewModel.Route);
                window.ViewModel.Navigate("family:approved");
                Require(window.ViewModel.Route == "family:approved" && window.ViewModel.PageLabel == "Vínculos aprovados • Pacientes" && area.Children[3].IsSelected && !area.Children[2].IsSelected, "Vínculos aprovados deveria abrir a própria subtela, marcada no menu.");
                window.ViewModel.GoBack();
                Require(window.ViewModel.Route == "family:requests" && area.Children[2].IsSelected, "Voltar deveria levar a Família.");                // Preferência salva com a rota antiga: reabrir volta para Família.
                var savedPath = Path.Combine(storageRoot, "pf-family-saved");
                Require(new PreferencesService(DesktopProfile.Operational.Id, savedPath).Save(new() { LastPage = "family", Appearance = Appearance.Light }), "A rota não foi salva.");
                var reopened = new ShellWindow(DesktopProfile.Operational, savedPath);
                try { Require(reopened.ViewModel.Route == "family:requests" && reopened.ViewModel.PageLabel == "Família • Pacientes", "A rota salva \"family\" deveria reabrir Família: " + reopened.ViewModel.Route); }
                // O tema é da aplicação inteira: a janela reaberta aplicou o dela, então a janela da checagem volta ao claro antes das capturas.
                finally { reopened.ViewModel.ConfirmClose = false; reopened.Close(); window.ViewModel.SetAppearance(Appearance.Light); }
                // Seleção e balão no ciclo.
                var content = Show(window, "family:requests", 1280, 760);
                var list = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Pedidos de família pendentes");
                var balloon = Descendants<SelectionBalloon>(content).Single(item => item.Name == "PendingBalloon");
                vm.SelectedPendingFamily = vm.FamilyPending[0]; content = Layout(window, 1280, 760);
                var flips = 0; balloon.IsVisibleChanged += (_, _) => flips++;
                fixture.AddFamilyRequest(unit.Id, "Paulo Dias", "52998224725", davi.Cpf, requestedAt: now.AddHours(-3));
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm); content = Layout(window, 1280, 760);
                Require(list.Items.Count == 3 && vm.SelectedPendingFamily?.Id == pending.Id && balloon.IsVisible && flips == 0, $"A seleção e o balão deveriam sobreviver à releitura sem piscar (piscadas: {flips}).");
                // Vários selecionados: Aprovar e Recusar somem (cada pedido pede a conferência dos documentos).
                list.SelectedItems.Clear(); list.SelectedItems.Add(vm.FamilyPending[0]); list.SelectedItems.Add(vm.FamilyPending[1]); content = Layout(window, 1280, 760);
                Require(balloon.IsVisible && balloon.Label == "2 pedidos selecionados" && !Descendants<Button>(balloon).Any(button => button.IsVisible && AutomationProperties.GetName(button) is "Aprovar pedido" or "Recusar pedido"), "Com dois pedidos selecionados o balão deveria mostrar só a contagem.");
                list.UnselectAll(); Layout(window, 1280, 760);
                // Ficha em linhas do pedido (segundo clique): os CPFs para conferir os documentos.
                var first = vm.FamilyPending[0];
                OpenFicha(window, () => { ClickListRow(list, ItemIndex(list, first), 1); ClickListRow(list, ItemIndex(list, first), 2); }, popup =>
                {
                    var fields = FullTexts(popup);
                    Require(popup.Title == "Maria Santos → Lia Santos" && fields.Contains("CPF do responsável") && fields.Contains("123.456.789-09") && fields.Contains("529.982.247-25"), "A ficha do pedido deveria trazer o responsável, o dependente e os dois CPFs: " + string.Join(" | ", fields));
                    popup.UpdateLayout(); SavePreview(popup, Path.Combine(previewRoot, "familia-ficha-claro.png"));
                    InvokeButton(Descendants<Button>(popup).Single(button => AutomationProperties.GetName(button) == "Fechar detalhes"));
                });
                // Pesquisa do cabeçalho sem resultado: "encontrado para esta busca" (diferente de "Nenhum pedido aguardando").
                var search = Descendants<TextBox>(window).Single(box => AutomationProperties.GetName(box) == "Pesquisar: Pedidos de vínculo");
                search.Text = "zzz-sem-resultado"; content = Layout(window, 1280, 760);
                Require(list.Items.Count == 0 && VisibleTexts(content).Contains("Nenhum pedido encontrado para esta busca.") && !VisibleTexts(content).Contains("Nenhum pedido aguardando decisão nesta UBS."), "A pesquisa sem resultado deveria dizer \"Nenhum pedido encontrado para esta busca.\": " + string.Join(" | ", VisibleTexts(content)));
                SavePreview(content, Path.Combine(previewRoot, "familia-busca-vazia-claro.png"));
                search.Text = ""; Layout(window, 1280, 760);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    window.ViewModel.SetAppearance(mode); vm.SelectedPendingFamily = null; content = Layout(window, 1280, 760);
                    SavePreview(content, Path.Combine(previewRoot, $"familia-{(mode == Appearance.Light ? "claro" : "escuro")}.png"));
                    vm.SelectedPendingFamily = vm.FamilyPending[0]; content = Layout(window, 1280, 760);
                    SavePreview(content, Path.Combine(previewRoot, $"familia-selecionado-{(mode == Appearance.Light ? "claro" : "escuro")}.png"));
                }
                window.ViewModel.SetAppearance(Appearance.Light);
                // Largura mínima da janela: o balão cabe e nada passa da lista.
                content = Layout(window, 980, 640);
                Require(balloon.IsVisible && list.ActualWidth <= content.ActualWidth && balloon.ActualWidth <= list.ActualWidth, $"Em 980 de largura o balão deveria caber na lista ({balloon.ActualWidth:F0} de {list.ActualWidth:F0}).");
                SavePreview(content, Path.Combine(previewRoot, "familia-980-selecionado-claro.png"));
                content = Show(window, "family:approved", 1280, 760);
                Require(VisibleTexts(content).Contains("Nenhum vínculo aprovado nesta UBS."), "Sem vínculos aprovados a tela deveria dizer \"Nenhum vínculo aprovado nesta UBS.\".");
                SavePreview(content, Path.Combine(previewRoot, "vinculos-vazio-claro.png"));
                // Vínculos aprovados com dados: a lista, o balão com a lixeira (Encerrar vínculo) e o pop-up Recusar da Família, nos dois temas.
                fixture.AddFamilyRequest(unit.Id, "Carla Lima", "98765432100", lia.Cpf, status: "approved", requestedAt: now.AddDays(-30));
                Pump(vm.RefreshAutomaticallyAsync()); Settle(vm); content = Layout(window, 1280, 760);
                var approvedList = Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == "Vínculos de família aprovados");
                Require(approvedList.Items.Count == 1 && VisibleTexts(content).Contains("Carla Lima → Lia Santos") && !VisibleTexts(content).Contains("Nenhum vínculo aprovado nesta UBS."), "Vínculos aprovados deveria listar o vínculo lido no ciclo: " + string.Join(" | ", VisibleTexts(content)));
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    var suffix = mode == Appearance.Light ? "claro" : "escuro";
                    window.ViewModel.SetAppearance(mode); vm.SelectedApprovedFamily = null; content = Layout(window, 1280, 760);
                    SavePreview(content, Path.Combine(previewRoot, $"vinculos-{suffix}.png"));
                    vm.SelectedApprovedFamily = vm.FamilyApproved[0]; content = Layout(window, 1280, 760);
                    Require(FindActionButton(content, "Encerrar vínculo selecionado").Style == (Style)application.FindResource("ListActionDangerButton"), "Encerrar vínculo deveria ser a lixeira branca em fundo vermelho.");
                    SavePreview(content, Path.Combine(previewRoot, $"vinculos-selecionado-{suffix}.png"));
                    content = Show(window, "family:requests", 1280, 760); vm.SelectedPendingFamily = vm.FamilyPending[0]; content = Layout(window, 1280, 760);
                    InspectModal<FormDialog>(window, dialog =>
                    {
                        Descendants<TextBox>(dialog).Single(box => AutomationProperties.GetName(box) == "Motivo da recusa").Text = "Documento do dependente ilegível";
                        // O pop-up abre com uma animação curta de entrada: a captura espera ela terminar.
                        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); System.Threading.Thread.Sleep(400); dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
                        SavePreview(dialog, Path.Combine(previewRoot, $"familia-recusar-{suffix}.png"));
                        InvokeButton(Descendants<Button>(dialog).Single(button => button.Content as string == "Fechar")); PumpUntil(() => !dialog.IsVisible);
                    }, () => InvokeButton(FindActionButton(content, "Recusar pedido")));
                    Require(vm.FamilyPending.Count == 3 && vm.FamilyRejectReason == "", "Fechar o pop-up Recusar não deveria recusar nem guardar o motivo.");
                    content = Show(window, "family:approved", 1280, 760);
                }
                window.ViewModel.SetAppearance(Appearance.Light);
                // Trocar de UBS com a Família aberta: os pedidos da nova UBS vêm junto com a troca (sem botão Atualizar e sem esperar o ciclo de 20 s).
                var other = fixture.AddUnit("UBS Família Vizinha", "Norte");
                var neighbor = fixture.AddRegistry(other.Id, "Rui Prado", "52998224725");
                fixture.AddFamilyRequest(other.Id, "Vera Prado", "11144477735", neighbor.Cpf, requestedAt: now.AddHours(-5));
                Show(window, "family:requests", 1280, 760);
                vm.CandidateObsUnit = other; Pump(vm.ConfirmObsUnitAsync()); Settle(vm); content = Layout(window, 1280, 760);
                Require(vm.ActiveObsUnit?.Id == other.Id && vm.FamilyPending.Select(link => link.GuardianName).SequenceEqual(["Vera Prado"]) && VisibleTexts(content).Contains("Vera Prado → Rui Prado"),
                    "Trocar de UBS na Família deveria trazer na hora os pedidos da nova UBS: " + string.Join(", ", vm.FamilyPending.Select(link => link.GuardianName)));
                // Sem pedido aguardando: o aviso de lista vazia (diferente do "encontrado para esta busca"), nos dois temas.
                fixture.FamilyLinks.Clear(); Pump(vm.RefreshAutomaticallyAsync()); Settle(vm); content = Layout(window, 1280, 760);
                Require(VisibleTexts(content).Contains("Nenhum pedido aguardando decisão nesta UBS.") && !VisibleTexts(content).Contains("Nenhum pedido encontrado para esta busca."), "Sem pedidos a Família deveria dizer \"Nenhum pedido aguardando decisão nesta UBS.\": " + string.Join(" | ", VisibleTexts(content)));
                SavePreview(content, Path.Combine(previewRoot, "familia-vazia-claro.png"));
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 760);
                SavePreview(content, Path.Combine(previewRoot, "familia-vazia-escuro.png"));
                window.ViewModel.SetAppearance(Appearance.Light);
            }
            finally { CloseWindow(window); }
        });
    }

    private static int ItemIndex(ListBox list, object item) => list.Items.IndexOf(item);

    /// <summary>Um clique do mouse (botão para baixo e para cima) numa linha da lista, pelos mesmos eventos que o Windows entrega.</summary>
    private static void ClickListRow(ListBox list, int index, int clicks)
    {
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
        row.RaiseEvent(MouseArgs(Mouse.PreviewMouseDownEvent, row, clicks)); row.RaiseEvent(MouseArgs(Mouse.MouseDownEvent, row, clicks));
        row.RaiseEvent(MouseArgs(Mouse.PreviewMouseUpEvent, row, clicks)); row.RaiseEvent(MouseArgs(Mouse.MouseUpEvent, row, clicks));
    }

    /// <summary>Roda <paramref name="open"/> (que pede a ficha) e confere a ficha que abrir dentro do laço do modal; falha se nenhuma abrir.</summary>
    private static void OpenFicha(ShellWindow window, Action open, Action<ItemDetailsWindow> inspect)
    {
        var observed = false; string? error = null; var attempts = 0;
        void Run()
        {
            var popup = window.OwnedWindows.OfType<ItemDetailsWindow>().SingleOrDefault();
            if (popup is null)
            {
                if (++attempts < 3000) window.Dispatcher.BeginInvoke(Run, DispatcherPriority.ApplicationIdle);
                else error = "A ficha não abriu.";
                return;
            }
            observed = true;
            try { inspect(popup); }
            catch (Exception failure) { error = failure.Message; }
            finally { if (popup.IsVisible) popup.Close(); }
        }
        window.Dispatcher.BeginInvoke(Run, DispatcherPriority.ApplicationIdle);
        open();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!observed && error is null) PumpUntil(() => observed || error is not null);
        Require(observed && error is null, error ?? "Nenhuma ficha foi aberta.");
    }
}
