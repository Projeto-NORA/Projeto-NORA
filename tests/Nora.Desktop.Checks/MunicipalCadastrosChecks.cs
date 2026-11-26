using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Painel Municipal, grupo cadastros e listagens (Cadastro de UBS, Cadastros de referência, Mapa da rede, Catálogo de especialidades, Comunicação e Usuários):
/// listagem aberta com balão de ações, ficha em linhas, pop-ups de cadastro e a ausência de botão Atualizar. Os ajudantes abaixo servem também às checagens
/// antigas da Secretaria, que foram adaptadas às telas novas.
/// </summary>
internal static partial class Program
{
    private static FrameworkElement PageHeaderOf(ShellWindow window) => (FrameworkElement)window.FindName("PageHeader");

    /// <summary>Um botão do cabeçalho da janela principal (as ações da tela, como "Nova UBS").</summary>
    private static Button HeaderButton(ShellWindow window, string name) =>
        Descendants<Button>(PageHeaderOf(window)).Single(button => button.IsVisible && (AutomationProperties.GetName(button) == name || button.Content?.ToString() == name));

    private static ListBox ListNamed(FrameworkElement content, string name) => Descendants<ListBox>(content).Single(box => AutomationProperties.GetName(box) == name);

    /// <summary>Seleciona linhas da listagem (o balão aparece, como num clique) e deixa a tela assentar.</summary>
    private static void SelectRows(ShellWindow window, ListBox list, params object[] items)
    {
        list.SelectedItems.Clear();
        foreach (var item in items) list.SelectedItems.Add(item);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    /// <summary>Um botão do balão de ações da listagem (pelo texto ou pelo nome de automação).</summary>
    private static Button BalloonButton(ListBox list, string name) =>
        Descendants<Button>(ListingBehavior.GetBalloon(list) ?? throw new InvalidOperationException("A listagem não tem balão.")).Single(button => button.Content?.ToString() == name || AutomationProperties.GetName(button) == name);

    /// <summary>Clica no botão do cabeçalho que abre um pop-up de cadastro e inspeciona o pop-up enquanto ele está aberto.</summary>
    private static void InspectHeaderForm(ShellWindow window, string buttonName, Action<FormDialog> inspect) =>
        InspectModal<FormDialog>(window, inspect, () => InvokeButton(HeaderButton(window, buttonName)));
    /// <summary>Checagens do comportamento novo do grupo: rotas sem título, card nem Atualizar; a rota "Nova conta"; pop-ups que fecham com a sessão; uma ficha por vez.</summary>
    private static void RunMunicipalCadastrosChecks(string storageRoot)
    {
        Check("Secretaria cadastros: as rotas do grupo não têm título repetido, botão Atualizar nem card atrás da listagem; a listagem ocupa a tela e o \"+ Novo\" fica no cabeçalho", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Centro", "Centro"); fixture.AddAnnouncement("Aviso", "Texto");
            var window = OpenMunicipal(fixture, storageRoot, "cad-rotas", Appearance.Light);
            try
            {
                LoginObs(window);
                var card = (Style)Application.Current.FindResource("Card");
                string[] oldHeadings = ["Unidades de saúde", "Unidades cadastradas", "Cadastros da UBS selecionada", "Agenda da UBS selecionada", "Mapa da rede", "Especialidades", "Especialidades da rede",
                    "Comunicação", "Novo comunicado", "Meus comunicados", "Pré-visualização", "Usuários", "Contas do OBS", "Nova conta OBS", "Vínculos de contas OBS", "Histórico de ações"];
                (string Route, string? List, string? NewButton)[] routes =
                [
                    ("units:registry", "UBS cadastradas", "Nova UBS"), ("units:reference", "Pacientes da UBS selecionada", null), ("network", null, null),
                    ("specialties:catalog", "Lista de especialidades", "Nova especialidade"), ("communication", "Comunicados da Secretaria", "Novo comunicado"),
                    ("users:accounts", "Contas OBS", "Nova conta"), ("users:access", "Contas e UBS com acesso", null), ("users:history", "Histórico de ações", null)
                ];
                foreach (var (route, listName, newButton) in routes)
                {
                    var content = Show(window, route, 1280, 900);
                    var page = (FrameworkElement)window.FindName("PageContainer");
                    var headings = Descendants<TextBlock>(page).Where(text => text.IsVisible && (oldHeadings.Contains(text.Text) || text.FontSize >= 18)).Select(text => text.Text).ToArray();
                    Require(headings.Length == 0, $"{route}: a tela repete um título ({string.Join(", ", headings)}).");
                    var buttons = Descendants<Button>(page).Concat(Descendants<Button>(PageHeaderOf(window))).Where(button => button.IsVisible).ToArray();
                    Require(!buttons.Any(button => (button.Content?.ToString() ?? "").Contains("Atualizar", StringComparison.Ordinal) || AutomationProperties.GetName(button).Contains("Atualizar", StringComparison.Ordinal)), $"{route}: há botão de atualizar.");
                    if (newButton is not null) Require(HeaderButton(window, newButton).IsVisible && !Descendants<Button>(page).Any(button => button.IsVisible && button.Content?.ToString() == newButton), $"{route}: \"{newButton}\" deveria ficar só no cabeçalho.");
                    if (listName is null) continue;
                    var list = ListNamed(content, listName);
                    for (DependencyObject? parent = list; parent is not null && !ReferenceEquals(parent, page); parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
                        Require(parent is not Border { Style: { } style } || !ReferenceEquals(style, card), $"{route}: a listagem está dentro de um card.");
                    Require(ReferenceEquals(list.Style, Application.Current.FindResource("OpenList")) && list.ActualHeight >= page.ActualHeight * 0.6, $"{route}: a listagem deveria ser aberta e ocupar a tela ({list.ActualHeight:F0} de {page.ActualHeight:F0}).");
                }
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria cadastros: o item de menu Nova conta mostra Contas com o pop-up aberto, e clicar de novo abre outro", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Centro", "Centro");
            var window = OpenMunicipal(fixture, storageRoot, "cad-nova-conta", Appearance.Light);
            try
            {
                LoginObs(window);
                FrameworkElement? content = null;
                for (var round = 1; round <= 2; round++)
                {
                    var opened = 0;
                    InspectModal<FormDialog>(window, dialog =>
                    {
                        opened++;
                        Require(dialog.Title == "Nova conta OBS" && Descendants<PasswordBox>(dialog).Count() == 2, "O pop-up aberto pela rota deveria ser o da conta nova.");
                    }, () => content = Show(window, "users:create", 1280, 900));
                    Require(opened == 1 && window.ViewModel.Route == "users:create" && ListNamed(content!, "Contas OBS").IsVisible, $"Rodada {round}: a rota Nova conta deveria mostrar a listagem de contas com um pop-up.");
                }
                Show(window, "users:accounts", 1280, 900);
                Require(!window.OwnedWindows.OfType<FormDialog>().Any(), "Contas não deveria abrir o pop-up sozinha.");
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria cadastros: pop-up de cadastro aberto fecha sozinho quando a sessão termina, sem deixar senha digitada", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Centro", "Centro");
            var window = OpenMunicipal(fixture, storageRoot, "cad-sessao", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                Show(window, "users:accounts", 1280, 900);
                PasswordBox? password = null; var closedWithoutSaving = false;
                InspectHeaderForm(window, "Nova conta", dialog =>
                {
                    password = Secret(dialog, "Senha provisória da nova conta"); password.Password = "provisoria-123";
                    vm.LogoutCommand.Execute(null);
                    PumpUntil(() => !dialog.IsVisible);
                    closedWithoutSaving = !dialog.Saved;
                });
                Require(closedWithoutSaving && !vm.IsAuthenticated && password!.Password.Length == 0, "Sair com o pop-up aberto deveria fechá-lo sem salvar e apagar a senha digitada.");
            }
            finally { CloseWindow(window); }
        });
        Check("Secretaria cadastros: \"Ver ficha\" acionado duas vezes seguidas abre uma ficha só", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Centro", "Centro"); fixture.AddAnnouncement("Para equipes", "Texto", "staff");
            var window = OpenMunicipal(fixture, storageRoot, "cad-ficha-unica", Appearance.Light);
            // Um temporizador (e não um repost em ApplicationIdle, que nunca deixaria o PumpUntil chegar ao SystemIdle) olha as fichas abertas e as fecha.
            var watch = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "communication", 1280, 900);
                var list = ListNamed(content, "Comunicados da Secretaria");
                SelectRows(window, list, vm.MyAnnouncements.Single());
                var seen = new HashSet<ItemDetailsWindow>(); var most = 0;
                watch.Tick += (_, _) =>
                {
                    var open = window.OwnedWindows.OfType<ItemDetailsWindow>().ToArray();
                    most = Math.Max(most, open.Length);
                    foreach (var details in open) { seen.Add(details); details.Close(); }
                };
                watch.Start();
                // As leituras ficam presas enquanto os dois cliques acontecem: o segundo chega com a primeira ficha ainda sendo montada.
                fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var view = BalloonButton(list, "Ver ficha");
                view.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, view)); view.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, view));
                fixture.PauseReads.TrySetResult();
                PumpUntil(() => seen.Count >= 1);
                var end = DateTime.UtcNow.AddMilliseconds(700); PumpUntil(() => DateTime.UtcNow > end);
                Require(seen.Count == 1 && most == 1, $"Dois cliques em Ver ficha abriram {seen.Count} fichas.");
            }
            finally { watch.Stop(); fixture.PauseReads?.TrySetResult(); CloseWindow(window); }
        });
        Check("Secretaria cadastros: com uma operação em andamento, as ações do balão e o \"+ Novo\" do cabeçalho ficam desabilitados e voltam ao terminar", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS Centro", "Centro");
            var window = OpenMunicipal(fixture, storageRoot, "cad-ocupado", Appearance.Light);
            Task? running = null;
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "users:accounts", 1280, 900);
                var list = ListNamed(content, "Contas OBS");
                SelectRows(window, list, vm.Staff.First(account => account.IsActive));
                var deactivate = BalloonButton(list, "Desativar conta"); var create = HeaderButton(window, "Nova conta");
                Require(deactivate.IsVisible && deactivate.IsEnabled && create.IsEnabled, "Sem operação em andamento, Desativar e Nova conta deveriam estar habilitados.");
                // Uma ação do usuário presa na leitura: a confirmação de Desativar não pode ser aceita para depois ser descartada em silêncio.
                fixture.PauseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
                running = vm.LoadHistoryAsync();
                Require(!vm.CanOperate && !deactivate.IsEnabled && !create.IsEnabled, "Com uma operação em andamento, as ações deveriam ficar desabilitadas.");
                fixture.PauseReads.TrySetResult(); Pump(running); window.UpdateLayout();
                Require(vm.CanOperate && deactivate.IsEnabled && create.IsEnabled && list.SelectedItems.Count == 1, "Terminada a operação, as ações e a seleção deveriam voltar.");
            }
            finally { fixture.PauseReads?.TrySetResult(); if (running != null) Pump(running); CloseWindow(window); }
        });
        Check("Secretaria cadastros: lista carregando mostra o passo com o indicador girando; vazio e busca sem resultado só com o texto", () =>
        {
            var source = new System.Collections.ObjectModel.ObservableCollection<string>();
            var list = new ListBox { ItemsSource = source };
            var empty = new ListEmptyText { List = list, EmptyText = "Carregando as contas…", NoMatchText = "Nenhuma conta encontrada para esta busca." };
            var host = new Window { Content = new Grid { Children = { list, empty } }, Width = 400, Height = 300, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            try
            {
                host.Show(); host.UpdateLayout();
                bool Spinning() => empty.Inlines.OfType<InlineUIContainer>().Any(container => container.Child is System.Windows.Shapes.Path);
                Require(empty.IsVisible && Spinning() && empty.Inlines.OfType<Run>().Any(run => run.Text == "Carregando as contas…"), "Carregando deveria mostrar o indicador e o passo escrito.");
                empty.EmptyText = "Nenhuma conta OBS cadastrada.";
                Require(empty.IsVisible && !Spinning() && empty.Text == "Nenhuma conta OBS cadastrada.", "Vazio deveria mostrar só o texto, sem indicador.");
                source.Add("Ana"); host.UpdateLayout();
                Require(!empty.IsVisible, "Com itens, o texto de vazio deveria sumir.");
                CollectionViewSource.GetDefaultView(source).Filter = _ => false; host.UpdateLayout();
                Require(empty.IsVisible && empty.Text == "Nenhuma conta encontrada para esta busca.", "Busca sem resultado deveria dizer que nada foi encontrado.");
            }
            finally { host.Close(); }
        });
    }
}
