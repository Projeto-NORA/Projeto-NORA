using System.Net.Http.Headers;
using System.Security.Cryptography;
using static RoundFixture;

/// <summary>
/// Foto de perfil de qualquer conta (<c>/api/auth/photo</c>): binário cru JPEG/PNG/WebP até 2 MB, bytes mágicos conferidos com o Content-Type, GET com
/// <c>Cache-Control: private, no-cache</c> e ETag (SHA-256), DELETE idempotente, uma foto por conta em cada escopo e 403 com troca de senha pendente. Roda com <c>--photo-only</c>.
/// </summary>
internal static class PhotoChecks
{
    private const int Max = 2 * 1024 * 1024;

    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP foto de perfil: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("foto de perfil");
        await using var f = await StartAsync(connection, "photo", unitCount: 0);
        var patient = await f.PatientAsync(1);

        static byte[] Image(byte[] header, int length, int seed)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            header.CopyTo(bytes, 0);
            return bytes;
        }
        static byte[] Jpeg(int length = 64, int seed = 1) => Image([0xFF, 0xD8, 0xFF, 0xE0], length, seed);
        static byte[] Png(int length = 64, int seed = 2) => Image([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], length, seed);
        static byte[] Webp(int length = 64, int seed = 3) => Image([(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x38, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P'], length, seed);
        static string Tag(byte[] content) => $"\"{Convert.ToHexStringLower(SHA256.HashData(content))}\"";

        static Task<HttpResponseMessage> Put(HttpClient client, byte[] content, string? contentType)
        {
            var body = new ByteArrayContent(content);
            if (contentType is not null) body.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            return client.PutAsync("/api/auth/photo", body);
        }

        async Task ExpectPhoto(HttpClient client, byte[] content, string contentType)
        {
            using var response = await client.GetAsync("/api/auth/photo");
            Expect(response, 200);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var cache = response.Headers.CacheControl;
            Ensure(bytes.AsSpan().SequenceEqual(content), "GET devolveu bytes diferentes dos enviados.");
            Ensure(response.Content.Headers.ContentType?.MediaType == contentType, $"Content-Type esperado {contentType}, recebido {response.Content.Headers.ContentType}.");
            Ensure(cache is { Private: true, NoCache: true, NoStore: false }, $"Cache-Control esperado 'private, no-cache', recebido '{cache}'.");
            Ensure(response.Headers.ETag?.Tag == Tag(content) && response.Headers.ETag is { IsWeak: false }, $"ETag esperado {Tag(content)}, recebido {response.Headers.ETag}.");
        }

        await suite.Check("Antes de enviar: GET → 404 photo-not-found (problem+json, no-store); DELETE sem foto → 204", async () =>
        {
            using (var missing = await patient.GetAsync("/api/auth/photo"))
            {
                await ExpectProblem(missing, 404, "photo-not-found");
                Ensure(missing.Headers.CacheControl?.NoStore == true, "Erro da foto pode ser cacheado.");
            }
            using (var delete = await patient.DeleteAsync("/api/auth/photo")) Expect(delete, 204);
        });

        await suite.Check("PUT JPEG, PNG e WebP → 204; GET devolve os mesmos bytes, o Content-Type, Cache-Control private, no-cache e ETag SHA-256 que muda com a foto; parâmetros do Content-Type ignorados", async () =>
        {
            var jpeg = Jpeg(); var png = Png(); var webp = Webp();
            using (var saved = await Put(patient, jpeg, "image/jpeg")) Expect(saved, 204);
            await ExpectPhoto(patient, jpeg, "image/jpeg");
            using (var saved = await Put(patient, png, "IMAGE/PNG; charset=utf-8")) Expect(saved, 204);
            await ExpectPhoto(patient, png, "image/png");
            using (var saved = await Put(patient, webp, "image/webp")) Expect(saved, 204);
            await ExpectPhoto(patient, webp, "image/webp");
            Ensure(Tag(jpeg) != Tag(png) && Tag(png) != Tag(webp), "Fotos diferentes deveriam ter ETag diferentes.");
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.account_photos WHERE account_id=$1", f.PatientIds[1]) == 1, "Uma linha por conta (a troca substitui).");
        });

        await suite.Check("If-None-Match: ETag atual → 304 sem corpo com o mesmo ETag e Cache-Control; ETag antigo → 200", async () =>
        {
            var webp = Webp();
            using (var same = new HttpRequestMessage(HttpMethod.Get, "/api/auth/photo"))
            {
                same.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(Tag(webp)));
                using var response = await patient.SendAsync(same);
                Expect(response, 304);
                Ensure((await response.Content.ReadAsByteArrayAsync()).Length == 0 && response.Headers.ETag?.Tag == Tag(webp) && response.Headers.CacheControl is { Private: true, NoCache: true }, "304 sem corpo, com ETag e Cache-Control.");
            }
            using var old = new HttpRequestMessage(HttpMethod.Get, "/api/auth/photo");
            old.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(Tag(Jpeg())));
            using var changed = await patient.SendAsync(old);
            Expect(changed, 200);
        });

        await suite.Check("Inválidos → 400 photo-invalid sem trocar a foto: bytes que não batem com o tipo, image/gif, application/json, sem Content-Type, corpo vazio, lixo", async () =>
        {
            var cases = new (string Label, byte[] Content, string? Type)[]
            {
                ("PNG declarado como JPEG", Png(), "image/jpeg"), ("JPEG declarado como WebP", Jpeg(), "image/webp"), ("WebP declarado como PNG", Webp(), "image/png"),
                ("RIFF sem WEBP", Image([(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E'], 64, 9), "image/webp"),
                ("GIF", Image("GIF89a"u8.ToArray(), 64, 4), "image/gif"), ("JSON", "{\"photo\":1}"u8.ToArray(), "application/json"), ("JPEG como JSON", Jpeg(), "application/json"),
                ("sem Content-Type", Jpeg(), null), ("vazio", [], "image/jpeg"), ("lixo", Image([0x00, 0x01], 64, 5), "image/png"), ("JPEG curto demais", [0xFF, 0xD8], "image/jpeg")
            };
            foreach (var (label, content, type) in cases)
            {
                using var response = await Put(patient, content, type);
                try { await ExpectProblem(response, 400, "photo-invalid"); }
                catch (Exception error) { throw new InvalidOperationException($"{label}: {error.Message}"); }
            }
            await ExpectPhoto(patient, Webp(), "image/webp");
        });

        await suite.Check("Tamanho: 2 MB exatos aceitos; 2 MB + 1 → 413 photo-too-large sem trocar a foto", async () =>
        {
            var exact = Jpeg(Max, 7);
            using (var saved = await Put(patient, exact, "image/jpeg")) Expect(saved, 204);
            await ExpectPhoto(patient, exact, "image/jpeg");
            using (var tooLarge = await Put(patient, Png(Max + 1, 8), "image/png")) await ExpectProblem(tooLarge, 413, "photo-too-large");
            await ExpectPhoto(patient, exact, "image/jpeg");
        });

        await suite.Check("DELETE → 204 e GET → 404; DELETE de novo → 204", async () =>
        {
            using (var delete = await patient.DeleteAsync("/api/auth/photo")) Expect(delete, 204);
            using (var missing = await patient.GetAsync("/api/auth/photo")) await ExpectProblem(missing, 404, "photo-not-found");
            using (var again = await patient.DeleteAsync("/api/auth/photo")) Expect(again, 204);
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.account_photos WHERE account_id=$1", f.PatientIds[1]) == 0, "A linha da foto deveria ter sido apagada.");
        });

        await suite.Check("Sem sessão ou com token inválido → 401 em GET, PUT e DELETE", async () =>
        {
            using (var get = await f.Anonymous.GetAsync("/api/auth/photo")) Expect(get, 401);
            using (var put = await Put(f.Anonymous, Jpeg(), "image/jpeg")) Expect(put, 401);
            using (var delete = await f.Anonymous.DeleteAsync("/api/auth/photo")) Expect(delete, 401);
            var bogus = f.NewClient(); bogus.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-que-nao-existe");
            using (var get = await bogus.GetAsync("/api/auth/photo")) Expect(get, 401);
        });

        await suite.Check("Paciente, OBS (administrador) e Secretaria: cada conta com a sua foto, isoladas; apagar uma não mexe nas outras", async () =>
        {
            var mine = Jpeg(seed: 11); var admin = Png(seed: 12); var manager = Webp(seed: 13);
            using (var a = await Put(patient, mine, "image/jpeg")) Expect(a, 204);
            using (var b = await Put(f.Admin, admin, "image/png")) Expect(b, 204);
            using (var c = await Put(f.Municipal, manager, "image/webp")) Expect(c, 204);
            await ExpectPhoto(patient, mine, "image/jpeg"); await ExpectPhoto(f.Admin, admin, "image/png"); await ExpectPhoto(f.Municipal, manager, "image/webp");
            using (var delete = await f.Admin.DeleteAsync("/api/auth/photo")) Expect(delete, 204);
            using (var missing = await f.Admin.GetAsync("/api/auth/photo")) await ExpectProblem(missing, 404, "photo-not-found");
            await ExpectPhoto(patient, mine, "image/jpeg"); await ExpectPhoto(f.Municipal, manager, "image/webp");
        });

        await suite.Check("Troca de senha pendente → 403 password-change-required em GET, PUT e DELETE (GET /api/auth/me continua 200)", async () =>
        {
            var pending = await f.PatientAsync(2);
            using (var saved = await Put(pending, Jpeg(seed: 21), "image/jpeg")) Expect(saved, 204);
            await f.Exec("UPDATE nora.accounts SET must_change_password=true WHERE id=$1", f.PatientIds[2]);
            using (var get = await pending.GetAsync("/api/auth/photo")) await ExpectProblem(get, 403, "password-change-required");
            using (var put = await Put(pending, Png(seed: 22), "image/png")) await ExpectProblem(put, 403, "password-change-required");
            using (var delete = await pending.DeleteAsync("/api/auth/photo")) await ExpectProblem(delete, 403, "password-change-required");
            using (var me = await pending.GetAsync("/api/auth/me")) Expect(me, 200);
            Ensure(await f.Scalar<string>("SELECT content_type FROM nora.account_photos WHERE account_id=$1", f.PatientIds[2]) == "image/jpeg", "A recusa não pode mudar a foto.");
        });

        await suite.Check("Limite por conta: 30 PUT/DELETE em 10 min; o 31º → 429 (GET continua); outra conta não é afetada", async () =>
        {
            var busy = await f.PatientAsync(3);
            for (var i = 0; i < 30; i++)
            {
                using var response = i % 2 == 0 ? await Put(busy, Jpeg(seed: 100 + i), "image/jpeg") : await busy.DeleteAsync("/api/auth/photo");
                Expect(response, 204);
            }
            using (var limited = await Put(busy, Jpeg(), "image/jpeg")) await ExpectProblem(limited, 429, "too-many-requests");
            using (var limitedDelete = await busy.DeleteAsync("/api/auth/photo")) await ExpectProblem(limitedDelete, 429, "too-many-requests");
            using (var read = await busy.GetAsync("/api/auth/photo")) await ExpectProblem(read, 404, "photo-not-found");
            using (var other = await Put(f.Municipal, Png(seed: 31), "image/png")) Expect(other, 204);
        });

        return suite.Report();
    }
}
