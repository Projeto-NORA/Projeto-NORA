using System.Net.Http.Json;
using Nora.Api.Services;
using static RoundFixture;

/// <summary>
/// Sugestão de troca de e-mail que não se perde quando o commit central falha depois do commit da UBS: a caixa de saída da UBS (gravada junto com o e-mail novo) é reprocessada pela repetição da correção
/// e pelo reconciliador, sem duplicar e sem ressuscitar sugestão dispensada. A falha do commit central é simulada com um gatilho adiado de teste (só neste schema descartável). Roda com <c>--email-outbox-only</c>.
/// </summary>
internal static class EmailOutboxChecks
{
    public static async Task<int> RunAsync(string root)
    {
        var connection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP caixa de saída do e-mail: configure NORA_TEST_CONNECTION somente para uma base descartável.");
            return 0;
        }
        var suite = new Suite("caixa de saída do e-mail");
        await using var f = await StartAsync(connection, "emailoutbox", unitCount: 2);
        var u1 = f.Unit1; var u2 = f.Unit2;
        // Gatilho adiado: com a chave armada, o commit central que insere uma sugestão falha (depois de a UBS já ter confirmado o e-mail novo). O teste desarma a chave logo depois.
        await f.Exec("CREATE TABLE nora.email_fail_switch(armed boolean NOT NULL)");
        await f.Exec("INSERT INTO nora.email_fail_switch(armed) VALUES(false)");
        await f.Exec("CREATE FUNCTION nora.email_fail_check() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF (SELECT armed FROM nora.email_fail_switch) THEN RAISE EXCEPTION 'falha simulada do commit central'; END IF; RETURN NULL; END $$");
        await f.Exec("CREATE CONSTRAINT TRIGGER email_fail_trigger AFTER INSERT ON nora.email_change_suggestions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION nora.email_fail_check()");
        async Task Arm(bool armed) => await f.Exec("UPDATE nora.email_fail_switch SET armed=$1", armed);
        async Task<HttpResponseMessage> Correct(int patient, string email) => await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(patient), email });
        async Task<int> CorrectFailing(int patient, string email)
        {
            await Arm(true);
            try { using var response = await Correct(patient, email); return (int)response.StatusCode; }
            finally { await Arm(false); }
        }
        async Task<long> Suggestions(int patient, string? status = null) => await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions s JOIN nora.accounts a ON a.id=s.account_id WHERE a.cpf=$1 AND ($2::text IS NULL OR s.status=$2)", Cpf(patient), (object?)status ?? DBNull.Value);
        async Task<long> Outbox(int patient) => await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.email_change_outbox WHERE cpf=$1", Cpf(patient));
        async Task<string> RegistryEmail(int patient) => await f.UnitScalar<string>(u1, "SELECT email FROM nora_unit.patient_registry WHERE cpf=$1", Cpf(patient));
        var reconciler = f.Service<EmailSuggestionReconciler>();
        foreach (var number in new[] { 1, 2, 3, 5, 6 }) await f.PatientAsync(number, u1);
        await f.PatientAsync(4); await f.RegisterAsync(4, u1); // conta sem vínculo com a UBS

        await suite.Check("Commit central falha depois do da UBS: a rota responde erro, a UBS tem o e-mail novo, não há sugestão e a caixa de saída guarda a linha; repetir a mesma correção recria a sugestão e esvazia a caixa", async () =>
        {
            Ensure(await CorrectFailing(1, "novo1@example.test") >= 500, "A falha do commit central vira erro 5xx.");
            Ensure(await RegistryEmail(1) == "novo1@example.test" && await Suggestions(1) == 0 && await Outbox(1) == 1, "UBS com o e-mail novo, sem sugestão e com a linha na caixa de saída.");
            using (var again = await Correct(1, "novo1@example.test")) Expect(again, 204);
            Ensure(await Suggestions(1, "pending") == 1 && await Outbox(1) == 0 && await f.Scalar<string>("SELECT s.suggested_email FROM nora.email_change_suggestions s JOIN nora.accounts a ON a.id=s.account_id WHERE a.cpf=$1 AND s.status='pending'", Cpf(1)) == "novo1@example.test",
                "Repetir a correção recria a sugestão com o e-mail certo e esvazia a caixa.");
            Ensure(await f.Scalar<long>("SELECT count(*) FROM nora.email_change_suggestions WHERE source_key IS NOT NULL") == 1, "A sugestão guarda a origem (a linha da caixa de saída).");
        });

        await suite.Check("Reconciliador: recupera a sugestão perdida (só de linhas com mais de 2 minutos), roda de novo sem duplicar", async () =>
        {
            Ensure(await CorrectFailing(2, "novo2@example.test") >= 500 && await Suggestions(2) == 0 && await Outbox(2) == 1, "Falha simulada deixa a linha.");
            Ensure(await reconciler.RunOnceAsync(default) == 0 && await Suggestions(2) == 0, "Linha recente (em andamento) não é tocada.");
            f.Clock.Advance(TimeSpan.FromMinutes(3));
            Ensure(await reconciler.RunOnceAsync(default) >= 1 && await Suggestions(2, "pending") == 1 && await Outbox(2) == 0, "O reconciliador recria a sugestão e esvazia a caixa.");
            Ensure(await reconciler.RunOnceAsync(default) == 0 && await Suggestions(2) == 1, "Rodar de novo não duplica.");
        });

        await suite.Check("Sugestão dispensada não renasce: reinserir a linha com o mesmo id não cria nada", async () =>
        {
            Ensure(await CorrectFailing(3, "novo3@example.test") >= 500, "Falha simulada.");
            var id = await f.UnitScalar<Guid>(u1, "SELECT id FROM nora_unit.email_change_outbox WHERE cpf=$1", Cpf(3));
            f.Clock.Advance(TimeSpan.FromMinutes(3));
            await reconciler.RunOnceAsync(default);
            Ensure(await Suggestions(3, "pending") == 1, "Recuperada pelo reconciliador.");
            await f.Exec("UPDATE nora.email_change_suggestions s SET status='dismissed',closed_at=$2,code_hash=NULL,code_expires_at=NULL FROM nora.accounts a WHERE a.id=s.account_id AND a.cpf=$1", Cpf(3), f.Clock.GetUtcNow());
            await f.UnitExec(u1, "INSERT INTO nora_unit.email_change_outbox(cpf,id,email,created_at) VALUES($1,$2,'novo3@example.test',$3)", Cpf(3), id, f.Clock.GetUtcNow().AddMinutes(-5));
            f.Clock.Advance(TimeSpan.FromMinutes(3));
            await reconciler.RunOnceAsync(default);
            Ensure(await Suggestions(3, "pending") == 0 && await Suggestions(3) == 1 && await Outbox(3) == 0, "A sugestão dispensada não renasce e a linha foi consumida.");
        });

        await suite.Check("Sem mudança e sem linha pendente → nenhuma sugestão; conta sem vínculo com a UBS → linha consumida sem sugestão; o PUT do cadastro também usa a caixa de saída; outro e-mail na repetição vence", async () =>
        {
            using (var same = await Correct(5, "paciente5@example.test")) Expect(same, 204);
            Ensure(await Suggestions(5) == 0 && await Outbox(5) == 0, "E-mail igual ao de referência: nada a sugerir.");
            using (var unlinked = await Correct(4, "novo4@example.test")) Expect(unlinked, 204);
            Ensure(await Suggestions(4) == 0 && await Outbox(4) == 0 && await RegistryEmail(4) == "novo4@example.test", "Conta não vinculada: a linha é consumida sem criar sugestão.");
            // PUT do cadastro (UpdateDetails): falha, depois outra correção com e-mail diferente; a sugestão pendente é a do e-mail mais recente.
            await Arm(true);
            try { using var put = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(6), fullName = "Paciente 6", email = "primeiro6@example.test" }); Ensure((int)put.StatusCode >= 500, "PUT falha no commit central."); }
            finally { await Arm(false); }
            Ensure(await RegistryEmail(6) == "primeiro6@example.test" && await Outbox(6) == 1 && await Suggestions(6) == 0, "O PUT também deixa a linha.");
            using (var put = await f.Admin.PutAsJsonAsync($"/api/obs/units/{u1}/registry", new { cpf = Cpf(6), fullName = "Paciente 6", email = "segundo6@example.test" })) Expect(put, 204);
            Ensure(await Suggestions(6, "pending") == 1 && await f.Scalar<string>("SELECT s.suggested_email FROM nora.email_change_suggestions s JOIN nora.accounts a ON a.id=s.account_id WHERE a.cpf=$1 AND s.status='pending'", Cpf(6)) == "segundo6@example.test" && await Outbox(6) == 0,
                "A linha foi substituída pela correção seguinte e a sugestão pendente é a do e-mail mais recente.");
            using (var invalid = await Correct(6, "isto não é e-mail")) Expect(invalid, 400);
            Ensure(await Outbox(6) == 0 && await RegistryEmail(6) == "segundo6@example.test", "E-mail inválido não grava nada.");
        });
        await suite.Check("Linha antiga de outra UBS não troca a sugestão mais nova: a UBS A falha às 10:00, a UBS B corrige às 10:01 e a passada periódica das 10:04 mantém a sugestão da B, com o instante da correção", async () =>
        {
            await f.PatientAsync(7, u1, u2);
            await Arm(true);
            try { using var failing = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u1}/registry/email", new { cpf = Cpf(7), email = "a7@example.test" }); Ensure((int)failing.StatusCode >= 500, "A correção da UBS A falha no commit central."); }
            finally { await Arm(false); }
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.email_change_outbox WHERE cpf=$1", Cpf(7)) == 1 && await Suggestions(7) == 0, "A linha da UBS A sobrou sem sugestão.");
            var older = f.Clock.GetUtcNow();
            f.Clock.Advance(TimeSpan.FromMinutes(1));
            using (var newer = await f.Admin.PatchAsJsonAsync($"/api/obs/units/{u2}/registry/email", new { cpf = Cpf(7), email = "b7@example.test" })) Expect(newer, 204);
            var newerAt = f.Clock.GetUtcNow();
            Ensure(await f.Scalar<string>("SELECT s.suggested_email FROM nora.email_change_suggestions s JOIN nora.accounts a ON a.id=s.account_id WHERE a.cpf=$1 AND s.status='pending'", Cpf(7)) == "b7@example.test", "A sugestão da UBS B ficou pendente.");
            f.Clock.Advance(TimeSpan.FromMinutes(3));
            await reconciler.RunOnceAsync(default);
            Ensure(await f.UnitScalar<long>(u1, "SELECT count(*) FROM nora_unit.email_change_outbox WHERE cpf=$1", Cpf(7)) == 0, "A linha antiga foi consumida.");
            Ensure(await Suggestions(7, "pending") == 1 && await f.Scalar<string>("SELECT s.suggested_email FROM nora.email_change_suggestions s JOIN nora.accounts a ON a.id=s.account_id WHERE a.cpf=$1 AND s.status='pending'", Cpf(7)) == "b7@example.test" && await Suggestions(7) == 1,
                "A sugestão antiga não substituiu a mais nova nem criou outra.");
            Ensure(await f.Scalar<DateTime>("SELECT s.created_at FROM nora.email_change_suggestions s JOIN nora.accounts a ON a.id=s.account_id WHERE a.cpf=$1 AND s.status='pending'", Cpf(7)) == newerAt.UtcDateTime && older < newerAt, "A sugestão nasce com o instante da correção, não o do reprocessamento.");
        });
        return suite.Report() == 0 ? 0 : 1;
    }
}
