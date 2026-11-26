using System.Windows;
using System.Windows.Input;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private DesktopPreferences preferences;
    private readonly PreferencesService storage;
    private readonly ThemeService themes;
    private string page;
    private string status;
    private bool expanded;
    private double sidebarWidth;
    public DesktopProfile Profile { get; }
    public WorkspaceViewModel Workspace { get; }
    public IReadOnlyList<NavigationItem> Navigation { get; }
    public bool IsMunicipal => Profile.IsMunicipal;
    public bool IsOperational => !IsMunicipal;
    public string WorkspaceName => IsMunicipal ? "GESTÃO / SECRETARIA" : "OPERAÇÃO UBS";
    public string NavigationHeading => IsMunicipal ? "GESTÃO DA REDE" : "OPERAÇÃO DA UNIDADE";
    public string Status { get => status; private set { status = value; Notify(); } }
    public bool HasPreferenceError => storage.Error != null;
    public string Page { get => page; private set { page = value; MarkSelectedNavigation(); Notify(); Notify(nameof(IsHome)); Notify(nameof(IsSettings)); Notify(nameof(PageLabel)); Notify(nameof(SectionSummary)); } }
    public bool IsHome => Page != "settings" && !ShowPasswordRequired && !ShowCompactAccess;
    public bool IsSettings => Page == "settings" && !ShowPasswordRequired && !ShowCompactAccess;
    /// <summary>Senha provisória: a janela mostra só a definição da nova senha (e Sair), sem menu nem dados.</summary>
    public bool ShowPasswordRequired => Workspace.MustChangePassword;
    /// <summary>Título do cabeçalho: "Subtela • Área" nas rotas de subtela (Listagem • Pacientes) e só o título nas demais (Painel da UBS, Agenda, Configurações).</summary>
    public string PageLabel => ShowPasswordRequired ? "Definir nova senha" : Page == "settings" ? "Configurações" : RouteLabel(Route);
    public string SectionSummary => ShowPasswordRequired ? "Troca obrigatória da senha provisória" : Page switch
    {
        "home" => IsMunicipal ? "Panorama e prioridades da rede" : "Visão operacional e prioridades do dia",
        "units" => "Acompanhe as unidades da rede",
        "specialties" => "Lista da rede, demanda e espera por especialidade",
        "network" => "Distribuição e carga por região",
        "capacity" => "Capacidade e uso das agendas",
        "reports" => "Relatórios, metas e exportação",
        "communication" => "Comunicados para as equipes e os pacientes das unidades",
        "users" => "Acessos e histórico de ações",
        "agenda" => "Consulte períodos, horários e situações da agenda",
        "patients" => "Pesquise, cadastre e consulte pacientes autorizados",
        "slots" => "Vagas oferecidas automaticamente",
        "indicators" => "Acompanhe o desempenho da unidade",
        "alerts" => "Priorize situações que exigem atenção",
        "unit-settings" => "Profissionais, consultórios, grade de horários e bloqueios",
        "family" => "Pedidos de vínculo entre responsáveis e dependentes",
        "announcements" => "Comunicados da Secretaria para a equipe",
        "settings" => "Aparência, atalhos e preferências deste computador",
        _ => ""
    };
    /// <summary>Barra lateral aberta. Fechada, ela e o divisor somem e a tela ocupa a largura toda; ao reabrir volta à largura salva.</summary>
    public bool SidebarExpanded
    {
        get => expanded;
        set
        {
            if (expanded == value) return;
            expanded = value;
            Notify(); Notify(nameof(SidebarColumnWidth)); Notify(nameof(SidebarSplitterWidth));
            if (RememberSidebar) { preferences.SidebarExpanded = value; Persist(); }
        }
    }
    public double SidebarWidth
    {
        get => sidebarWidth;
        set
        {
            var normalized = Math.Clamp(value, 208, 380);
            if (Math.Abs(sidebarWidth - normalized) < 0.5) return;
            sidebarWidth = normalized;
            Notify();
            Notify(nameof(SidebarColumnWidth));
            if (RememberSidebar) { preferences.SidebarWidth = normalized; Persist(); }
        }
    }
    /// <summary>Largura da coluna da barra lateral: a largura salva quando aberta e zero quando recolhida.</summary>
    public GridLength SidebarColumnWidth
    {
        get => new(SidebarExpanded ? SidebarWidth : 0, GridUnitType.Pixel);
        set
        {
            // Recolhida, a coluna vale zero: esse valor não é uma largura escolhida pela pessoa e não pode ser guardado.
            if (SidebarExpanded && value.IsAbsolute) SidebarWidth = value.Value;
        }
    }
    /// <summary>Largura da alça de arrasto entre a barra lateral e a tela: some junto com a barra.</summary>
    public GridLength SidebarSplitterWidth => new(SidebarExpanded ? 6 : 0, GridUnitType.Pixel);
    public Appearance Appearance => preferences.Appearance;
    public bool LightSelected => Appearance == Appearance.Light;
    public bool DarkSelected => Appearance == Appearance.Dark;
    public bool SystemSelected => Appearance == Appearance.System;
    public bool ShowSidebarTips { get => preferences.ShowSidebarTips; set { preferences.ShowSidebarTips = value; Changed(); } }
    public bool ConfirmClose { get => preferences.ConfirmClose; set { preferences.ConfirmClose = value; Changed(); } }
    public bool SaveNavigation { get => preferences.SaveNavigation; set { preferences.SaveNavigation = value; if (value) preferences.LastPage = Page; Changed(); } }
    public bool Animations { get => preferences.Animations; set { preferences.Animations = value; Changed(); } }
    public bool RememberSidebar { get => preferences.RememberSidebar; set { preferences.RememberSidebar = value; if (value) preferences.SidebarWidth = SidebarWidth; Changed(); } }
    public bool FullScreen { get => preferences.FullScreen; set { preferences.FullScreen = value; Changed(); } }
    public ICommand NavigateCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand MainMenuCommand { get; }
    public ICommand AppearanceCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand RestoreCommand { get; }
    public ShellViewModel(DesktopProfile profile, PreferencesService storage, ThemeService themes, NoraApiClient? api = null, IObsDeviceStore? devices = null)
    {
        Profile = profile;
        Workspace = new(profile, api, devices);
        Workspace.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(WorkspaceViewModel.AnnouncementBadgeCount)) { SetBadge("announcements", Workspace.AnnouncementBadgeCount); return; }
            if (change.PropertyName == nameof(WorkspaceViewModel.AlertBadgeCount)) { SetBadge("alerts", Workspace.AlertBadgeCount); return; }
            if (change.PropertyName != nameof(WorkspaceViewModel.MustChangePassword)) return;
            foreach (var name in new[] { nameof(ShowPasswordRequired), nameof(IsHome), nameof(IsSettings), nameof(PageLabel), nameof(SectionSummary), nameof(CanGoBack), nameof(CanGoForward) }) Notify(name);
        };
        this.storage = storage;
        this.themes = themes;
        preferences = storage.Load();
        status = storage.Error ?? "Entre na conta para carregar os dados da API.";
        page = preferences.SaveNavigation ? preferences.LastPage : "home";
        Workspace.Section = page;
        expanded = !preferences.RememberSidebar || preferences.SidebarExpanded;
        sidebarWidth = !preferences.RememberSidebar ? 264 : Math.Clamp(preferences.SidebarWidth, 208, 380);
        Navigation = profile.IsMunicipal ? BuildMunicipalNavigation() : BuildOperationalNavigation();
        subPage = null;
        activeArea = AreaFor(page) is { HasChildren: true } area ? area : null;
        if (page == "slots" && !profile.IsMunicipal) Workspace.SlotPanelScope = subPage ?? "";
        MarkSelectedNavigation();
        NavigateCommand = new RelayCommand(p => Navigate(p?.ToString() ?? "home"));
        BackCommand = new RelayCommand(_ => GoBack(), _ => CanGoBack);
        ForwardCommand = new RelayCommand(_ => GoForward(), _ => CanGoForward);
        MainMenuCommand = new RelayCommand(_ => ShowMainMenu());
        AppearanceCommand = new RelayCommand(p => { if (Enum.TryParse<Appearance>(p?.ToString(), out var mode)) SetAppearance(mode); });
        ToggleSidebarCommand = new RelayCommand(_ => SidebarExpanded = !SidebarExpanded);
        RestoreCommand = new RelayCommand(_ => RestoreDefaults());
        themes.Apply(preferences.Appearance);
    }
    private void MarkSelectedNavigation()
    {
        if (Navigation is null) return;
        foreach (var item in Navigation)
        {
            item.IsSelected = item.Id == page || item.Children.Any(child => child.Id == page);
            foreach (var child in item.Children) child.IsSelected = child.Id == page && (child.Sub ?? "") == (subPage ?? "");
        }
    }
    private void SetBadge(string id, int count)
    {
        foreach (var item in Navigation)
        {
            if (item.Id == id && !item.HasChildren) item.Badge = count;
            if (item.Children.FirstOrDefault(child => child.Id == id) is { } child) { child.Badge = count; item.Badge = item.Children.Sum(other => other.Badge); }
        }
    }

    // ---- Navegação em dois níveis e histórico (voltar e avançar, na barra de título) ----
    private const int HistoryLimit = 50;
    private string? subPage;
    private NavigationItem? activeArea;
    private readonly List<string> history = [];
    private readonly List<string> forward = [];
    /// <summary>Parte da página mostrada pela subaba ("list", "register"...); nulo quando a página não tem subabas.</summary>
    public string? SubPage { get => subPage; private set { subPage = value; Notify(); } }
    /// <summary>Área principal aberta na barra lateral: com ela, a barra mostra as subabas e o item "Voltar ao menu".</summary>
    public NavigationItem? ActiveArea { get => activeArea; private set { if (ReferenceEquals(activeArea, value)) return; activeArea = value; Notify(); Notify(nameof(SidebarItems)); Notify(nameof(ShowingSubmenu)); Notify(nameof(SidebarHeading)); } }
    public bool ShowingSubmenu => ActiveArea is not null;
    /// <summary>Itens da barra lateral: as áreas principais ou as subabas da área aberta.</summary>
    public IReadOnlyList<NavigationItem> SidebarItems => ActiveArea?.Children ?? Navigation;
    public string SidebarHeading => ActiveArea?.Title.ToUpperInvariant() ?? NavigationHeading;
    public bool CanGoBack => history.Count > 0 && !ShowPasswordRequired;
    public bool CanGoForward => forward.Count > 0 && !ShowPasswordRequired;
    public string Route => SubPage is null ? Page : $"{Page}:{SubPage}";
    /// <summary>A página tem subtelas, mas nenhuma foi escolhida (atalho, página restaurada ou chamada direta): todas aparecem empilhadas e a página precisa rolar.</summary>
    public bool ShowsAllSubPages => SubPage is null && Navigation.Any(area => area.Children.Any(child => child.Id == Page && child.Sub is not null));
    public string BackTip => history.Count > 0 ? $"Voltar para {RouteTitle(history[^1])} (Alt + ←)" : "Nenhuma página anterior";
    public string ForwardTip => forward.Count > 0 ? $"Avançar para {RouteTitle(forward[^1])} (Alt + →)" : "Nenhuma página para avançar";
    private IEnumerable<NavigationItem> AllItems => Navigation.SelectMany(item => item.HasChildren ? item.Children.Prepend(item) : [item]);
    private NavigationItem? AreaFor(string id) => Navigation.FirstOrDefault(item => item.Id == id && !item.HasChildren) ?? Navigation.FirstOrDefault(item => item.Children.Any(child => child.Id == id));
    private string? DefaultSub(string id) => AreaFor(id)?.Children.FirstOrDefault(child => child.Id == id)?.Sub;
    private NavigationItem? RouteItem(string target, string? sub) =>
        AllItems.FirstOrDefault(item => item.Id == target && (item.Sub ?? "") == (sub ?? "") && !item.HasChildren) ?? AllItems.FirstOrDefault(item => item.Id == target);
    private string RouteTitle(string route)
    {
        var (target, sub) = SplitRoute(route);
        return target == "settings" ? "Configurações" : RouteItem(target, sub)?.Title ?? "Início";
    }
    /// <summary>Caminho da rota para o título do cabeçalho: item do menu que é subtela de uma área vira "Subtela • Área".</summary>
    private string RouteLabel(string route)
    {
        var (target, sub) = SplitRoute(route);
        if (target == "settings") return "Configurações";
        if (RouteItem(target, sub) is not { } item) return "Início";
        var area = Navigation.FirstOrDefault(parent => parent.Children.Any(child => ReferenceEquals(child, item)));
        return area is null ? item.Title : $"{item.Title} • {area.Title}";
    }
    private static (string Target, string? Sub) SplitRoute(string route)
    {
        var parts = route.Split(':', 2);
        return (parts[0], parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null);
    }
    /// <summary>"Voltar ao menu": a barra lateral volta às áreas principais sem trocar a página aberta.</summary>
    public void ShowMainMenu() => ActiveArea = null;
    /// <summary>Volta para a página anterior da pilha de histórico; a página que ficou para trás passa a ser a próxima do "avançar".</summary>
    public void GoBack() => Traverse(history, forward, CanGoBack);
    /// <summary>Avança para a página de que se voltou; a página atual volta para a pilha do "voltar".</summary>
    public void GoForward() => Traverse(forward, history, CanGoForward);
    private void Traverse(List<string> source, List<string> other, bool allowed)
    {
        if (!allowed) return;
        var current = Route;
        var route = source[^1];
        source.RemoveAt(source.Count - 1);
        if (Navigate(route, remember: false)) Push(other, current);
        else RaiseHistoryChanged();
    }
    private static void Push(List<string> stack, string route)
    {
        stack.Add(route);
        if (stack.Count > HistoryLimit) stack.RemoveAt(0);
    }
    private void RaiseHistoryChanged()
    {
        Notify(nameof(CanGoBack)); Notify(nameof(BackTip)); Notify(nameof(CanGoForward)); Notify(nameof(ForwardTip));
        CommandManager.InvalidateRequerySuggested();
    }
    public void Navigate(string destination) => Navigate(destination, remember: true);
    /// <summary>Abre a página. Devolve falso quando o destino é recusado (senha provisória pendente ou rota indisponível).</summary>
    private bool Navigate(string destination, bool remember)
    {
        var (target, sub) = SplitRoute(destination);
        if (target == "queue" && IsOperational) target = "slots";
        if (ShowPasswordRequired || (target != "settings" && !AllItems.Any(n => n.Id == target && n.Available))) return false;
        var area = AreaFor(target);
        // Sem subaba (atalho, página restaurada ou chamada direta), a página aparece inteira; a subaba vem do clique no menu.
        if (area is { HasChildren: true } && sub is not null && !area.Children.Any(child => child.Id == target && child.Sub == sub)) sub = null;
        if (area is not { HasChildren: true }) sub = null;
        var previous = Route;
        var changed = previous != (sub is null ? target : $"{target}:{sub}");
        if (remember && changed)
        {
            // Rota nova encerra o caminho do "avançar".
            Push(history, previous);
            forward.Clear();
        }
        if (target == "slots" && IsOperational) Workspace.SlotPanelScope = sub ?? "";
        Workspace.Section = target;
        subPage = sub;
        Page = target;
        Notify(nameof(SubPage)); Notify(nameof(Route));
        ActiveArea = area is { HasChildren: true } ? area : null;
        MarkSelectedNavigation();
        RaiseHistoryChanged();
        if (SaveNavigation) { preferences.LastPage = Page; Persist(); }
        return true;
    }

    private static NavigationItem Leaf(string id, string icon, string title, string tip, string? sub = null) => new(id, icon, title, true, tip, sub);
    private static NavigationItem Area(string id, string icon, string title, string tip, params NavigationItem[] children) => new(id, icon, title, true, tip, null, children);
    private static IReadOnlyList<NavigationItem> BuildOperationalNavigation() =>
    [
        Leaf("home", "\uE80F", "Painel da UBS", "Resumo do dia: números, consultórios, agenda e alertas da UBS."),
        Area("patients", "\uE77B", "Pacientes", "Pacientes da UBS: listagem, cadastro e vínculos de família.",
            Leaf("patients", "\uE8FD", "Listagem", "Pesquise, filtre e abra a ficha dos pacientes (duplo clique).", "list"),
            Leaf("patients", "\uE8FA", "Cadastro", "Cadastre um paciente novo ou edite a ficha selecionada.", "register"),
            Leaf("family", "\uE716", "Família", "Aprove ou recuse vínculos entre responsáveis e dependentes.")),
        Leaf("agenda", "\uE787", "Agenda", "Calendário de consultas: agende, chame, conclua ou cancele."),
        Area("slots", "\uE945", "Vaga Relâmpago", "Painel automático das vagas: quem tem consulta mais tarde recebe a vaga sozinho.",
            Leaf("slots", "\uE916", "Em andamento", "Vagas abertas e ofertas com o prazo de resposta correndo.", "live"),
            Leaf("slots", "\uE81C", "Encerradas", "Vagas aceitas, recusadas e vencidas, com filtros.", "closed")),
        Area("indicators", "\uE9D9", "Indicadores", "Desempenho e alertas da UBS.",
            Leaf("indicators", "\uE9D9", "Desempenho", "Espera, produção, comparecimento e Vaga Relâmpago no período."),
            Leaf("alerts", "\uE7BA", "Alertas", "Situações que pedem atenção, avaliadas pela API.")),
        Leaf("announcements", "\uE715", "Comunicados", "Comunicados da Secretaria para a equipe."),
        Area("unit-settings", "\uE946", "Informações da UBS", "Atendimento, equipe, consultórios, grade e bloqueios.",
            Leaf("unit-settings", "\uE8BF", "Atendimento", "Horários de funcionamento e serviços oferecidos.", "profile"),
            Leaf("unit-settings", "\uE716", "Profissionais", "Cadastre e edite os profissionais da UBS.", "professionals"),
            Leaf("unit-settings", "\uE80F", "Consultórios", "Salas de atendimento da UBS.", "rooms"),
            Leaf("unit-settings", "\uE787", "Grade semanal", "Horários de atendimento de cada profissional.", "schedule"),
            Leaf("unit-settings", "\uE8C9", "Bloqueios", "Folgas e feriados que retiram horários da grade.", "blocks"))
    ];
    private static IReadOnlyList<NavigationItem> BuildMunicipalNavigation() =>
    [
        Leaf("home", "\uE80F", "Visão geral", "Panorama atual da rede de saúde."),
        Area("units", "\uE80A", "Unidades", "UBS da rede: cadastro, desempenho, mapa e capacidade.",
            Leaf("units", "\uE8FD", "Cadastro de UBS", "Cadastre as UBS e confira a base de cada uma.", "registry"),
            Leaf("units", "\uE9D2", "Desempenho", "Compare o desempenho das UBS no período.", "performance"),
            Leaf("units", "\uE77B", "Cadastros de referência", "Consulte os pacientes cadastrados em uma UBS.", "reference"),
            Leaf("network", "\uE707", "Mapa da rede", "Território e localização das UBS."),
            Leaf("capacity", "\uE787", "Capacidade", "Horários ofertados, ocupados e ocupação por UBS e região.")),
        Area("specialties", "\uE8B7", "Especialidades", "Lista da rede e demanda por especialidade.",
            Leaf("specialties", "\uE8FD", "Catálogo", "Crie, renomeie, ative e desative especialidades da rede.", "catalog"),
            Leaf("specialties", "\uE9D2", "Demanda", "Espera e produção por especialidade.", "demand")),
        Area("indicators", "\uE9D9", "Indicadores", "Indicadores, Vaga Relâmpago, alertas, relatórios e metas.",
            Leaf("indicators", "\uE9D9", "Desempenho", "Acesso, produção e evolução no período."),
            Leaf("slots", "\uE945", "Vaga Relâmpago", "Vagas e ofertas da rede no período."),
            Leaf("alerts", "\uE7BA", "Alertas", "Situações que pedem atenção nas UBS."),
            Leaf("reports", "\uE8A5", "Relatórios e metas", "Metas da rede e exportação do resumo em CSV.")),
        Leaf("communication", "\uE8F2", "Comunicação", "Comunicados para as equipes e os pacientes."),
        Area("users", "\uE77B", "Usuários", "Contas da equipe, acessos às UBS e histórico de ações.",
            Leaf("users", "\uE8FD", "Contas", "Contas da equipe: ativar, desativar e redefinir senha.", "accounts"),
            Leaf("users", "\uE8FA", "Nova conta", "Crie uma conta de equipe.", "create"),
            Leaf("users", "\uE72E", "Acessos às UBS", "Conceda ou retire o acesso de uma conta a cada UBS.", "access"),
            Leaf("users", "\uE81C", "Histórico", "Ações realizadas na plataforma.", "history"))
    ];    public void CycleTheme() => SetAppearance((Appearance)(((int)Appearance + 1) % 3));
    public void SetAppearance(Appearance mode)
    {
        if (!Enum.IsDefined(mode)) return;
        preferences.Appearance = mode;
        themes.Apply(mode);
        Notify(nameof(Appearance)); Notify(nameof(LightSelected)); Notify(nameof(DarkSelected)); Notify(nameof(SystemSelected));
        Persist();
    }
    public void RestoreDefaults()
    {
        preferences = new();
        expanded = true;
        sidebarWidth = preferences.SidebarWidth;
        themes.Apply(preferences.Appearance);
        foreach (var name in new[] { nameof(ShowSidebarTips), nameof(ConfirmClose), nameof(SaveNavigation), nameof(Animations), nameof(RememberSidebar), nameof(FullScreen), nameof(SidebarExpanded), nameof(SidebarWidth), nameof(SidebarColumnWidth), nameof(SidebarSplitterWidth), nameof(Appearance), nameof(LightSelected), nameof(DarkSelected), nameof(SystemSelected) }) Notify(name);
        // The restored persisted navigation starts at home on the next launch; settings stays visible now.
        Persist();
        Notify(nameof(ShortcutRows));
        if (storage.Error == null) Status = "Preferências padrão restauradas neste aplicativo.";
    }
    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null) { Notify(name); Persist(); }
    private void Persist()
    {
        Status = storage.Save(preferences) ? "Preferências salvas neste computador." : storage.Error!;
        Notify(nameof(HasPreferenceError));
    }
}
