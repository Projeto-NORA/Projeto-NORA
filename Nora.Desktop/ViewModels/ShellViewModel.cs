using System.Windows;
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
    public bool IsHome => Page != "settings" && !ShowPasswordRequired;
    public bool IsSettings => Page == "settings" && !ShowPasswordRequired;
    /// <summary>Senha provisória: a janela mostra só a definição da nova senha (e Sair), sem menu nem dados.</summary>
    public bool ShowPasswordRequired => Workspace.MustChangePassword;
    public string PageLabel => ShowPasswordRequired ? "Definir nova senha" : Page == "settings" ? "Configurações" : Navigation.FirstOrDefault(n => n.Id == Page)?.Title ?? "Visão geral";
    public string SectionSummary => ShowPasswordRequired ? "Troca obrigatória da senha provisória" : Page switch
    {
        "home" => IsMunicipal ? "Panorama e prioridades da rede" : "Visão operacional e prioridades do dia",
        "units" => "Acompanhe as unidades da rede",
        "specialties" => "Lista da rede, demanda, fila e espera por especialidade",
        "network" => "Distribuição e carga por região",
        "capacity" => "Capacidade e uso das agendas",
        "reports" => "Relatórios, metas e exportação",
        "communication" => "Comunicados para as equipes e os pacientes das unidades",
        "users" => "Acessos e histórico de ações",
        "queue" => "Organize a ordem de atendimento e acompanhe esperas",
        "agenda" => "Consulte períodos, horários e situações da agenda",
        "patients" => "Pesquise, cadastre e consulte pacientes autorizados",
        "slots" => "Reaproveite oportunidades e acompanhe Vagas Relâmpago",
        "indicators" => "Acompanhe o desempenho da unidade",
        "alerts" => "Priorize situações que exigem atenção",
        "unit-settings" => "Profissionais, consultórios, grade de horários e bloqueios",
        "family" => "Pedidos de vínculo entre responsáveis e dependentes",
        "announcements" => "Comunicados da Secretaria para a equipe",
        "settings" => "Aparência, atalhos e preferências deste computador",
        _ => ""
    };
    public bool SidebarExpanded { get => expanded; set { expanded = value; Notify(); if (RememberSidebar) { preferences.SidebarExpanded = value; Persist(); } } }
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
    public GridLength SidebarColumnWidth
    {
        get => new(SidebarWidth, GridUnitType.Pixel);
        set
        {
            if (value.IsAbsolute) SidebarWidth = value.Value;
        }
    }
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
    public ICommand AppearanceCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand RestoreCommand { get; }
    public ShellViewModel(DesktopProfile profile, PreferencesService storage, ThemeService themes, NoraApiClient? api = null, IObsDeviceStore? devices = null)
    {
        Profile = profile;
        Workspace = new(profile, api, devices);
        Workspace.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(WorkspaceViewModel.UnreadAnnouncementCount)) { SetBadge("announcements", Workspace.UnreadAnnouncementCount); return; }
            if (change.PropertyName != nameof(WorkspaceViewModel.MustChangePassword)) return;
            foreach (var name in new[] { nameof(ShowPasswordRequired), nameof(IsHome), nameof(IsSettings), nameof(PageLabel), nameof(SectionSummary) }) Notify(name);
        };
        this.storage = storage;
        this.themes = themes;
        preferences = storage.Load();
        status = storage.Error ?? "Entre na conta para carregar os dados da API.";
        page = preferences.SaveNavigation ? preferences.LastPage : "home";
        Workspace.Section = page;
        expanded = !preferences.RememberSidebar || preferences.SidebarExpanded;
        sidebarWidth = !preferences.RememberSidebar ? 264 : Math.Clamp(preferences.SidebarWidth, 208, 380);
        Navigation = profile.IsMunicipal
            ? [new("home", "\uE80F", "Visão geral", true), new("units", "\uE80A", "Unidades", true), new("specialties", "\uE8B7", "Especialidades", true), new("network", "\uE707", "Mapa da rede", true), new("indicators", "\uE9D9", "Indicadores", true), new("slots", "\uE945", "Vaga Relâmpago", true), new("capacity", "\uE787", "Capacidade", true), new("alerts", "\uE7BA", "Alertas", true), new("reports", "\uE8A5", "Relatórios e metas", true), new("communication", "\uE8F2", "Comunicação", true), new("users", "\uE77B", "Usuários e histórico", true)]
            : [new("home", "\uE80F", "Painel da UBS", true), new("queue", "\uE8EF", "Fila", true), new("agenda", "\uE787", "Agenda", true), new("patients", "\uE77B", "Pacientes", true), new("family", "\uE716", "Família", true), new("slots", "\uE945", "Vaga Relâmpago", true), new("indicators", "\uE9D9", "Indicadores", true), new("alerts", "\uE7BA", "Alertas", true), new("announcements", "\uE715", "Comunicados", true), new("unit-settings", "\uE115", "Configurações da UBS", true)];
        MarkSelectedNavigation();
        NavigateCommand = new RelayCommand(p => Navigate(p?.ToString() ?? "home"));
        AppearanceCommand = new RelayCommand(p => { if (Enum.TryParse<Appearance>(p?.ToString(), out var mode)) SetAppearance(mode); });
        ToggleSidebarCommand = new RelayCommand(_ => SidebarWidth = SidebarWidth < 240 ? 264 : 208);
        RestoreCommand = new RelayCommand(_ => RestoreDefaults());
        themes.Apply(preferences.Appearance);
    }
    private void MarkSelectedNavigation() { if (Navigation is null) return; foreach (var item in Navigation) item.IsSelected = item.Id == page; }
    private void SetBadge(string id, int count) { if (Navigation.FirstOrDefault(item => item.Id == id) is { } item) item.Badge = count; }
    public void Navigate(string destination)
    {
        if (ShowPasswordRequired || (destination != "settings" && !Navigation.Any(n => n.Id == destination && n.Available))) return;
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
        sidebarWidth = preferences.SidebarWidth;
        themes.Apply(preferences.Appearance);
        foreach (var name in new[] { nameof(ShowSidebarTips), nameof(ConfirmClose), nameof(SaveNavigation), nameof(Animations), nameof(RememberSidebar), nameof(FullScreen), nameof(SidebarExpanded), nameof(SidebarWidth), nameof(SidebarColumnWidth), nameof(Appearance), nameof(LightSelected), nameof(DarkSelected), nameof(SystemSelected) }) Notify(name);
        // The restored persisted navigation starts at home on the next launch; settings stays visible now.
        Persist();
        if (storage.Error == null) Status = "Preferências padrão restauradas neste aplicativo.";
    }
    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null) { Notify(name); Persist(); }
    private void Persist()
    {
        Status = storage.Save(preferences) ? "Preferências salvas neste computador." : storage.Error!;
        Notify(nameof(HasPreferenceError));
    }
}
