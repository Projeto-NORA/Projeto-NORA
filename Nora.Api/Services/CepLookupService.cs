using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>Fetches only a CEP from ViaCEP; patient identity and address drafts never leave NORA.</summary>
public sealed class CepLookupService(HttpClient http, IMemoryCache cache)
{
    public async Task<AddressInfo?> LookupAsync(string value, CancellationToken ct)
    {
        var cep = AccountService.NormalizeCep(value);
        if (cep.Length != 8) throw new ApiInputException();
        if (cache.TryGetValue<AddressInfo?>("cep:" + cep, out var cached)) return cached;
        try
        {
            var result = await http.GetFromJsonAsync<ViaCepResponse>($"ws/{cep}/json/", ct);
            if (result is null || result.Error) return null;
            if (string.IsNullOrWhiteSpace(result.Logradouro) || string.IsNullOrWhiteSpace(result.Localidade) || string.IsNullOrWhiteSpace(result.Uf)) return null;
            var address = new AddressInfo(cep, result.Logradouro.Trim(), null, EmptyToNull(result.Complemento), EmptyToNull(result.Bairro), result.Localidade.Trim(), result.Uf.Trim().ToUpperInvariant(), EmptyToNull(result.Ibge));
            cache.Set("cep:" + cep, address, TimeSpan.FromHours(24));
            return address;
        }
        catch (HttpRequestException exception) { throw new CepUnavailableException(exception); }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested) { throw new CepUnavailableException(exception); }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ViaCepResponse(
        [property: JsonPropertyName("logradouro")] string? Logradouro,
        [property: JsonPropertyName("complemento")] string? Complemento,
        [property: JsonPropertyName("bairro")] string? Bairro,
        [property: JsonPropertyName("localidade")] string? Localidade,
        [property: JsonPropertyName("uf")] string? Uf,
        [property: JsonPropertyName("ibge")] string? Ibge,
        [property: JsonPropertyName("erro")] bool Error = false);
}

public sealed class CepUnavailableException(Exception inner) : Exception("Consulta de CEP indisponível.", inner);
