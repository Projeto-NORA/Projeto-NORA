using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Nora.Api;

/// <summary>
/// X-Forwarded-For/X-Forwarded-Proto ficam DESLIGADOS por padrão: sem proxy confiável configurado, qualquer cliente poderia forjar o
/// cabeçalho e escolher o "IP" usado pelos limites de taxa. A configuração só liga o middleware quando há proxy declarado em
/// <c>Nora:ForwardedHeaders:KnownProxies</c> (IPs) e/ou <c>Nora:ForwardedHeaders:KnownNetworks</c> (CIDR); cada lista aceita índices
/// (<c>KnownProxies:0</c>, <c>Nora__ForwardedHeaders__KnownProxies__0</c>) ou valores separados por vírgula. O cabeçalho só é honrado
/// quando a conexão vem de um desses endereços e apenas a entrada mais próxima é lida (ForwardLimit 1). Entrada inválida impede a subida.
/// </summary>
internal static class ForwardedHeadersSetup
{
    /// <returns>Verdadeiro quando há proxy confiável configurado (o chamador deve usar <c>UseForwardedHeaders</c> antes dos demais middlewares).</returns>
    public static bool Configure(WebApplicationBuilder builder)
    {
        var proxies = ReadList(builder.Configuration, "Nora:ForwardedHeaders:KnownProxies")
            .Select(value => IPAddress.TryParse(value, out var address) ? address : throw new InvalidOperationException("Nora:ForwardedHeaders:KnownProxies contém um endereço IP inválido.")).ToList();
        var networks = ReadList(builder.Configuration, "Nora:ForwardedHeaders:KnownNetworks")
            .Select(value => System.Net.IPNetwork.TryParse(value, out var network) ? network : throw new InvalidOperationException("Nora:ForwardedHeaders:KnownNetworks contém uma rede CIDR inválida.")).ToList();
        if (proxies.Count == 0 && networks.Count == 0) return false;
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
            foreach (var network in networks) options.KnownIPNetworks.Add(network);
        });
        return true;
    }

    internal static List<string> ReadList(IConfiguration configuration, string key)
    {
        var values = new List<string>();
        if (configuration[key] is { Length: > 0 } single) values.AddRange(single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        values.AddRange(configuration.GetSection(key).GetChildren().Select(child => child.Value?.Trim()).Where(value => !string.IsNullOrEmpty(value))!);
        return values;
    }
}
