using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private sealed class GeocodeTransport(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class FakeGeocoder : IHealthUnitGeocoder
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public TaskCompletionSource<(double Latitude, double Longitude)>? Pending { get; set; }
        public Task<(double Latitude, double Longitude)> FindAsync(string address, string region)
        {
            Calls++;
            if (Fail) throw new NoraApiException("Endereço não encontrado.");
            if (Pending is not null) return Pending.Task;
            return Task.FromResult((-23.2, -45.9));
        }
    }
    private static void CheckHealthUnitGeocoding()
    {
        Check("UBS: geocodificação aceita só endereço e região correspondentes", () =>
        {
            const string feature = """{"properties":{"countrycode":"BR","street":"Rua Florença","housenumber":"100","city":"São José dos Campos"},"geometry":{"coordinates":[-45.9,-23.2]}}""";
            using var transport = new GeocodeTransport("{\"features\":[" + feature + "]}");
            using var http = new HttpClient(transport);
            var geo = new HealthUnitGeocoder(http);
            var result = Await(geo.FindAsync("R. Florença, 100", "São José dos Campos"));
            Require(result == (-23.2, -45.9) && transport.RequestedUri?.Host == "photon.komoot.io", "Localização ou serviço incorreto.");
            Require(Await(geo.FindAsync("Rua Florença 100 - Jardim Veneza", "São José dos Campos")) == result, "Não reconheceu número sem vírgula.");
            try { Await(geo.FindAsync("R. Florença, 101", "São José dos Campos")); throw new InvalidOperationException("Aceitou um número diferente do informado."); }
            catch (NoraApiException) { }
            try { Await(geo.FindAsync("R. Florença, 100", "Outra Cidade")); }
            catch (NoraApiException) { return; }
            throw new InvalidOperationException("Aceitou uma cidade diferente da informada.");
        });
        Check("UBS: geocodificação não escolhe localização ambígua", () =>
        {
            const string one = """{"properties":{"countrycode":"BR","street":"Rua Exemplo","housenumber":"1","city":"Cidade"},"geometry":{"coordinates":[-45.9,-23.2]}}""";
            const string two = """{"properties":{"countrycode":"BR","street":"Rua Exemplo","housenumber":"1","city":"Cidade"},"geometry":{"coordinates":[-46.1,-23.1]}}""";
            using var http = new HttpClient(new GeocodeTransport("{\"features\":[" + one + "," + two + "]}"));
            try { Await(new HealthUnitGeocoder(http).FindAsync("Rua Exemplo, 1", "Cidade")); }
            catch (NoraApiException) { return; }
            throw new InvalidOperationException("Aceitou mais de uma localização possível.");
        });
        Check("UBS: serviço de localização indisponível retorna erro compreensível", () =>
        {
            using var http = new HttpClient(new GeocodeTransport("", HttpStatusCode.ServiceUnavailable));
            try { Await(new HealthUnitGeocoder(http).FindAsync("Rua Exemplo, 1", "Cidade")); }
            catch (NoraApiException error) { Require(error.Message.Contains("localização"), "Falha não foi explicada."); return; }
            throw new InvalidOperationException("Serviço indisponível não retornou erro.");
        });
        Check("UBS: cadastro busca localização e edição preserva ou atualiza coordenadas", () =>
        {
            using var fixture = new ApiFixture();
            var geo = new FakeGeocoder();
            using var vm = new WorkspaceViewModel(DesktopProfile.Municipal, new NoraApiClient(fixture), unitGeocoder: geo);
            vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
            Pump(vm.LoginAsync("isolated-test-password"));
            vm.UnitName = "UBS Teste"; vm.UnitRegion = "Cidade"; vm.UnitAddress = "Rua Exemplo, 1";
            Pump(vm.SaveUnitAsync());
            Require(geo.Calls == 1 && fixture.Count("POST", "/api/municipal/units") == 1 && vm.SelectedUnit?.Latitude == -23.2, "Cadastro não usou a localização buscada.");
            Require(vm.UnitSaveSteps.Count == 2 && vm.UnitSaveSteps.All(step => step.State == StepState.Done), "Etapas do cadastro não ficaram concluídas.");
            vm.EditSelectedUnit(); vm.UnitName = "UBS Alterada"; Pump(vm.SaveUnitAsync());
            Require(geo.Calls == 1 && vm.SelectedUnit?.Latitude == -23.2, "Edição apagou ou buscou novamente a localização sem alterar endereço.");
            vm.EditSelectedUnit(); vm.UnitAddress = "Outra Rua, 2"; Pump(vm.SaveUnitAsync());
            Require(geo.Calls == 2 && vm.SelectedUnit?.Address == "Outra Rua, 2", "Mudança de endereço não refez a busca.");
            var savedId = vm.SelectedUnit!.Id;
            vm.EditSelectedUnit(); vm.UnitAddress = "Rua Inexistente, 9"; geo.Fail = true; Pump(vm.SaveUnitAsync());
            Require(vm.MessageKind == MessageKind.Error && vm.SelectedUnit?.Address == "Outra Rua, 2" && fixture.Count("PUT", $"/api/municipal/units/{savedId}") == 2,
                "Falha de localização na edição alterou a UBS salva.");
            vm.NewUnitDraft(); vm.UnitName = "UBS Falha"; vm.UnitRegion = "Cidade"; vm.UnitAddress = "Rua Inexistente, 9"; geo.Fail = true;
            Pump(vm.SaveUnitAsync());
            Require(vm.MessageKind == MessageKind.Error && fixture.Count("POST", "/api/municipal/units") == 1 && vm.UnitSaveSteps.Single().State == StepState.Failed,
                "Falha de localização não foi exibida ou gravou uma UBS sem posição válida.");
        });
        Check("UBS: fechar a janela durante a localização não salva a unidade", () =>
        {
            using var fixture = new ApiFixture();
            var geo = new FakeGeocoder { Pending = new TaskCompletionSource<(double, double)>() };
            var vm = new WorkspaceViewModel(DesktopProfile.Municipal, new NoraApiClient(fixture), unitGeocoder: geo);
            vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
            Pump(vm.LoginAsync("isolated-test-password"));
            vm.UnitName = "UBS Teste"; vm.UnitRegion = "Cidade"; vm.UnitAddress = "Rua Exemplo, 1";
            var save = vm.SaveUnitAsync();
            PumpUntil(() => geo.Calls == 1);
            vm.Dispose();
            geo.Pending.SetResult((-23.2, -45.9));
            Pump(save);
            Require(fixture.Count("POST", "/api/municipal/units") == 0, "A UBS foi salva depois de fechar a janela.");
        });
        Check("UBS: Enter no endereço busca localização e salva pelo formulário real", () =>
        {
            using var fixture = new ApiFixture();
            var geo = new FakeGeocoder();
            using var vm = new WorkspaceViewModel(DesktopProfile.Municipal, new NoraApiClient(fixture), unitGeocoder: geo);
            vm.Endpoint = "https://fixture.invalid"; vm.Identifier = "fixture@example.invalid";
            Pump(vm.LoginAsync("isolated-test-password"));
            vm.UnitName = "UBS Teste"; vm.UnitRegion = "Cidade"; vm.UnitAddress = "Rua Exemplo, 1";
            var view = new HealthUnitsView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 700, ShowInTaskbar = false };
            try
            {
                window.Show(); window.Activate(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var address = Descendants<TextBox>(view).Single(box => AutomationProperties.GetName(box) == "Endereço da UBS");
                Require(address.Focus(), "Não foi possível focar o endereço.");
                PressEnter(address);
                PumpUntil(() => fixture.Count("POST", "/api/municipal/units") == 1 && !vm.IsBusy);
                Require(geo.Calls == 1 && vm.SelectedUnit?.Latitude == -23.2, "Enter não salvou com a localização buscada.");
            }
            finally { window.Close(); }
        });
    }
}
