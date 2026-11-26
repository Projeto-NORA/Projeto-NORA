using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Informações da UBS no padrão novo (26/11/2026): listagens abertas com balão de ações, cadastros em pop-up com a grade de campos, grade semanal por dia, ficha em linhas
/// e a subtela Computadores removida. O atendimento ao público tem as checagens em <c>UnitProfileChecks</c>.
/// </summary>
internal static partial class Program
{
    private static ListBox UnitList(FrameworkElement content, string name) => Descendants<ListBox>(content).Single(list => AutomationProperties.GetName(list) == name);

    /// <summary>Seleciona na lista os itens que atendem ao critério (o balão aparece pelo comportamento da listagem) e devolve a tela já medida.</summary>
    private static FrameworkElement SelectUnitRows<T>(ShellWindow window, string listName, Func<T, bool> match, double width = 1280, double height = 900) where T : class
    {
        var content = Layout(window, width, height);
        var list = UnitList(content, listName);
        list.SelectedItems.Clear();
        foreach (var item in list.Items.OfType<T>().Where(match)) list.SelectedItems.Add(item);
        return Layout(window, width, height);
    }

    private static SelectionBalloon VisibleBalloon(FrameworkElement content) => Descendants<SelectionBalloon>(content).Single(balloon => balloon.IsVisible);

    /// <summary>Clica o botão (do cabeçalho ou do balão) que abre o pop-up e deixa a inspeção rodar dentro dele.</summary>
    private static void OpenUnitForm(ShellWindow window, FrameworkElement content, string buttonName, Action<FormDialog> inspect) =>
        InspectModal<FormDialog>(window, inspect, () => InvokeButton(FindActionButton(content, buttonName)));

    private static TextBox DialogInput(FormDialog dialog, string name) => Descendants<TextBox>(dialog).Single(box => AutomationProperties.GetName(box) == name);
    private static Button DialogButton(FormDialog dialog, string label) => Descendants<Button>(dialog).Single(button => button.IsVisible && button.Content as string == label);
    private static void SubmitUnitForm(FormDialog dialog, string primary) { InvokeButton(DialogButton(dialog, primary)); PumpUntil(() => !dialog.IsBusy); }
    /// <summary>Enter na lista abre a ficha dos selecionados (pelo comportamento da listagem, em seguida, não dentro da chamada); a inspeção roda com a ficha aberta.</summary>
    private static void OpenDetailsByEnter(ShellWindow window, ListBox list, Action<ItemDetailsWindow> inspect)
    {
        var seen = false;
        InspectModal<ItemDetailsWindow>(window, details => { seen = true; inspect(details); }, () => { PressKey(list, Key.Enter); PumpUntil(() => seen); });
    }
    private static Rect BoundsIn(FrameworkElement element, FrameworkElement root) => new(element.TranslatePoint(new Point(0, 0), root), new Size(element.ActualWidth, element.ActualHeight));

    private static void RunUnitInformationChecks(string storageRoot, string[] args)
    {
        var previewRoot = PreviewRoot(args);
        Check("Informações da UBS: o menu não tem mais Computadores, a rota antiga cai na página inteira e nenhuma tela tem título repetido nem botão Atualizar", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            fixture.AddUnit("UBS Menu", "Centro");
            var window = OpenOperational(fixture, storageRoot, "unit-info-menu", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var area = window.ViewModel.Navigation.Single(item => item.Id == "unit-settings");
                Require(area.Children.Select(item => item.Title).SequenceEqual(["Atendimento", "Profissionais", "Consultórios", "Grade semanal", "Bloqueios"]), "O menu de Informações da UBS deveria ter só cinco itens, sem Computadores: " + string.Join(", ", area.Children.Select(item => item.Title)));
                Require(!area.Tip.Contains("computadores", StringComparison.OrdinalIgnoreCase), "A dica da área ainda cita computadores.");
                window.ViewModel.Navigate("unit-settings:devices");
                Require(window.ViewModel.Page == "unit-settings" && window.ViewModel.SubPage is null, "A rota antiga unit-settings:devices deveria cair na página inteira, sem subtela.");
                foreach (var route in new[] { "unit-settings:profile", "unit-settings:professionals", "unit-settings:rooms", "unit-settings:schedule", "unit-settings:blocks" })
                {
                    var content = Show(window, route, 1280, 900);
                    var texts = FullTexts(content);
                    Require(!texts.Contains("Informações da UBS") || window.ViewModel.PageLabel.Contains("Informações da UBS", StringComparison.Ordinal), "Título extra na tela.");
                    Require(!texts.Any(text => text is "Atendimento ao público" or "Grade semanal de horários" or "Bloqueios (folgas e feriados)" or "Computadores"), $"{route}: a tela repete o nome da subtela ou fala de computadores: " + string.Join(" | ", texts.Take(20)));
                    Require(!Descendants<Button>(content).Any(button => button.IsVisible && (AutomationProperties.GetName(button) ?? button.Content?.ToString() ?? "").StartsWith("Atualizar", StringComparison.Ordinal)), $"{route}: não deveria haver botão Atualizar.");
                    Require(!Descendants<DataGrid>(content).Any(grid => grid.IsVisible), $"{route}: a listagem não deveria ser uma tabela.");
                }
            }
            finally { CloseWindow(window); }
        });

        Check("Profissionais: listagem aberta em duas linhas com etiqueta, \"+ Novo profissional\" no cabeçalho, balão por seleção (Editar, Desativar/Reativar, lote) e ficha em linhas", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Profissionais", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); var pediatria = fixture.AddSpecialty("Pediatria");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id, pediatria.Id], acronym: "CRM", number: "12345", state: "SP");
            var bruno = fixture.AddProfessional(unit.Id, "Dr. Bruno Alves", [pediatria.Id]);
            var carla = fixture.AddProfessional(unit.Id, "Dra. Carla Rocha", [cardio.Id], active: false, acronym: "COREN", number: "99", state: "RS");
            var window = OpenOperational(fixture, storageRoot, "unit-info-professionals", Appearance.Light);
            var asked = new List<(string Title, string Text)>(); Confirmation.Handler = (_, title, text) => { asked.Add((title, text)); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:professionals", 1280, 900);
                var list = UnitList(content, "Profissionais da UBS");
                Require(list.Items.Count == 3 && vm.Professionals.Select(item => item.FullName).SequenceEqual(["Dr. Bruno Alves", "Dra. Ana Lima", "Dra. Carla Rocha"]), "A listagem deveria trazer os três profissionais em ordem alfabética.");
                var texts = FullTexts(content);
                Require(texts.Contains("Cardiologia, Pediatria | CRM 12345/SP") && texts.Contains("Pediatria") && texts.Contains("Cardiologia | COREN 99/RS") && texts.Count(text => text == "Ativo") == 2 && texts.Count(text => text == "Desativado") == 1, "As duas linhas e a etiqueta de cada profissional deveriam aparecer (dados só do que foi informado): " + string.Join(" | ", texts));
                Require(!texts.Any(text => text.Contains("Não informado", StringComparison.Ordinal) || text.Contains("|  |", StringComparison.Ordinal)), "Campo vazio não pode virar texto nem separador sobrando.");
                var openList = (Style)Application.Current.FindResource("OpenList");
                Require(ReferenceEquals(list.Style, openList) && list.SelectionMode == SelectionMode.Extended && !Descendants<Border>(content).Any(border => border.IsVisible && ReferenceEquals(border.Style, (Style)Application.Current.FindResource("Card"))), "A listagem deveria ser aberta (OpenList), sem card.");
                Require(Math.Abs(list.ActualHeight - ((ScrollViewer)window.FindName("PageScroll")).ViewportHeight) < 60 && list.ActualHeight > 400, $"A lista deveria ocupar a tela inteira (altura {list.ActualHeight:F0}).");
                // Cabeçalho: caminho da tela, pesquisa com lupa e o botão novo à direita.
                var add = FindActionButton(content, "Novo profissional");
                var addBox = BoundsIn(add, content); var search = Input(content, "Pesquisar: Profissionais");
                Require(window.ViewModel.PageLabel == "Profissionais • Informações da UBS" && addBox.Right > content.ActualWidth - 60 && addBox.Top < 70 && BoundsIn(search, content).Right < addBox.Left && add.IsEnabled && Descendants<System.Windows.Shapes.Path>(add).Any(), $"O botão \"+ Novo profissional\" deveria ficar no cabeçalho, à direita da pesquisa ({addBox}).");
                Require(!Descendants<SelectionBalloon>(content).Any(balloon => balloon.IsVisible), "Sem seleção não há balão.");
                // Balão com um ativo: Editar e Desativar (lixeira vermelha).
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id);
                var balloon = VisibleBalloon(content);
                Require(balloon.Label == "Dra. Ana Lima" && FindActionButton(content, "Editar profissional").IsVisible && FindActionButton(content, "Desativar profissional").IsVisible && !Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Reativar profissional"), "Com um profissional ativo o balão deveria ter Editar e Desativar.");
                var danger = FindActionButton(content, "Desativar profissional");
                Require(ReferenceEquals(danger.Style, (Style)Application.Current.FindResource("ListActionDangerButton")) && ((SolidColorBrush)danger.Background).Color == ThemeBrush(Application.Current, "ListActionDangerBrush").Color, "Desativar deveria ser o botão de lixeira vermelha.");
                var balloonBox = BoundsIn(balloon, content); var listBox = BoundsIn(list, content);
                Require(balloonBox.Bottom <= listBox.Bottom && balloonBox.Top > listBox.Top + listBox.Height / 2 && Math.Abs(list.ActualHeight - listBox.Height) < 1, "O balão deveria flutuar sobre o pé da lista, sem empurrá-la.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-profissionais-balao-light.png"), 1280, 900);
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == carla.Id);
                Require(FindActionButton(content, "Reativar profissional").IsVisible && !Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Desativar profissional"), "Com um profissional desativado o balão deveria trazer Reativar, não Desativar.");
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id || item.Id == bruno.Id);
                Require(VisibleBalloon(content).Label == "2 profissionais selecionados" && !Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Editar profissional") && FindActionButton(content, "Desativar profissional").IsVisible, "Com vários o balão perde o Editar e mantém o que vale para todos.");
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id || item.Id == carla.Id);
                Require(!Descendants<Button>(content).Any(button => button.IsVisible && (AutomationProperties.GetName(button) ?? "") is "Desativar profissional" or "Reativar profissional" or "Editar profissional"), "Com ativo e desativado juntos nenhuma ação de situação se aplica a todos.");
                // Lote: uma confirmação só.
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id || item.Id == bruno.Id);
                Click(window, content, "Desativar profissional", 1280, 900);
                Require(asked.Count == 1 && asked[0].Title == "Desativar profissionais" && asked[0].Text.StartsWith("Desativar 2 profissionais?", StringComparison.Ordinal) && fixture.Count("PATCH", "/active") == 2 && fixture.ProfessionalsOf(unit.Id).All(item => !item.IsActive) && vm.Message == "2 profissionais desativados. As consultas já marcadas continuam como estavam.", "Desativar dois profissionais deveria pedir uma confirmação e enviar as duas alterações: " + vm.Message);
                Require(list.SelectedItems.Count == 2, "A seleção deveria continuar depois da ação.");
                content = Layout(window, 1280, 900);
                Click(window, content, "Reativar profissional", 1280, 900);
                Require(asked.Count == 1 && fixture.ProfessionalsOf(unit.Id).Count(item => item.IsActive) == 2 && vm.Message == "2 profissionais reativados.", "Reativar não pede confirmação: " + vm.Message);
                // Um só: confirmação com o nome.
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == bruno.Id);
                Click(window, content, "Desativar profissional", 1280, 900);
                Require(asked.Count == 2 && asked[1].Title == "Desativar profissional" && asked[1].Text.StartsWith("Desativar Dr. Bruno Alves?", StringComparison.Ordinal) && !fixture.ProfessionalsOf(unit.Id).Single(item => item.Id == bruno.Id).IsActive, "A confirmação de um só deveria citar o nome.");
                // Ficha em linhas (Enter na lista): uma por vez, sem cards.
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id);
                OpenDetailsByEnter(window, UnitList(content, "Profissionais da UBS"), details =>
                {
                    Require(details.Title == "Dra. Ana Lima" && Descendants<TextBlock>(details).Any(text => text.Text == "Registro no conselho") && Descendants<TextBlock>(details).Any(text => text.Text == "CRM 12345/SP") && Descendants<TextBlock>(details).Any(text => text.Text == "Cardiologia, Pediatria"), "A ficha deveria listar nome, especialidades e registro em linhas.");
                    SavePreview(details, Path.Combine(previewRoot, "obs-profissionais-ficha-light.png"));
                });
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Profissionais: o pop-up de cadastro tem a grade de campos, o registro no conselho numa linha com a explicação, sem aviso da lista da rede, e as regras e o 409 aparecem dentro dele", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Cadastro Profissional", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); fixture.AddSpecialty("Pediatria"); fixture.AddSpecialty("Dermatologia", active: false);
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]);
            var window = OpenOperational(fixture, storageRoot, "unit-info-professional-form", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:professionals", 1280, 900);
                OpenUnitForm(window, content, "Novo profissional", dialog =>
                {
                    Require(dialog.Title == "Cadastrar profissional" && DialogButton(dialog, "Salvar profissional").IsVisible, "O pop-up deveria abrir com o título e o botão primário.");
                    var texts = FullTexts(dialog);
                    Require(texts.Contains("Nome completo") && texts.Contains("Registro no conselho (opcional)") && texts.Contains("Especialidades") && !texts.Any(text => text.Contains("lista da rede", StringComparison.OrdinalIgnoreCase)), "Os rótulos deveriam estar lá, sem o aviso da lista da rede: " + string.Join(" | ", texts));
                    Require(texts.Any(text => text.StartsWith("O conselho profissional é o órgão", StringComparison.Ordinal) && text.Contains("CRM", StringComparison.Ordinal)), "A dica deveria explicar o que é o conselho profissional.");
                    var acronym = DialogInput(dialog, "Sigla do conselho"); var number = DialogInput(dialog, "Número do conselho"); var state = DialogInput(dialog, "UF do conselho"); var name = DialogInput(dialog, "Nome do profissional");
                    Require(Math.Abs(BoundsIn(acronym, dialog).Top - BoundsIn(number, dialog).Top) < 1 && Math.Abs(BoundsIn(number, dialog).Top - BoundsIn(state, dialog).Top) < 1 && BoundsIn(acronym, dialog).Right < BoundsIn(number, dialog).Left && BoundsIn(number, dialog).Right < BoundsIn(state, dialog).Left, "Sigla, número e UF deveriam estar numa linha só.");
                    Require(BoundsIn(name, dialog).Width > 280 && Math.Abs(BoundsIn(name, dialog).Width - (BoundsIn(acronym, dialog).Width + BoundsIn(number, dialog).Width + BoundsIn(state, dialog).Width + 16)) < 40, "O nome e o grupo do conselho deveriam ter a mesma largura de coluna da grade.");
                    var boxes = Descendants<CheckBox>(dialog).Where(box => box.IsVisible).ToArray();
                    Require(boxes.Select(box => box.Content?.ToString()).SequenceEqual(["Cardiologia", "Pediatria"]) && boxes.All(box => BoundsIn(Descendants<Border>(box).First(), dialog).Left < BoundsIn(Descendants<TextBlock>(box).First(item => item.Text == (string)box.Content), dialog).Left), "Só as especialidades ativas, com a caixa à esquerda do nome.");
                    Require(!Descendants<Border>(dialog).Any(border => FindAncestor<TextBox>(border) is null && FindAncestor<CheckBox>(border) is null && border.Background is SolidColorBrush brush && brush.Color == ThemeBrush(Application.Current, "SurfaceAltBrush").Color && border.CornerRadius.TopLeft >= 8 && border.ActualWidth > 300 && border.ActualHeight > 60), "Não deveria haver card dentro do pop-up.");
                    SavePreview(dialog, Path.Combine(previewRoot, "obs-profissional-cadastro-light.png"));
                    // Sem nome nem especialidade: o pop-up continua aberto com os erros nos campos e a mensagem dentro dele.
                    SubmitUnitForm(dialog, "Salvar profissional");
                    Require(dialog.IsVisible && !dialog.Saved && dialog.ErrorMessage == "Corrija o que está destacado e salve de novo." && FullTexts(dialog).Contains("Marque ao menos uma especialidade.") && FullTexts(dialog).Any(text => text.StartsWith("Informe o nome do profissional", StringComparison.Ordinal)) && fixture.Count("POST", "/professionals") == 0, "Sem nome nem especialidade o pop-up deveria ficar aberto, mostrando os erros: " + dialog.ErrorMessage);
                    name.Text = "Dra. Ana Lima"; acronym.Text = "crm";
                    boxes.First(box => (string)box.Content == "Pediatria").IsChecked = true;
                    SubmitUnitForm(dialog, "Salvar profissional");
                    Require(dialog.IsVisible && FullTexts(dialog).Any(text => text.StartsWith("Conselho: informe a sigla, o número e a UF juntos", StringComparison.Ordinal)), "Conselho incompleto deveria ser recusado dentro do pop-up: " + dialog.ErrorMessage + " | " + string.Join(" | ", FullTexts(dialog)));
                    Require(acronym.CharacterCasing == CharacterCasing.Upper && state.CharacterCasing == CharacterCasing.Upper, "A sigla e a UF do conselho deveriam ficar em maiúsculas.");
                    number.Text = "4321"; state.Text = "sp";
                    SubmitUnitForm(dialog, "Salvar profissional");
                    Require(dialog.IsVisible && FullTexts(dialog).Count(text => text.Contains("Já existe", StringComparison.Ordinal)) == 1 && fixture.ProfessionalsOf(unit.Id).Count == 1, "O 409 de nome repetido deveria aparecer dentro do pop-up, que continua aberto: " + dialog.ErrorMessage + " / " + vm.ProfessionalNameError);
                    SavePreview(dialog, Path.Combine(previewRoot, "obs-profissional-cadastro-erro-light.png"));
                    name.Text = "Dr. Bruno Alves";
                    SubmitUnitForm(dialog, "Salvar profissional");
                    PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && fixture.ProfessionalsOf(unit.Id).Count == 2 && vm.Professionals.Any(item => item is { FullName: "Dr. Bruno Alves", CouncilDisplay: "CRM 4321/SP" }), "O profissional válido deveria ser gravado e fechar o pop-up.");
                });
                // Editar: o mesmo pop-up preenchido; X fecha sem gravar.
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id);
                OpenUnitForm(window, content, "Editar profissional", dialog =>
                {
                    Require(dialog.Title == "Editar profissional" && DialogInput(dialog, "Nome do profissional").Text == "Dra. Ana Lima" && Descendants<CheckBox>(dialog).Single(box => box.IsVisible && (string)box.Content == "Cardiologia").IsChecked == true, "Editar deveria abrir o pop-up preenchido.");
                    DialogInput(dialog, "Nome do profissional").Text = "Dra. Ana Lima Souza";
                    InvokeButton(Descendants<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Fechar sem salvar"));
                    PumpUntil(() => !dialog.IsVisible);
                    Require(!dialog.Saved && fixture.Count("PUT", "/professionals/" + ana.Id) == 0, "O X não deveria gravar.");
                });
                content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id);
                OpenUnitForm(window, content, "Editar profissional", dialog =>
                {
                    DialogInput(dialog, "Nome do profissional").Text = "Dra. Ana Lima Souza"; SubmitUnitForm(dialog, "Salvar profissional");
                    PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && fixture.Count("PUT", "/professionals/" + ana.Id) == 1 && vm.Professionals.Any(item => item.FullName == "Dra. Ana Lima Souza"), "Salvar a edição deveria gravar o PUT e fechar o pop-up.");
                });
                window.ViewModel.SetAppearance(Appearance.Dark); content = Layout(window, 1280, 900);
                OpenUnitForm(window, content, "Novo profissional", dialog => SavePreview(dialog, Path.Combine(previewRoot, "obs-profissional-cadastro-dark.png")));
                window.ViewModel.SetAppearance(Appearance.Light);
            }
            finally { CloseWindow(window); }
        });

        Check("Consultórios: listagem aberta com etiqueta, \"+ Novo consultório\" abre o pop-up, balão com Renomear, Desativar ou Reativar e o 409 dentro do pop-up", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Consultórios", "Centro");
            var sala1 = fixture.AddRoom(unit.Id, "Consultório 1"); var sala2 = fixture.AddRoom(unit.Id, "Consultório 2", active: false);
            var window = OpenOperational(fixture, storageRoot, "unit-info-rooms", Appearance.Light);
            var asked = new List<string>(); Confirmation.Handler = (_, title, _) => { asked.Add(title); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:rooms", 1280, 900);
                var texts = FullTexts(content);
                Require(window.ViewModel.PageLabel == "Consultórios • Informações da UBS" && texts.Contains("Consultório 1") && texts.Contains("Ativo") && texts.Contains("Desativado") && FindActionButton(content, "Novo consultório").IsEnabled, "A listagem de consultórios deveria mostrar nome e etiqueta e o botão novo no cabeçalho.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-consultorios-light.png"), 1280, 900);
                content = SelectUnitRows<Room>(window, "Consultórios da UBS", item => item.Id == sala1.Id);
                Require(VisibleBalloon(content).Label == "Consultório 1" && FindActionButton(content, "Renomear consultório").IsVisible && FindActionButton(content, "Desativar consultório").IsVisible, "O balão do consultório ativo deveria ter Renomear e Desativar.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-consultorios-balao-light.png"), 1280, 900);
                OpenUnitForm(window, content, "Renomear consultório", dialog =>
                {
                    var name = DialogInput(dialog, "Nome do consultório");
                    Require(dialog.Title == "Renomear consultório" && name.Text == "Consultório 1" && !FullTexts(dialog).Any(text => text.Contains("Nome do consultório") && text != "Nome do consultório"), "Renomear deveria abrir o pop-up com o nome atual.");
                    name.Text = "consultório 2"; SubmitUnitForm(dialog, "Salvar consultório");
                    Require(dialog.IsVisible && FullTexts(dialog).Any(text => text.Contains("Já existe", StringComparison.Ordinal)), "O nome repetido deveria ser recusado dentro do pop-up: " + dialog.ErrorMessage);
                    name.Text = "Sala Azul"; SubmitUnitForm(dialog, "Salvar consultório"); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && vm.Rooms.Any(item => item.Name == "Sala Azul") && vm.Message == "Consultório renomeado.", "Salvar deveria renomear e fechar: " + vm.Message);
                });
                content = SelectUnitRows<Room>(window, "Consultórios da UBS", item => item.Id == sala1.Id);
                Click(window, content, "Desativar consultório", 1280, 900);
                Require(asked.SequenceEqual(["Desativar consultório"]) && !fixture.RoomsOf(unit.Id).Single(item => item.Id == sala1.Id).IsActive && FindActionButton(Layout(window, 1280, 900), "Reativar consultório").IsVisible, "Desativar deveria pedir confirmação e, com a seleção mantida, o balão passar a Reativar.");
                content = SelectUnitRows<Room>(window, "Consultórios da UBS", _ => true);
                Require(VisibleBalloon(content).Label == "2 consultórios selecionados" && FindActionButton(content, "Reativar consultório").IsVisible && !Descendants<Button>(content).Any(button => button.IsVisible && (AutomationProperties.GetName(button) ?? "") is "Renomear consultório" or "Desativar consultório"), "Com dois consultórios desativados juntos só Reativar vale (sem Renomear nem Desativar).");
                OpenUnitForm(window, content, "Novo consultório", dialog =>
                {
                    Require(dialog.Title == "Cadastrar consultório" && DialogInput(dialog, "Nome do consultório").Text == "", "Novo consultório deveria abrir o pop-up vazio.");
                    SubmitUnitForm(dialog, "Salvar consultório");
                    Require(dialog.IsVisible && FullTexts(dialog).Any(text => text.StartsWith("Informe o nome do consultório", StringComparison.Ordinal)), "Sem nome o pop-up deveria ficar aberto com o erro no campo.");
                    SavePreview(dialog, Path.Combine(previewRoot, "obs-consultorio-cadastro-light.png"));
                });
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Grade semanal: sete botões de dia com segunda ao abrir, listagem do dia, \"+ Novo horário\" com o dia já escolhido, Editar e Remover (lote) no balão", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Grade", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); var pediatria = fixture.AddSpecialty("Pediatria");
            var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id, pediatria.Id]); var bruno = fixture.AddProfessional(unit.Id, "Dr. Bruno Alves", [pediatria.Id]);
            var sala1 = fixture.AddRoom(unit.Id, "Consultório 1"); var sala2 = fixture.AddRoom(unit.Id, "Consultório 2");
            fixture.AddScheduleRow(unit.Id, ana, sala1, cardio.Id, weekday: 1, "08:00", "12:00", 20);
            var tarde = fixture.AddScheduleRow(unit.Id, bruno, sala2, pediatria.Id, weekday: 1, "13:00", "17:00", 30, validFrom: "2030-01-10");
            var quarta = fixture.AddScheduleRow(unit.Id, ana, sala1, cardio.Id, weekday: 3, "09:00", "11:00", 30);
            var window = OpenOperational(fixture, storageRoot, "unit-info-schedule", Appearance.Light);
            var asked = new List<string>(); Confirmation.Handler = (_, title, _) => { asked.Add(title); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:schedule", 1280, 900);
                var toggle = Descendants<SegmentedToggle>(content).Single(item => AutomationProperties.GetName(item) == "Dia da semana da grade");
                Require(toggle.Items.Count == 7 && toggle.Items.Cast<WeekdayOption>().Select(item => item.Label).SequenceEqual(["Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo"]) && ((WeekdayOption)toggle.SelectedItem).Day == 1, "Os sete botões de dia deveriam abrir com a segunda selecionada.");
                var list = UnitList(content, "Grade semanal da UBS");
                var texts = FullTexts(content);
                Require(list.Items.Count == 2 && texts.Contains("08:00 às 12:00 · atendimentos de 20 min") && texts.Contains("Dra. Ana Lima | Cardiologia | Consultório 1") && texts.Contains("13:00 às 17:00 · atendimentos de 30 min") && texts.Contains("Dr. Bruno Alves | Pediatria | Consultório 2 | A partir de 10/01/2030"), "A listagem de segunda deveria ter título e dados como o pedido: " + string.Join(" | ", texts));
                Require(!texts.Any(text => text is "Grade semanal de horários" or "Segunda-feira") && BoundsIn(toggle, content).Bottom < BoundsIn(list, content).Top, "Os botões de dia ficam acima da lista, sem título repetido.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-grade-light.png"), 1280, 900);
                toggle.SelectedIndex = 2; content = Layout(window, 1280, 900); list = UnitList(content, "Grade semanal da UBS");
                Require(vm.ScheduleDay.Day == 3 && list.Items.Count == 1 && FullTexts(content).Contains("09:00 às 11:00 · atendimentos de 30 min"), "Quarta deveria mostrar só o horário da quarta.");
                toggle.SelectedIndex = 1; content = Layout(window, 1280, 900);
                Require(FullTexts(content).Contains("Nenhum horário na grade de terça-feira."), "Um dia sem horários deveria dizer isso: " + string.Join(" | ", FullTexts(content)));
                SaveScrolled(window, Path.Combine(previewRoot, "obs-grade-dia-vazio-light.png"), 1280, 900);
                // Novo horário já vem com o dia escolhido (terça) e o botão "Salvar horário na grade" à direita.
                OpenUnitForm(window, content, "Novo horário", dialog =>
                {
                    var day = Descendants<ComboBox>(dialog).Single(box => AutomationProperties.GetName(box) == "Dia da semana da grade");
                    Require(dialog.Title == "Adicionar horário à grade" && (int)day.SelectedValue == 2, "O pop-up deveria vir com o dia escolhido nos botões (terça).");
                    var primary = DialogButton(dialog, "Salvar horário na grade");
                    Require(BoundsIn(primary, dialog).Right > dialog.ActualWidth - 60, "O primário deveria ficar à direita.");
                    Require(!Descendants<Border>(dialog).Any(border => FindAncestor<TextBox>(border) is null && FindAncestor<ComboBox>(border) is null && FindAncestor<Button>(border) is null && border.Background is SolidColorBrush brush && brush.Color == ThemeBrush(Application.Current, "SurfaceAltBrush").Color && border.CornerRadius.TopLeft >= 8 && border.ActualWidth > 300), "Não deveria haver card dentro do pop-up.");
                    SubmitUnitForm(dialog, "Salvar horário na grade");
                    Require(dialog.IsVisible && dialog.ErrorMessage == "Corrija o que está destacado e salve de novo." && FullTexts(dialog).Count(text => text == "Escolha o profissional.") == 1, "Sem profissional o motivo deveria aparecer uma vez, junto dos campos: " + dialog.ErrorMessage);
                    SavePreview(dialog, Path.Combine(previewRoot, "obs-grade-cadastro-light.png"));
                    vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(item => item.Id == bruno.Id); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(item => item.Id == sala1.Id); vm.ScheduleStart = "14:00"; vm.ScheduleEnd = "18:00";
                    SubmitUnitForm(dialog, "Salvar horário na grade"); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && fixture.ScheduleOf(unit.Id).Any(row => row.Weekday == 2 && row.StartTime == "14:00") && vm.Message == "Horário adicionado à grade.", "O horário deveria ser gravado na terça: " + vm.Message);
                });
                content = Layout(window, 1280, 900); list = UnitList(content, "Grade semanal da UBS");
                Require(list.Items.Count == 1 && FullTexts(content).Contains("14:00 às 18:00 · atendimentos de 30 min"), "A listagem de terça deveria mostrar o horário novo.");
                // Sobreposição (409) dentro do pop-up, junto aos campos.
                OpenUnitForm(window, content, "Novo horário", dialog =>
                {
                    vm.ScheduleProfessional = vm.ActiveProfessionalOptions.Single(item => item.Id == bruno.Id); vm.ScheduleRoom = vm.ActiveRoomOptions.Single(item => item.Id == sala1.Id); vm.ScheduleStart = "15:00"; vm.ScheduleEnd = "16:00";
                    SubmitUnitForm(dialog, "Salvar horário na grade");
                    Require(dialog.IsVisible && vm.ScheduleOverlapError.StartsWith("Sobreposição", StringComparison.Ordinal) && Descendants<Border>(dialog).Any(border => AutomationProperties.GetName(border) == "Aviso de sobreposição na grade" && border.IsVisible) && FullTexts(dialog).Contains(vm.ScheduleOverlapError), "O aviso de sobreposição deveria aparecer no pop-up: " + vm.ScheduleOverlapError);
                    SavePreview(dialog, Path.Combine(previewRoot, "obs-grade-sobreposicao-light.png"));
                });
                // Editar (pop-up preenchido) e Remover (lixeira, com confirmação; vários = uma confirmação).
                toggle.SelectedIndex = 0; content = SelectUnitRows<ScheduleTemplate>(window, "Grade semanal da UBS", row => row.Id == tarde.Id);
                OpenUnitForm(window, content, "Editar horário", dialog =>
                {
                    Require(dialog.Title == "Editar horário da grade" && DialogInput(dialog, "Início da grade").Text == "13:00" && DialogInput(dialog, "Fim da grade").Text == "17:00" && DialogInput(dialog, "Início da vigência da grade").Text == "10/01/2030", "Editar deveria abrir o pop-up preenchido.");
                    DialogInput(dialog, "Duração do atendimento da grade").Text = "20"; SubmitUnitForm(dialog, "Salvar horário na grade"); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && fixture.ScheduleOf(unit.Id).Single(row => row.Id == tarde.Id).SlotMinutes == 20, "A edição deveria ser gravada.");
                });
                content = SelectUnitRows<ScheduleTemplate>(window, "Grade semanal da UBS", _ => true);
                Require(VisibleBalloon(content).Label == "2 horários selecionados" && !Descendants<Button>(content).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "Editar horário") && ReferenceEquals(FindActionButton(content, "Remover horário").Style, (Style)Application.Current.FindResource("ListActionDangerButton")), "Com dois horários o balão só traz Remover, em lixeira vermelha.");
                Click(window, content, "Remover horário", 1280, 900);
                Require(asked.SequenceEqual(["Remover horários da grade"]) && fixture.ScheduleOf(unit.Id).All(row => row.Weekday != 1) && vm.Message == "2 horários removidos da grade. Consultas já marcadas não foram afetadas." && fixture.ScheduleOf(unit.Id).Any(row => row.Id == quarta.Id), "Remover dois horários deveria pedir uma confirmação só: " + vm.Message);
                Require(FullTexts(Layout(window, 1280, 900)).Contains("Nenhum horário na grade de segunda-feira."), "Depois de remover a segunda deveria dizer que não há horário.");
                // Ficha em linhas do horário (Enter).
                toggle.SelectedIndex = 2; content = SelectUnitRows<ScheduleTemplate>(window, "Grade semanal da UBS", row => row.Id == quarta.Id);
                OpenDetailsByEnter(window, UnitList(content, "Grade semanal da UBS"), details => Require(details.Title == "Quarta-feira · 09:00–11:00" && Descendants<TextBlock>(details).Any(text => text.Text == "Consultório 1"), "A ficha do horário deveria listar dia, horário, profissional e consultório."));
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Bloqueios: listagem aberta (motivo; quem | início | fim), \"+ Novo bloqueio\" abre o pop-up com \"Cadastrar bloqueio\" e o balão remove com confirmação", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Bloqueios", "Centro");
            var cardio = fixture.AddSpecialty("Cardiologia"); var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]);
            var feriado = fixture.AddBlockFor(unit.Id, null, DateTime.Today.AddDays(10), "Feriado municipal");
            var folga = fixture.AddBlockFor(unit.Id, ana.Id, DateTime.Today.AddDays(20), "Folga da Dra. Ana");
            var window = OpenOperational(fixture, storageRoot, "unit-info-blocks", Appearance.Light);
            var asked = new List<string>(); Confirmation.Handler = (_, title, _) => { asked.Add(title); return true; };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:blocks", 1280, 900);
                var texts = FullTexts(content);
                Require(window.ViewModel.PageLabel == "Bloqueios • Informações da UBS" && texts.Contains("Feriado municipal") && texts.Contains("UBS inteira | " + DateText(DateTime.Today.AddDays(10)) + " (dia inteiro)") && texts.Contains("Dra. Ana Lima | " + DateText(DateTime.Today.AddDays(20)) + " (dia inteiro)") && !texts.Contains("Bloqueios (folgas e feriados)"), "A listagem deveria mostrar motivo e dados, sem o título de baixo: " + string.Join(" | ", texts));
                SaveScrolled(window, Path.Combine(previewRoot, "obs-bloqueios-light.png"), 1280, 900);
                OpenUnitForm(window, content, "Novo bloqueio", dialog =>
                {
                    Require(dialog.Title == "Novo bloqueio" && BoundsIn(DialogButton(dialog, "Cadastrar bloqueio"), dialog).Right > dialog.ActualWidth - 60, "O pop-up deveria ter Cadastrar bloqueio à direita.");
                    var times = Descendants<TextBox>(dialog).Where(box => (AutomationProperties.GetName(box) ?? "").StartsWith("Horário", StringComparison.Ordinal)).ToArray();
                    Require(times.Length == 2 && times.All(box => !box.IsEnabled), "Com dia inteiro os horários ficam desabilitados.");
                    SubmitUnitForm(dialog, "Cadastrar bloqueio");
                    Require(dialog.IsVisible && FullTexts(dialog).Any(text => text.Contains("motivo do bloqueio", StringComparison.Ordinal)) && fixture.Count("POST", "/schedule-blocks") == 0, "Sem motivo o erro deveria aparecer no pop-up.");
                    SavePreview(dialog, Path.Combine(previewRoot, "obs-bloqueio-cadastro-light.png"));
                    DialogInput(dialog, "Motivo do bloqueio").Text = "Reunião da equipe"; DialogInput(dialog, "Data inicial do bloqueio").Text = DateText(DateTime.Today.AddDays(5));
                    SubmitUnitForm(dialog, "Cadastrar bloqueio"); PumpUntil(() => !dialog.IsVisible);
                    Require(dialog.Saved && fixture.BlocksOf(unit.Id).Count == 3 && vm.ScheduleBlocks.Any(block => block.Reason == "Reunião da equipe"), "O bloqueio deveria ser cadastrado e fechar o pop-up.");
                });
                content = SelectUnitRows<ScheduleBlock>(window, "Bloqueios da UBS", block => block.Id == feriado.Id);
                Require(VisibleBalloon(content).Label == "Feriado municipal" && Descendants<Button>(content).Count(button => button.IsVisible && ReferenceEquals(button.Style, (Style)Application.Current.FindResource("ListActionDangerButton"))) == 1, "O balão do bloqueio só tem Remover (lixeira).");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-bloqueios-balao-light.png"), 1280, 900);
                Click(window, content, "Remover bloqueio", 1280, 900);
                Require(asked.SequenceEqual(["Remover bloqueio"]) && fixture.BlocksOf(unit.Id).All(block => block.Id != feriado.Id) && vm.Message == "Bloqueio removido.", "Remover deveria pedir confirmação e remover: " + vm.Message);
                content = SelectUnitRows<ScheduleBlock>(window, "Bloqueios da UBS", _ => true);
                Click(window, content, "Remover bloqueio", 1280, 900);
                Require(asked.Count == 2 && asked[1] == "Remover bloqueios" && fixture.BlocksOf(unit.Id).Count == 0 && vm.Message == "2 bloqueios removidos.", "Remover vários deveria pedir uma confirmação só: " + vm.Message);
                Require(FullTexts(Layout(window, 1280, 900)).Contains("Nenhum bloqueio nos próximos 12 meses."), "A lista vazia deveria dizer que não há bloqueios.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-bloqueios-vazio-light.png"), 1280, 900);
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });

        Check("Informações da UBS: estados da listagem (vazio, sem resultado na pesquisa, falha, carregando), seleção que sobrevive à releitura automática e atendente só consulta", () =>
        {
            using var fixture = new ApiFixture { UseWorkspaceIdentity = true };
            var unit = fixture.AddUnit("UBS Estados", "Centro");
            var window = OpenOperational(fixture, storageRoot, "unit-info-states", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "unit-settings:professionals", 1280, 900);
                Require(FullTexts(content).Any(text => text.StartsWith("Nenhum profissional cadastrado.", StringComparison.Ordinal)) && !FullTexts(content).Contains("Nenhum profissional encontrado para esta busca."), "Lista vazia deveria dizer que nenhum foi cadastrado.");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-profissionais-vazio-light.png"), 1280, 900);
                var cardio = fixture.AddSpecialty("Cardiologia"); var ana = fixture.AddProfessional(unit.Id, "Dra. Ana Lima", [cardio.Id]); fixture.AddProfessional(unit.Id, "Dr. Bruno Alves", [cardio.Id]);
                Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 900);
                Require(UnitList(content, "Profissionais da UBS").Items.Count == 2 && !FullTexts(content).Any(text => text.StartsWith("Nenhum profissional cadastrado.", StringComparison.Ordinal)), "A releitura automática deveria trazer os profissionais sozinha.");
                var search = Input(content, "Pesquisar: Profissionais"); search.Text = "zzz"; content = Layout(window, 1280, 900);
                Require(FullTexts(content).Contains("Nenhum profissional encontrado para esta busca.") && !FullTexts(content).Any(text => text.StartsWith("Nenhum profissional cadastrado.", StringComparison.Ordinal)), "A busca sem resultado deveria dizer isso, diferente de \"cadastrado\": " + string.Join(" | ", FullTexts(content)));
                SaveScrolled(window, Path.Combine(previewRoot, "obs-profissionais-sem-resultado-light.png"), 1280, 900);
                search.Text = "ana"; content = Layout(window, 1280, 900);
                Require(UnitList(content, "Profissionais da UBS").Items.Count == 1 && !FullTexts(content).Contains("Nenhum profissional encontrado para esta busca."), "A pesquisa deveria filtrar a lista.");
                // Seleção e balão não somem nem piscam na releitura automática; a mudança de outra pessoa aparece sozinha.
                search.Text = ""; content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id);
                var balloon = VisibleBalloon(content); var changes = 0; balloon.IsVisibleChanged += (_, _) => changes++;
                fixture.SetProfessionalActive(unit.Id, ana.Id, false);
                Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 900);
                Require(changes == 0 && balloon.IsVisible && balloon.Label == "Dra. Ana Lima" && UnitList(content, "Profissionais da UBS").SelectedItems.Count == 1 && FindActionButton(content, "Reativar profissional").IsVisible && FullTexts(content).Contains("Desativado"), "A releitura automática não deveria desfazer a seleção nem piscar o balão, e a mudança de outra pessoa aparece sozinha.");
                // Falha: a tela diz que tenta de novo e a leitura seguinte conserta.
                fixture.FailCatalogUnits.Add(unit.Id); fixture.AddProfessional(unit.Id, "Dr. Carlos Dias", [cardio.Id]);
                Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 900);
                Require(vm.ProfessionalsState == "Não foi possível ler os profissionais agora. A tela tenta de novo sozinha." && FullTexts(content).Contains(vm.ProfessionalsState) && UnitList(content, "Profissionais da UBS").Items.Count == 2, "A falha deveria avisar e manter a lista anterior: " + vm.ProfessionalsState);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-profissionais-falha-light.png"), 1280, 900);
                fixture.FailCatalogUnits.Clear(); Pump(vm.RefreshAutomaticallyAsync()); content = Layout(window, 1280, 900);
                Require(vm.ProfessionalsState == "" && UnitList(content, "Profissionais da UBS").Items.Count == 3, "A leitura seguinte deveria consertar sozinha.");
                // Tema escuro.
                window.ViewModel.SetAppearance(Appearance.Dark); content = SelectUnitRows<Professional>(window, "Profissionais da UBS", item => item.Id == ana.Id);
                SaveScrolled(window, Path.Combine(previewRoot, "obs-profissionais-balao-dark.png"), 1280, 900);
                foreach (var route in new[] { "unit-settings:rooms", "unit-settings:schedule", "unit-settings:blocks" }) { Show(window, route, 1280, 900); SaveScrolled(window, Path.Combine(previewRoot, "obs-" + route.Split(':')[1] + "-vazio-dark.png"), 1280, 900); }
                window.ViewModel.SetAppearance(Appearance.Light);
                // Janela mínima (980): nada fica cortado no cabeçalho.
                content = Show(window, "unit-settings:professionals", 980, 640);
                var addBox = BoundsIn(FindActionButton(content, "Novo profissional"), content);
                Require(addBox.Right <= content.ActualWidth && addBox.Left > 300, $"No tamanho mínimo o botão novo deveria continuar à vista ({addBox}).");
                SaveScrolled(window, Path.Combine(previewRoot, "obs-profissionais-janela-minima-light.png"), 980, 640);
            }
            finally { CloseWindow(window); }
        });
    }
}
