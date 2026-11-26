using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>Cabeçalho da tela: título em caminho, pesquisa em linha, botão Filtros, ações da tela e janela estreita.</summary>
internal static partial class Program
{
    private static readonly (string Route, string Label)[] OperationalTitles =
    [
        ("home", "Painel da UBS"), ("agenda", "Agenda"), ("announcements", "Comunicados"), ("slots", "Vaga Relâmpago"),
        ("patients:list", "Listagem • Pacientes"), ("patients:register", "Cadastro • Pacientes"), ("family", "Família • Pacientes"),
        ("slots:live", "Em andamento • Vaga Relâmpago"), ("slots:closed", "Encerradas • Vaga Relâmpago"),
        ("indicators", "Desempenho • Indicadores"), ("alerts", "Alertas • Indicadores"),
        ("unit-settings:schedule", "Grade semanal • Informações da UBS"), ("unit-settings:blocks", "Bloqueios • Informações da UBS"),
        ("settings", "Configurações")
    ];

    private static readonly (string Route, string Label)[] MunicipalTitles =
    [
        ("home", "Visão geral"), ("communication", "Comunicação"),
        ("units:registry", "Cadastro de UBS • Unidades"), ("network", "Mapa da rede • Unidades"), ("capacity", "Capacidade • Unidades"),
        ("specialties:catalog", "Catálogo • Especialidades"), ("specialties:demand", "Demanda • Especialidades"),
        ("indicators", "Desempenho • Indicadores"), ("slots", "Vaga Relâmpago • Indicadores"), ("reports", "Relatórios e metas • Indicadores"),
        ("users:history", "Histórico • Usuários"), ("settings", "Configurações")
    ];

    private static void RunHeaderChecks(string storageRoot)
    {
        foreach (var (profile, titles) in new[] { (DesktopProfile.Operational, OperationalTitles), (DesktopProfile.Municipal, MunicipalTitles) })
        Check($"{profile.Name}: o cabeçalho mostra o caminho \"Subtela • Área\" nas rotas de subtela e só o título nas demais", () =>
        {
            var window = OpenHidden(profile, storageRoot, "header-title-" + profile.Id);
            try
            {
                var header = (FrameworkElement)window.FindName("PageHeader");
                foreach (var (route, label) in titles)
                {
                    window.ViewModel.Navigate(route);
                    Layout(window, 1280, 740);
                    Require(window.ViewModel.PageLabel == label, $"Rota {route}: o título deveria ser \"{label}\" e foi \"{window.ViewModel.PageLabel}\".");
                    Require(Descendants<TextBlock>(header).Any(text => text.IsVisible && text.Text == label), $"Rota {route}: o cabeçalho não mostra \"{label}\".");
                }
            }
            finally { CloseWindow(window); }
        });
        Check("Cabeçalho: sem o chip da UBS nem de \"Gestão conectada\"; Agenda e o catálogo só com pesquisa não têm botão Filtros; Pacientes e UBS têm", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS do cabeçalho");
            foreach (var municipal in new[] { false, true })
            {
                var window = municipal ? OpenMunicipal(fixture, storageRoot, "header-real-municipal") : OpenOperational(fixture, storageRoot, "header-real-obs");
                try
                {
                    var vm = LoginObs(window);
                    var header = (FrameworkElement)window.FindName("PageHeader");
                    var filters = (Button)window.FindName("FiltersButton");
                    int Searches() => Descendants<TextBox>(header).Count(box => box.IsVisible && AutomationProperties.GetName(box).StartsWith("Pesquisar"));
                    foreach (var route in municipal ? new[] { "home", "specialties:catalog", "units:registry" } : new[] { "home", "agenda", "patients:list" })
                    {
                        Show(window, route);
                        Require(!Descendants<TextBlock>(header).Any(text => text.Text.Contains("Gestão") || (vm.UnitLabel.Length > 0 && text.Text.Contains(vm.UnitLabel))), $"Rota {route}: o chip da UBS ou de \"Gestão conectada\" continua no cabeçalho.");
                    }
                    var searchOnly = municipal ? "specialties:catalog" : "agenda";
                    Show(window, searchOnly);
                    Require(Searches() == 1 && !filters.IsVisible, $"{searchOnly}: deveria mostrar só o campo Pesquisar em linha, sem o botão Filtros (campos {Searches()}, botão {filters.IsVisible}).");
                    var realFilters = municipal ? "units:registry" : "patients:list";
                    Show(window, realFilters);
                    Require(filters.IsVisible && Searches() == 0, $"{realFilters}: deveria mostrar o botão Filtros e nenhum campo Pesquisar em linha (campos {Searches()}, botão {filters.IsVisible}).");
                    if (!municipal)
                    {
                        // "Limpar filtros" continua valendo para os grupos do pop-up.
                        vm.ObsRegistrySearch = "texto que não existe";
                        var popup = OpenPageFilters(window);
                        InvokeButton(FindActionButton(popup, "Limpar filtros"));
                        ((Popup)window.FindName("FiltersPopup")).IsOpen = false;
                        Require(vm.ObsRegistrySearch.Length == 0, "Limpar filtros não limpou a busca do pop-up.");
                    }
                }
                finally { CloseWindow(window); }
            }
        });
        Check("Cabeçalho: pesquisa em linha com a lupa à direita, botão Filtros só com filtro real, ações da tela somem com a tela e voltam com ela", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "header-stage");
            try
            {
                var vm = window.ViewModel;
                vm.Navigate("patients:register");
                var stage = HeaderStage(window, "list");
                var list = new ListBox { ItemsSource = new[] { "Ana", "Bruno" } };
                var action = new Button { Content = "+ Novo teste", Padding = new Thickness(14, 7, 14, 7) };
                AutomationProperties.SetName(action, "Novo teste");
                stage.Children.Add(list);
                stage.Children.Add(new PageFilterGroup { Label = "Lista de teste", SearchFields = "Length", ItemsTarget = list });
                stage.Children.Add(new PageActions { Actions = action });
                var header = (FrameworkElement)window.FindName("PageHeader");
                var filters = (Button)window.FindName("FiltersButton");
                Layout(window, 1280, 740);
                Require(!action.IsVisible && !Descendants<TextBox>(header).Any() && !filters.IsVisible, "Em outra subtela nada da tela deveria aparecer no cabeçalho.");
                vm.Navigate("patients:list"); Layout(window, 1280, 740);
                Require(vm.PageLabel == "Listagem • Pacientes", "Título da subtela incorreto: " + vm.PageLabel);
                var search = Descendants<TextBox>(header).Single(box => box.IsVisible);
                Require(AutomationProperties.GetName(search) == "Pesquisar: Lista de teste" && !filters.IsVisible, "Tela só com pesquisa: campo sem nome acessível ou botão Filtros visível.");
                Require(Descendants<TextBlock>(search).Any(text => text.IsVisible && text.Text == "Pesquisar"), "O campo vazio deveria mostrar o texto-guia \"Pesquisar\".");
                var magnifier = Descendants<System.Windows.Shapes.Path>(search).Single(path => path.Data is not null);
                Require(magnifier.TranslatePoint(new Point(0, 0), search).X > search.ActualWidth / 2, "A lupa deveria ficar à direita do campo.");
                Require(search.Height == 36 && search.ActualHeight == 36, "O campo de pesquisa deveria ter 36 px de altura.");
                double Left(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), header).X;
                double Right(FrameworkElement element) => Left(element) + element.ActualWidth;
                Require(action.IsVisible && Window.GetWindow(action) == window && Descendants<Button>(header).Contains(action), "As ações da tela deveriam estar no cabeçalho.");
                Require(Math.Abs(Right(action) - (header.ActualWidth - 32)) < 1.5 && Left(search) < Left(action), $"As ações deveriam ficar no canto direito (borda {Right(action):F1} de {header.ActualWidth - 32:F1}).");
                Require(Nora.Desktop.Generics.ScreenSearch.Find(header, window, "pesquisar").Count == 0 && Nora.Desktop.Generics.ScreenSearch.Find(header, window, "Novo teste").Count == 1, "Ctrl + F não deveria achar o texto-guia do campo, e deveria achar o botão da tela.");
                search.Text = "5";
                Require(list.Items.Count == 1, "O campo do cabeçalho não filtrou a lista da tela.");
                Require(Nora.Desktop.Generics.ScreenSearch.Find(header, window, "resultado").Count == 0, "Ctrl + F não deveria achar o aviso de resultados do campo.");
                search.Text = "";
                Require(list.Items.Count == 2, "Apagar o texto deveria restaurar a lista.");

                // Mais um grupo em linha e um filtro real: ordem no cabeçalho = filtros em linha, botão Filtros, ações.
                var period = new TextBlock { Text = "Período de teste", VerticalAlignment = VerticalAlignment.Center };
                var real = new TextBox();
                AutomationProperties.SetName(real, "Campo do filtro real");
                stage.Children.Add(new PageFilterGroup { Label = "Período", Inline = true, Fields = period });
                stage.Children.Add(new PageFilterGroup { Label = "Filtro real", FilterKey = "teste", Fields = new StackPanel { Children = { real } } });
                Layout(window, 1280, 740);
                Require(filters.IsVisible && period.IsVisible && Descendants<TextBlock>(header).Contains(period), "Com um filtro real o botão Filtros deveria aparecer e o grupo em linha ficar no cabeçalho.");
                Require(Left(search) < Left(period) && Right(period) < Left(filters) && Right(filters) < Left(action), $"Ordem incorreta: pesquisa {Left(search):F0}, período {Left(period):F0}, Filtros {Left(filters):F0}, ação {Left(action):F0}.");
                var body = OpenPageFilters(window);
                Require(Descendants<TextBox>(body).Any(box => AutomationProperties.GetName(box) == "Campo do filtro real") && !Descendants<TextBox>(body).Any(box => AutomationProperties.GetName(box).StartsWith("Pesquisar")) && !VisibleTexts(body).Contains("Período de teste"), "O pop-up deveria ter só o filtro real, sem a pesquisa nem o grupo em linha.");
                ((Popup)window.FindName("FiltersPopup")).IsOpen = false;

                // Janela estreita: a pesquisa encolhe até o mínimo e depois o título corta; os botões não encolhem.
                var title = Descendants<TextBlock>(header).First(text => text.Text == vm.PageLabel);
                double searchWide = search.ActualWidth, filtersWide = filters.ActualWidth, actionWide = action.ActualWidth, titleWide = title.ActualWidth;
                Require(Math.Abs(searchWide - 260) < 1, "A pesquisa deveria ter 260 px com espaço de sobra: " + searchWide);
                // Larguras escolhidas pela soma do que o cabeçalho precisa (título, pesquisa, grupo em linha, Filtros, ação e 4 espaços de 12).
                var needed = titleWide + searchWide + period.ActualWidth + filtersWide + actionWide + 48;
                var chrome = ((FrameworkElement)window.Content).ActualWidth - header.ActualWidth + 64;
                Layout(window, (int)(needed - 40 + chrome), 740);
                Require(search.ActualWidth is > 141 and < 259 && Math.Abs(title.ActualWidth - titleWide) < 1 && Math.Abs(filters.ActualWidth - filtersWide) < 0.5 && Math.Abs(action.ActualWidth - actionWide) < 0.5, $"Pouco aperto: só a pesquisa deveria encolher (pesquisa {search.ActualWidth:F0}, título {title.ActualWidth:F0}/{titleWide:F0}).");
                Layout(window, (int)(needed - 160 + chrome), 740);
                Require(Math.Abs(search.ActualWidth - 140) < 1 && title.ActualWidth < titleWide - 1 && Math.Abs(filters.ActualWidth - filtersWide) < 0.5 && Math.Abs(action.ActualWidth - actionWide) < 0.5, $"Janela mínima: a pesquisa deveria ir ao mínimo e o título cortar (pesquisa {search.ActualWidth:F0}, título {title.ActualWidth:F0}/{titleWide:F0}).");
                Require(filters.IsVisible && action.IsVisible && Left(filters) > Right(title) && Right(action) <= header.ActualWidth - 31 && Left(search) >= Right(title), "Nenhum botão pode sumir nem sobrepor o título.");
                Layout(window, 1280, 740);

                // Trocar de subtela leva tudo embora; voltar traz de volta, com o mesmo conteúdo.
                vm.Navigate("patients:register"); Layout(window, 1280, 740);
                Require(!Descendants<TextBox>(header).Any() && !Descendants<Button>(header).Contains(action) && !Descendants<TextBlock>(header).Contains(period) && !filters.IsVisible, "As ações, a pesquisa e o botão Filtros deveriam sumir com a tela.");
                vm.Navigate("patients:list"); Layout(window, 1280, 740);
                Require(Descendants<Button>(header).Contains(action) && Descendants<TextBox>(header).Count(box => box.IsVisible) == 1 && filters.IsVisible, "Ao voltar à tela, as ações, a pesquisa e o botão Filtros deveriam reaparecer.");
            }
            finally { CloseWindow(window); }
        });
        Check("Cabeçalho: PageActions e PageFilterGroup em linha declarados em XAML chegam ao cabeçalho e o botão da ação funciona", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "header-xaml");
            try
            {
                window.ViewModel.Navigate("patients:list");
                var stage = HeaderStage(window, "list");
                var declared = (StackPanel)System.Windows.Markup.XamlReader.Parse(
                    "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:views=\"clr-namespace:Nora.Desktop.Views;assembly=Nora.Desktop\">" +
                    "<views:PageFilterGroup Label=\"Período\" Inline=\"True\"><ComboBox AutomationProperties.Name=\"Período em XAML\" MinWidth=\"120\"><ComboBoxItem Content=\"Mês\" IsSelected=\"True\" /></ComboBox></views:PageFilterGroup>" +
                    "<views:PageActions><Button Content=\"+ Novo em XAML\" AutomationProperties.Name=\"Novo em XAML\" /></views:PageActions></StackPanel>");
                var clicks = 0;
                ((Button)((PageActions)declared.Children[1]).Actions!).Click += (_, _) => clicks++;
                stage.Children.Add(declared);
                var header = (FrameworkElement)window.FindName("PageHeader");
                Layout(window, 1280, 740);
                var combo = Descendants<ComboBox>(header).Single(box => AutomationProperties.GetName(box) == "Período em XAML");
                var button = Descendants<Button>(header).Single(item => AutomationProperties.GetName(item) == "Novo em XAML");
                Require(combo.IsVisible && button.IsVisible && !((Button)window.FindName("FiltersButton")).IsVisible, "O grupo em linha e a ação declarados em XAML deveriam estar no cabeçalho, sem o botão Filtros.");
                Require(combo.TranslatePoint(new Point(0, 0), header).X < button.TranslatePoint(new Point(0, 0), header).X, "O filtro em linha deveria ficar à esquerda da ação.");
                InvokeButton(button);
                Require(clicks == 1, "O botão hospedado no cabeçalho não respondeu ao clique.");
            }
            finally { CloseWindow(window); }
        });
        Check("Cabeçalho: ligações por ElementName entre a tela e o conteúdo levado ao cabeçalho continuam valendo nos dois sentidos", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "header-element-name");
            try
            {
                window.ViewModel.Navigate("patients:list");
                var stage = HeaderStage(window, "list");
                // Como as telas fazem: a tela segue o Mode do alternador que mora no cabeçalho, e o botão do cabeçalho lê um texto da tela.
                var declared = (StackPanel)System.Windows.Markup.XamlReader.Parse(
                    "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:views=\"clr-namespace:Nora.Desktop.Views;assembly=Nora.Desktop\">" +
                    "<TextBlock x:Name=\"Origem\" Text=\"antes\" /><TextBlock x:Name=\"Seguidor\" Text=\"{Binding Mode, ElementName=Alternador}\" />" +
                    "<views:PageActions><StackPanel Orientation=\"Horizontal\"><views:ViewModeToggle x:Name=\"Alternador\" ScreenKey=\"teste-element-name\" />" +
                    "<Button Content=\"{Binding Text, ElementName=Origem}\" AutomationProperties.Name=\"Botão ligado à tela\" /></StackPanel></views:PageActions></StackPanel>");
                stage.Children.Add(declared);
                var header = (FrameworkElement)window.FindName("PageHeader");
                Layout(window, 1280, 740); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var toggle = Descendants<ViewModeToggle>(header).Single();
                var bound = Descendants<Button>(header).Single(item => AutomationProperties.GetName(item) == "Botão ligado à tela");
                var origin = (TextBlock)declared.FindName("Origem"); var follower = (TextBlock)declared.FindName("Seguidor");
                Require(Equals(bound.Content, "antes") && follower.Text == "Cards", $"As ligações deveriam valer com o conteúdo no cabeçalho (botão \"{bound.Content}\", tela \"{follower.Text}\").");
                origin.Text = "depois"; InvokeButton(toggle);
                Require(Equals(bound.Content, "depois") && follower.Text == "Charts", $"As ligações deveriam acompanhar as mudanças (botão \"{bound.Content}\", tela \"{follower.Text}\").");
                window.ViewModel.Navigate("patients:register"); Layout(window, 1280, 740);
                window.ViewModel.Navigate("patients:list"); Layout(window, 1280, 740); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                origin.Text = "de novo"; InvokeButton(toggle);
                Require(Equals(bound.Content, "de novo") && follower.Text == "Cards", $"Depois de sair e voltar à tela as ligações deveriam continuar (botão \"{bound.Content}\", tela \"{follower.Text}\").");
            }
            finally { CloseWindow(window); }
        });
        Check("Cabeçalho: \"Limpar filtros\" também apaga a pesquisa em linha da tela, como antes do cabeçalho novo", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "header-clear-search");
            try
            {
                window.ViewModel.Navigate("patients:list");
                var stage = HeaderStage(window, "list");
                var list = new ListBox { ItemsSource = new[] { "Ana", "Bruno" } };
                stage.Children.Add(list);
                stage.Children.Add(new PageFilterGroup { Label = "Lista de teste", SearchFields = "Length", ItemsTarget = list });
                stage.Children.Add(new PageFilterGroup { Label = "Outro filtro", FilterKey = "teste", Fields = new TextBox { Text = "valor" } });
                var header = (FrameworkElement)window.FindName("PageHeader");
                Layout(window, 1280, 740);
                var search = Descendants<TextBox>(header).Single(box => box.IsVisible);
                search.Text = "5";
                Require(list.Items.Count == 1 && ((Button)window.FindName("FiltersButton")).IsVisible, "A pesquisa deveria filtrar e o botão Filtros deveria aparecer pelo outro grupo.");
                InvokeButton(FindActionButton(OpenPageFilters(window), "Limpar filtros"));
                ((Popup)window.FindName("FiltersPopup")).IsOpen = false;
                Require(search.Text.Length == 0 && list.Items.Count == 2, "Limpar filtros deveria apagar a pesquisa em linha e devolver a lista inteira.");
            }
            finally { CloseWindow(window); }
        });
        Check("Cabeçalho: fora da janela principal as ações e a pesquisa aparecem na própria tela", () =>
        {
            var button = new Button { Content = "Ação solta" };
            var list = new ListBox();
            var panel = new StackPanel();
            panel.Children.Add(list);
            panel.Children.Add(new PageActions { Actions = button });
            panel.Children.Add(new PageFilterGroup { Label = "Solta", SearchFields = "Length", ItemsTarget = list });
            var window = new Window { Content = panel, Opacity = 0, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(button.IsVisible && FindAncestor<PageActions>(button) is not null, "Fora da janela principal o botão deveria aparecer onde foi declarado.");
                Require(Descendants<TextBox>(panel).Count(box => box.IsVisible) == 1, "Fora da janela principal o campo Pesquisar deveria aparecer na própria tela.");
            }
            finally { window.Close(); }
        });
    }

    /// <summary>Esvazia a área da tela e põe nela um painel que só aparece na subtela indicada: o que ele declara é o único conteúdo da tela.</summary>
    private static StackPanel HeaderStage(ShellWindow window, string visibleOn)
    {
        var container = (Grid)window.FindName("PageContainer");
        foreach (UIElement child in container.Children) child.Visibility = Visibility.Collapsed;
        var stage = new StackPanel();
        SubPage.SetKey(stage, visibleOn);
        container.Children.Add(stage);
        return stage;
    }
}
