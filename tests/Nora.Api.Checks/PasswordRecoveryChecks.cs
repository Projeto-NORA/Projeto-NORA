using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Nora.Api.Services;
using static RoundFixture;

internal static class PasswordRecoveryChecks
{
    private const string Request = "/api/auth/password-reset/request";
    private const string Confirm = "/api/auth/password-reset/confirm";
    private const string NewPassword = "Paciente-teste-alterada!29";

    public static async Task<int> RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP recuperação de senha: exige banco PostgreSQL descartável.");
            return 0;
        }

        await using var fixture = await StartAsync(connection, "recovery", unitCount: 0, extraArgs:
            ["--Nora:PasswordReset:HashKey", "synthetic-password-reset-key-for-tests-only"]);
        var suite = new Suite("recuperação de senha do paciente");

        await suite.Check("CPF cadastrado recebe código no e-mail da conta; CPF desconhecido recebe o mesmo formato sem envio", async () =>
        {
            var patient = await fixture.PatientAsync(142);
            var before = fixture.Mail.Sent.Count;
            var known = await StartRecovery(fixture, 142);
            var unknown = await StartRecovery(fixture, 143);
            Ensure(known != Guid.Empty && unknown != Guid.Empty && known != unknown, "O desafio precisa ser opaco e distinto.");
            Ensure(fixture.Mail.Sent.Count == before + 1 && fixture.Mail.Sent[^1].To == "paciente142@example.test",
                "O envio precisa usar o endereço já cadastrado do paciente.");
            var code = Code(fixture);
            Ensure(!await Reset(fixture, unknown, code), "Um código válido de outro desafio foi aceito.");
            var wrong = code == "000000" ? "111111" : "000000";
            Ensure(!await Reset(fixture, known, wrong), "Um código incorreto foi aceito.");
            Ensure(await Reset(fixture, known, code) && !await Reset(fixture, known, code), "O código precisa valer uma única vez.");
            using var current = await patient.GetAsync("/api/auth/me");
            Expect(current, 401);
            using var oldPassword = await fixture.NewClient().PostAsJsonAsync("/api/auth/login",
                new { scope = "patient", identifier = Cpf(142), password = Password });
            Expect(oldPassword, 401);
            await fixture.LoginAsync(fixture.NewClient(), "patient", Cpf(142), password: NewPassword);
        });

        await suite.Check("Código vencido não altera a senha", async () =>
        {
            await fixture.PatientAsync(144);
            var challenge = await StartRecovery(fixture, 144);
            var code = Code(fixture);
            fixture.Clock.Advance(TimeSpan.FromMinutes(11));
            Ensure(!await Reset(fixture, challenge, code), "Um código vencido foi aceito.");
            await fixture.LoginAsync(fixture.NewClient(), "patient", Cpf(144));
        });

        return suite.Report();
    }

    private static class Assert
    {
        public static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
        {
            try { await action(); }
            catch (T) { return; }
            throw new InvalidOperationException(message);
        }
    }

    private static async Task<Guid> StartRecovery(RoundFixture fixture, int patient)
    {
        using var response = await fixture.NewClient().PostAsJsonAsync(Request, new { cpf = Cpf(patient) });
        Expect(response, 202);
        var body = await Read(response);
        Ensure(body.GetProperty("expiresInSeconds").GetInt32() == 600, "Vida útil inesperada.");
        return body.GetProperty("challengeId").GetGuid();
    }

    private static string Code(RoundFixture fixture)
    {
        var match = Regex.Match(fixture.Mail.Sent[^1].Body, @"\b[0-9]{6}\b");
        Ensure(match.Success, "O e-mail de teste deve conter um código de seis dígitos.");
        return match.Value;
    }

    private static async Task<bool> Reset(RoundFixture fixture, Guid challengeId, string code)
    {
        using var response = await fixture.NewClient().PostAsJsonAsync(Confirm, new { challengeId, code, newPassword = NewPassword });
        Expect(response, 200);
        return (await Read(response)).GetProperty("codeValid").GetBoolean();
    }
}
