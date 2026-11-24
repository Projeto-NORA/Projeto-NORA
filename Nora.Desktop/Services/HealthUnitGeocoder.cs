using System.Net.Http;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nora.Desktop.Services;

public interface IHealthUnitGeocoder
{
    Task<(double Latitude, double Longitude)> FindAsync(string address, string region);
}

/// <summary>Busca pontual iniciada pelo usuário; endereços são enviados ao Photon/OpenStreetMap, sem dados de pacientes.</summary>
public sealed class HealthUnitGeocoder : IHealthUnitGeocoder
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient http;
    public HealthUnitGeocoder(HttpClient? http = null) => this.http = http ?? SharedHttp;

    public async Task<(double Latitude, double Longitude)> FindAsync(string address, string region)
    {
        var query = Uri.EscapeDataString($"{address}, {region}, Brasil");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://photon.komoot.io/api/?q={query}&limit=5&bbox=-74.5,-34,-28,5.6");
        request.Headers.UserAgent.ParseAdd("NORA/1.0 (cadastro de unidades de saude)");
        try
        {
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) throw new NoraApiException("Não foi possível buscar a localização. Confira sua conexão e tente novamente.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            if (!body.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
                throw new NoraApiException("O serviço de localização retornou uma resposta inválida. Tente novamente.");
            var numberMatch = Regex.Match(address, @"(?:,\s*|\s+)(\d+[A-Za-z]?)(?=\s*(?:-|,|$))");
            var number = numberMatch.Groups[1].Value;
            var streetText = numberMatch.Success ? address[..numberMatch.Index] : address.Split(',')[0];
            var streetWords = Normalize(streetText).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word is not ("rua" or "r" or "avenida" or "av" or "travessa" or "tv" or "alameda" or "al" or "de" or "da" or "do" or "das" or "dos"))
                .ToArray();
            var expectedRegion = Normalize(region);
            var matches = new List<(double Latitude, double Longitude)>();
            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object ||
                    !properties.TryGetProperty("countrycode", out var country) || !string.Equals(country.GetString(), "BR", StringComparison.OrdinalIgnoreCase) ||
                    !feature.TryGetProperty("geometry", out var geometry) || !geometry.TryGetProperty("coordinates", out var coords) ||
                    coords.ValueKind != JsonValueKind.Array || coords.GetArrayLength() < 2 ||
                    !coords[0].TryGetDouble(out var longitude) || !coords[1].TryGetDouble(out var latitude) ||
                    !double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -34 or > 5.6 || longitude is < -74.5 or > -28) continue;
                var street = Normalize(Property(properties, "street") + " " + Property(properties, "name"));
                var candidateWords = street.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                var place = Normalize(string.Join(' ', new[] { "city", "county", "district", "locality", "suburb", "neighbourhood", "state" }.Select(key => Property(properties, key))));
                if (streetWords.Length == 0 || streetWords.Any(word => !candidateWords.Contains(word)) ||
                    expectedRegion.Length == 0 || !place.Contains(expectedRegion, StringComparison.Ordinal)) continue;
                if (number.Length > 0 && (!properties.TryGetProperty("housenumber", out var house) ||
                    !string.Equals(house.GetString(), number, StringComparison.OrdinalIgnoreCase))) continue;
                if (!matches.Any(item => Math.Abs(item.Latitude - latitude) < 0.00001 && Math.Abs(item.Longitude - longitude) < 0.00001))
                    matches.Add((latitude, longitude));
            }
            if (matches.Count != 1)
                throw new NoraApiException("Não foi possível identificar um único endereço. Confira o endereço e a região e tente novamente.");
            return matches[0];
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            throw new NoraApiException("Não foi possível buscar a localização. Confira sua conexão e tente novamente.");
        }
    }

    private static string Property(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return string.Join(' ', result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
