using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Galeria visual do grupo cadastros do Painel Municipal (só com <c>--visual-gallery</c>): as telas com dados, o estado vazio, a seleção com o balão, os pop-ups
/// de cadastro e as fichas, nos dois temas e na largura mínima. Os arquivos começam por <c>cad-</c> e vão para a pasta de prévias (<c>--preview-dir</c>).
/// </summary>
internal static partial class Program
{
    /// <summary>Captura uma janela (pop-up ou ficha) depois da animação de abertura, para não sair meio transparente.</summary>
    private static void Shot(Window popup, string path)
    {
        popup.UpdateLayout();
        var end = DateTime.UtcNow.AddMilliseconds(450);
        PumpUntil(() => DateTime.UtcNow > end);
        SavePreview(popup, path);
    }

    private static void RunMunicipalCadastrosGallery(string storageRoot, string previewRoot)
    {
        if (!Environment.GetCommandLineArgs().Contains("--visual-gallery")) return;
        Directory.CreateDirectory(previewRoot);
        Check("Secretaria cadastros: galeria visual das telas (dados, balão, pop-up e ficha) nos dois temas e na largura mínima", () =>
        {
            using var fixture = new ApiFixture();
            var now = DateTimeOffset.UtcNow;
            var centro = fixture.AddUnit(new HealthUnit(Guid.NewGuid(), "UBS Centro", "Centro", "Rua das Flores, 120 - Centro", -23.5, -46.6, "ready"));
            fixture.AddUnit(new HealthUnit(Guid.NewGuid(), "UBS Vila Norte", "Norte", "Av. Brasil, 800 - Vila Norte", -23.4, -46.7, "pending"));
            fixture.AddUnit(new HealthUnit(Guid.NewGuid(), "UBS Jardim Sul", "Sul", "", -23.6, -46.5, "ready"));
            fixture.AddRegistry(centro.Id, "Ana Beatriz Souza", "52998224725", "ana@example.test", "11988887777");
            fixture.AddRegistry(centro.Id, "Carlos Eduardo Lima", "11144477735", "", null, active: false);
            fixture.AddAppointment(centro.Id, "scheduled", now.AddHours(3), "52998224725", "Ana Beatriz Souza", "Clínica geral", "Dra. Helena");
            fixture.AddAppointment(centro.Id, "completed", now.AddHours(-3), "11144477735", "Carlos Eduardo Lima", "Cardiologia", "Dr. Paulo");
            fixture.AddSpecialty("Cardiologia"); fixture.AddSpecialty("Clínica geral"); fixture.AddSpecialty("Pediatria", active: false);
            fixture.StaffAccounts.Add(new(Guid.NewGuid(), "Marina Costa", "admin", "marina@example.test", true, true));
            fixture.StaffAccounts.Add(new(Guid.NewGuid(), "Rafael Torres", "attendant", "rafael@example.test", false, false));
            fixture.History.Add(new(now.AddMinutes(-5), "staff-granted", "Gestor Fixture", centro.Id, "UBS Centro", "Marina Costa", "c1"));
            fixture.History.Add(new(now.AddHours(-2), "staff-created", "Gestor Fixture", null, null, "Rafael Torres", "c2"));
            fixture.History.Add(new(now.AddDays(-1), "registry-read", "Gestor Fixture", centro.Id, "UBS Centro", null, "c3"));
            fixture.AddAnnouncement("Mutirão de vacinação na região Centro", "Todas as UBS do Centro abrem no sábado para a campanha.", "staff", "region", ["Centro"]).OperatorReadAt = now.AddMinutes(-30);
            fixture.AddAnnouncement("Novo fluxo de agendamento", "O agendamento passa a pedir o motivo da consulta.", "both", "all", expiresAt: now.AddDays(10)).PatientReads = 12;
            fixture.AddAnnouncement("Manutenção encerrada", "A manutenção terminou.", "patients").ClosedAt = now.AddDays(-1);
            Confirmation.Handler = (_, _, _) => false;
            var window = OpenMunicipal(fixture, storageRoot, "cad-gallery", Appearance.Light);
            try
            {
                var vm = LoginObs(window);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    var tag = mode == Appearance.Light ? "claro" : "escuro";
                    window.ViewModel.SetAppearance(mode);
                    string File(string name) => Path.Combine(previewRoot, $"cad-{name}-{tag}.png");
                    void Page(string route, string name, double width = 1280, double height = 780) => SavePreview(Show(window, route, width, height), File(name));
                    ListBox Select(string route, string listName, Func<object> pick, string name)
                    {
                        var content = Show(window, route, 1280, 780); var list = ListNamed(content, listName);
                        SelectRows(window, list, pick()); SavePreview(Layout(window, 1280, 780), File(name)); return list;
                    }
                    void Ficha(object item, string name)
                    {
                        var details = Await(vm.BuildMunicipalDetailsAsync(item)) ?? throw new InvalidOperationException("A ficha não abriu: " + name);
                        var popup = new ItemDetailsWindow(details) { Owner = window }; popup.Show(); Shot(popup, File(name)); popup.Close();
                    }
                    void Popup(ListBox list, string button, string name) => InspectModal<FormDialog>(window, dialog => { Shot(dialog, File(name)); }, () => InvokeButton(BalloonButton(list, button)));
                    void HeaderPopup(string button, string name) => InspectHeaderForm(window, button, dialog => { Shot(dialog, File(name)); });

                    // Cadastro de UBS: lista, seleção (base pendente), pop-ups e ficha.
                    Page("units:registry", "ubs");
                    var norte = vm.Units.First(unit => unit.Name == "UBS Vila Norte");
                    var units = Select("units:registry", "UBS cadastradas", () => norte, "ubs-selecao");
                    Popup(units, "Tentar criar a base de novo", "ubs-base-pendente-popup");
                    Popup(units, "Editar", "ubs-editar-popup");
                    HeaderPopup("Nova UBS", "ubs-nova-popup");
                    Ficha(norte, "ubs-ficha");
                    // Cadastros de referência e agenda da UBS.
                    vm.SelectedUnit = vm.Units.First(unit => unit.Name == "UBS Centro");
                    Pump(vm.LoadUnitRegistryAsync()); Pump(vm.LoadMunicipalAppointmentsAsync());
                    Page("units:reference", "referencia");
                    Select("units:reference", "Pacientes da UBS selecionada", () => vm.UnitRegistry.First(), "referencia-selecao");
                    Ficha(vm.UnitRegistry.First(), "referencia-ficha");
                    var reference = Show(window, "units:reference", 1280, 780);
                    var toggle = Descendants<SegmentedToggle>(reference).Single(item => AutomationProperties.GetName(item) == "Mostrar cadastros ou agenda da UBS");
                    toggle.SelectedIndex = 1; Layout(window, 1280, 780);
                    SelectRows(window, ListNamed(reference, "Consultas da UBS selecionada"), vm.VisibleMunicipalAppointments.First());
                    SavePreview(Layout(window, 1280, 780), File("agenda-selecao"));
                    toggle.SelectedIndex = 0;
                    // Mapa da rede.
                    Page("network", "mapa");
                    // Catálogo de especialidades.
                    Page("specialties:catalog", "especialidades");
                    var specialties = Select("specialties:catalog", "Lista de especialidades", () => vm.SpecialtyCatalog.First(item => item.Name == "Cardiologia"), "especialidades-selecao");
                    Popup(specialties, "Renomear", "especialidades-renomear-popup");
                    HeaderPopup("Nova especialidade", "especialidades-nova-popup");
                    // Comunicação.
                    Page("communication", "comunicacao");
                    Select("communication", "Comunicados da Secretaria", () => vm.MyAnnouncements.First(), "comunicacao-selecao");
                    Ficha(vm.MyAnnouncements.First(), "comunicacao-ficha");
                    HeaderPopup("Novo comunicado", "comunicacao-novo-popup");
                    // Usuários.
                    Page("users:accounts", "contas");
                    var marina = vm.Staff.First(item => item.FullName == "Marina Costa");
                    var accounts = Select("users:accounts", "Contas OBS", () => marina, "contas-selecao");
                    Popup(accounts, "Redefinir senha provisória", "contas-redefinir-popup");
                    HeaderPopup("Nova conta", "contas-nova-popup");
                    Ficha(marina, "contas-ficha");
                    Page("users:access", "acessos");
                    var access = Select("users:access", "Contas e UBS com acesso", () => marina, "acessos-selecao");
                    Popup(access, "Conceder acesso", "acessos-conceder-popup");
                    Page("users:history", "historico");
                    Select("users:history", "Histórico de ações", () => vm.HistoryEvents.First(), "historico-selecao");
                    Ficha(vm.HistoryEvents.First(), "historico-ficha");
                    // Largura mínima da janela (980).
                    Page("units:registry", "ubs-980", 980, 640);
                    Page("users:accounts", "contas-980", 980, 640);
                    Page("communication", "comunicacao-980", 980, 640);
                }
            }
            finally { Confirmation.Handler = null; CloseWindow(window); }
        });
        Check("Secretaria cadastros: galeria visual do estado vazio nos dois temas", () =>
        {
            using var fixture = new ApiFixture();
            fixture.StaffAccounts.Clear();
            var window = OpenMunicipal(fixture, storageRoot, "cad-gallery-empty", Appearance.Light);
            try
            {
                LoginObs(window);
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
                {
                    var tag = mode == Appearance.Light ? "claro" : "escuro";
                    window.ViewModel.SetAppearance(mode);
                    foreach (var (route, name) in new[] { ("units:registry", "ubs"), ("units:reference", "referencia"), ("specialties:catalog", "especialidades"), ("communication", "comunicacao"), ("users:accounts", "contas"), ("users:access", "acessos"), ("users:history", "historico") })
                        SavePreview(Show(window, route, 1280, 560), Path.Combine(previewRoot, $"cad-{name}-vazio-{tag}.png"));
                }
            }
            finally { CloseWindow(window); }
        });
    }
}
