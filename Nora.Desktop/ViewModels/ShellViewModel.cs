using System.Collections.ObjectModel;
using System.Windows.Input;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private DesktopPreferences preferences;
    private readonly PreferencesService storage;
    private readonly ThemeService themes;
    private string page;
    private string status;
    private bool expanded;
    private OperationalRow? selectedRow;
    public DesktopProfile Profile { get; }
    public WorkspaceViewModel Workspace { get; }
    public IReadOnlyList<NavigationItem> Navigation { get; }
    public IReadOnlyList<MetricCard> Metrics { get; }
    public ObservableCollection<OperationalRow> Rows { get; } = [];
    public bool IsMunicipal => Profile.IsMunicipal;
    public bool IsOperational => !IsMunicipal;
    public string WorkspaceName => IsMunicipal ? "GESTÃO MUNICIPAL" : "OPERAÇÃO DA UNIDADE";
    public string HomeTitle => IsMunicipal ? "Uma visão da rede, em um só lugar." : "Mais clareza para cuidar de cada jornada.";
    public string HomeSubtitle => IsMunicipal ? "Indicadores e acompanhamento da rede municipal de saúde." : "Organize consultas, acompanhe a fila e encontre novas oportunidades de atendimento.";
    public string SectionTitle => IsMunicipal ? "Panorama da rede" : "Agenda de atendimentos";
    public string EmptyTitle => IsMunicipal ? "A rede ainda não está conectada" : "Sua agenda começa aqui";
    public string EmptyDescription => IsMunicipal ? "Os indicadores serão exibidos após a integração com os dados das unidades. Nenhuma métrica está disponível nesta etapa." : "Quando a unidade estiver conectada, os atendimentos aparecerão nesta área. Nenhuma consulta foi carregada nesta etapa.";
    public string Status { get => status; private set { status = value; Notify(); } }
    public string Page { get => page; private set { page = value; Notify(); Notify(nameof(IsHome)); Notify(nameof(IsSettings)); Notify(nameof(PageLabel)); } }
    public bool IsHome => Page != "settings";
    public bool IsSettings => Page == "settings";
    public string PageLabel => Page == "settings" ? "Configurações" : Navigation.FirstOrDefault(n => n.Id == Page)?.Title ?? "Visão geral";
    public bool SidebarExpanded { get => expanded; set { expanded = value; Notify(); Notify(nameof(SidebarWidth)); if (RememberSidebar) { preferences.SidebarExpanded = value; Persist(); } } }
    public double SidebarWidth => SidebarExpanded ? 248 : 80;
    public OperationalRow? SelectedRow { get => selectedRow; set { selectedRow = value; Notify(); Notify(nameof(HasSelection)); } }
    public bool HasSelection => SelectedRow != null;
    public bool HasNoRows => Rows.Count == 0;
    public Appearance Appearance => preferences.Appearance;
    public bool LightSelected => Appearance == Appearance.Light;
    public bool DarkSelected => Appearance == Appearance.Dark;
    public bool SystemSelected => Appearance == Appearance.System;
    public bool ShowSidebarTips { get => preferences.ShowSidebarTips; set { preferences.ShowSidebarTips = value; Changed(); } }
    public bool ConfirmClose { get => preferences.ConfirmClose; set { preferences.ConfirmClose = value; Changed(); } }
    public bool SaveNavigation { get => preferences.SaveNavigation; set { preferences.SaveNavigation = value; if (value) preferences.LastPage = Page; Changed(); } }
    public bool Animations { get => preferences.Animations; set { preferences.Animations = value; Changed(); } }
    public bool RememberSidebar { get => preferences.RememberSidebar; set { preferences.RememberSidebar = value; if (value) preferences.SidebarExpanded = SidebarExpanded; Changed(); } }
    public bool FullScreen { get => preferences.FullScreen; set { preferences.FullScreen = value; Changed(); } }
    public ICommand NavigateCommand { get; }
    public ICommand AppearanceCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand RestoreCommand { get; }
    public ShellViewModel(DesktopProfile profile, PreferencesService storage, ThemeService themes, NoraApiClient? api = null)
    {
        Profile = profile;
        Workspace = new(profile, api);
        this.storage = storage;
        this.themes = themes;
        preferences = storage.Load();
        status = storage.Error ?? "Entre na conta para carregar os dados da API.";
        page = preferences.SaveNavigation ? preferences.LastPage : "home";
        Workspace.Section = page;
        expanded = !preferences.RememberSidebar || preferences.SidebarExpanded;
        Navigation = profile.IsMunicipal
            ? [new("home", "\uE80F", "Visão geral", true), new("units", "\uE80A", "Unidades de saúde"), new("indicators", "\uE9D9", "Indicadores", true), new("reports", "\uE8A5", "Relatórios")]
            : [new("home", "\uE80F", "Visão geral", true), new("patients", "\uE77B", "Pacientes", true), new("appointments", "\uE787", "Consultas"), new("queue", "\uE8EF", "Fila de espera", true), new("slots", "\uE945", "Vaga Relâmpago", true)];
        Metrics = profile.IsMunicipal
            ? [new("Unidades", "\uE80A", "Cobertura da rede"), new("Atendimentos", "\uE787", "Produção consolidada"), new("Espera", "\uE823", "Acesso aos serviços"), new("Ocupação", "\uE9D9", "Capacidade assistencial")]
            : [new("Consultas", "\uE787", "Agenda da unidade"), new("Fila de espera", "\uE823", "Jornadas em acompanhamento"), new("Confirmações", "\uE73E", "Atendimentos confirmados"), new("Vagas", "\uE945", "Oportunidades de atendimento")];
        NavigateCommand = new RelayCommand(p => Navigate(p?.ToString() ?? "home"));
        AppearanceCommand = new RelayCommand(p => { if (Enum.TryParse<Appearance>(p?.ToString(), out var mode)) SetAppearance(mode); });
        ToggleSidebarCommand = new RelayCommand(_ => SidebarExpanded = !SidebarExpanded);
        RestoreCommand = new RelayCommand(_ => RestoreDefaults());
        themes.Apply(preferences.Appearance);
        Rows.CollectionChanged += (_, _) => Notify(nameof(HasNoRows));
    }
    public void Navigate(string destination)
    {
        if (destination != "settings" && !Navigation.Any(n => n.Id == destination && n.Available)) return;
        Workspace.Section = destination;
        Page = destination;
        if (SaveNavigation) { preferences.LastPage = Page; Persist(); }
    }
    public void CycleTheme() => SetAppearance((Appearance)(((int)Appearance + 1) % 3));
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
        themes.Apply(preferences.Appearance);
        foreach (var name in new[] { nameof(ShowSidebarTips), nameof(ConfirmClose), nameof(SaveNavigation), nameof(Animations), nameof(RememberSidebar), nameof(FullScreen), nameof(SidebarExpanded), nameof(SidebarWidth), nameof(Appearance), nameof(LightSelected), nameof(DarkSelected), nameof(SystemSelected) }) Notify(name);
        // The restored persisted navigation starts at home on the next launch; settings stays visible now.
        Persist();
        if (storage.Error == null) Status = "Preferências padrão restauradas neste aplicativo.";
    }
    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null) { Notify(name); Persist(); }
    private void Persist() { Status = storage.Save(preferences) ? "Preferências salvas neste computador." : storage.Error!; }
}
