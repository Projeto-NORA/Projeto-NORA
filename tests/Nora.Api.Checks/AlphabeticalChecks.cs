using System.Net.Http.Json;
using System.Text.Json;
using static RoundFixture;

/// <summary>Ordem alfabética (2026-11-24): as listas nominais vêm de A a Z pelo nome, sem diferenciar maiúsculas. Roda com <c>--alphabetical-only</c>.</summary>
internal static class AlphabeticalChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP ordem alfabética: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("ordem alfabética");
        await using var f = await StartAsync(connection, "alpha");
        var u1 = f.Unit1;
        var specialty = await f.SpecialtyAsync("Clínica geral");
        foreach (var name in new[] { "zeca Lima", "Ana Souza", "bruno Dias", "Carla Prado" }) await f.ProfessionalAsync(u1, name, specialty);
        foreach (var name in new[] { "sala Zeta", "Sala alfa", "sala Beta" }) await f.RoomAsync(u1, name);
        foreach (var (number, name) in new[] { (61, "zélia Nunes"), (62, "Abel Rocha"), (63, "bianca Melo") })
        {
            using var response = await f.Admin.PostAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(number), fullName = name, email = $"alfa{number}@example.test" }); Expect(response, 201);
        }
        foreach (var (number, name) in new[] { (71, "xavier Equipe"), (72, "Bete Equipe") })
        {
            using var response = await f.Municipal.PostAsJsonAsync("/api/municipal/staff", new { fullName = name, email = $"equipe-alfa{number}@example.test", password = "a", role = "attendant" }); Expect(response, 201);
        }

        static IEnumerable<JsonElement> Items(JsonElement root) => root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : root.EnumerateObject().First(property => property.Value.ValueKind == JsonValueKind.Array).Value.EnumerateArray();
        async Task<List<string>> Names(HttpClient client, string path, string field)
        {
            using var response = await client.GetAsync(path); Expect(response, 200);
            return Items(await Read(response)).Select(item => item.GetProperty(field).GetString()!).ToList();
        }
        void Sorted(List<string> names, string label)
        {
            var expected = names.OrderBy(name => name.ToLowerInvariant(), StringComparer.Ordinal).ToList();
            Ensure(names.SequenceEqual(expected), $"{label} fora da ordem A–Z: {string.Join(" | ", names)}");
        }

        await suite.Check("Profissionais e consultórios da UBS em ordem A–Z sem diferenciar maiúsculas", async () =>
        {
            var professionals = await Names(f.Admin, $"/api/obs/units/{u1}/professionals", "fullName");
            Ensure(professionals.Take(4).SequenceEqual(new[] { "Ana Souza", "bruno Dias", "Carla Prado", "zeca Lima" }), $"Profissionais: {string.Join(" | ", professionals)}");
            var rooms = await Names(f.Admin, $"/api/obs/units/{u1}/rooms", "name");
            Ensure(rooms.SequenceEqual(new[] { "Sala alfa", "sala Beta", "sala Zeta" }), $"Consultórios: {string.Join(" | ", rooms)}");
        });
        await suite.Check("Cadastros de referência do OBS e da Secretaria em ordem A–Z", async () =>
        {
            var obs = await Names(f.Admin, $"/api/obs/units/{u1}/registry", "fullName");
            Ensure(obs.SequenceEqual(new[] { "Abel Rocha", "bianca Melo", "zélia Nunes" }), $"Cadastros do OBS: {string.Join(" | ", obs)}");
            Sorted(await Names(f.Municipal, $"/api/municipal/units/{u1}/registry", "fullName"), "Cadastros da Secretaria");
        });
        await suite.Check("Contas da equipe, UBS e especialidades em ordem A–Z", async () =>
        {
            var staff = await Names(f.Municipal, "/api/municipal/staff", "fullName");
            Sorted(staff, "Contas da equipe");
            Ensure(staff.IndexOf("Bete Equipe") < staff.IndexOf("xavier Equipe"), "Bete antes de xavier.");
            Sorted(await Names(f.Municipal, "/api/municipal/units", "name"), "UBS da Secretaria");
            Sorted(await Names(f.Anonymous, "/api/registration/units", "name"), "UBS do cadastro");
            Sorted(await Names(f.Municipal, "/api/municipal/specialties", "name"), "Especialidades");
        });
        return suite.Report();
    }
}
