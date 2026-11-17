using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

/// <summary>
/// Computadores do OBS nas suítes que entram como atendente: o administrador ativa um computador da UBS (sem cabeçalho, "ativar este computador") e a atendente entra mandando o cabeçalho
/// <c>X-Nora-Device</c> em todas as requisições, como o OBS faz. Nada é gravado fora do banco descartável do teste.
/// </summary>
internal static class ObsDeviceHelper
{
    public const string HeaderName = "X-Nora-Device";

    public static string HeaderValue(Guid deviceId, string secret) => deviceId.ToString("N") + "." + secret;

    /// <summary>Ativa um computador da UBS pelo administrador e devolve o id e o valor do cabeçalho.</summary>
    public static async Task<(Guid DeviceId, string Header)> ActivateAsync(HttpClient admin, Guid unitId, string name = "Computador de teste")
    {
        using var response = await admin.PostAsJsonAsync($"/api/obs/units/{unitId}/devices", new { name, activateThisComputer = true });
        if ((int)response.StatusCode != 201) throw new InvalidOperationException($"Ativação do computador de teste falhou: HTTP {(int)response.StatusCode}.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var credential = json.RootElement.GetProperty("credential");
        var id = credential.GetProperty("deviceId").GetGuid();
        return (id, HeaderValue(id, credential.GetProperty("secret").GetString()!));
    }

    /// <summary>Passa a mandar (ou deixa de mandar, com nulo) o cabeçalho do computador em todas as requisições do cliente.</summary>
    public static void Send(HttpClient client, string? header)
    {
        client.DefaultRequestHeaders.Remove(HeaderName);
        if (header is not null) client.DefaultRequestHeaders.Add(HeaderName, header);
    }

    /// <summary>Ativa um computador da UBS, entra como o usuário do OBS mandando o cabeçalho e fica com ele nas próximas requisições. Devolve o cabeçalho.</summary>
    public static async Task<string> ActivateAndLoginAsync(HttpClient admin, HttpClient client, Guid unitId, string identifier, string password, string name = "Computador de teste")
    {
        var (_, header) = await ActivateAsync(admin, unitId, name);
        Send(client, header);
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { scope = "obs", identifier, password });
        if ((int)response.StatusCode != 200) throw new InvalidOperationException($"Login da atendente com computador falhou: HTTP {(int)response.StatusCode}.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("token").GetString()!);
        return header;
    }
}
