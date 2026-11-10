using Microsoft.Extensions.Configuration;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

internal static class UnitDatabaseChecks
{
    public static async Task<int> RunAsync()
    {
        var centralConnection = Environment.GetEnvironmentVariable("NORA_TEST_CONNECTION");
        var firstConnection = Environment.GetEnvironmentVariable("NORA_TEST_UNIT_A_CONNECTION");
        var secondConnection = Environment.GetEnvironmentVariable("NORA_TEST_UNIT_B_CONNECTION");
        if (string.IsNullOrWhiteSpace(centralConnection) || string.IsNullOrWhiteSpace(firstConnection) || string.IsNullOrWhiteSpace(secondConnection))
        {
            Console.WriteLine("SKIP bancos por UBS: configure conexões descartáveis para central, UBS A e UBS B.");
            return 0;
        }
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var incorrect = Guid.NewGuid(); var centralId = Guid.NewGuid();
        var schema = "nora_unit_check_" + Guid.NewGuid().ToString("N");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Nora"] = centralConnection,
            ["Nora:Schema"] = schema,
            [$"Nora:UnitDatabases:{first:N}"] = firstConnection,
            [$"Nora:UnitDatabases:{second:N}"] = secondConnection,
            [$"Nora:UnitDatabases:{incorrect:N}"] = firstConnection,
            [$"Nora:UnitDatabases:{centralId:N}"] = centralConnection
        }).Build();
        await using var central = new DatabaseHelper(config);
        await using var databases = new UnitDatabaseRouter(config);
        var migrations = new UnitMigrationDao(databases, central);
        try
        {
            await using (var connection = await central.OpenAsync())
            await using (var setup = new NpgsqlCommand($"CREATE SCHEMA {schema}; CREATE TABLE {schema}.accounts(id uuid)", connection))
                await setup.ExecuteNonQueryAsync();

            await migrations.MigrateAsync(first); await migrations.MigrateAsync(first);
            await migrations.MigrateAsync(second); await migrations.MigrateAsync(second);
            var registry = new UnitPatientRegistryDao(databases);
            var actor = Guid.NewGuid();
            await registry.CreateAsync(first, actor, new UnitPatientRecord("529.982.247-25", "Paciente Sintético", "PATIENT@example.test", "+55 (48) 99999-0028", new AddressInfo("01001-000", "Praça da Sé", "10", null, "Sé", "São Paulo", "SP")));
            var registered = await registry.FindByCpfAsync(first, "52998224725");
            Require(registered?.Email == "patient@example.test" && registered.Phone == "+5548999990028" && registered.Address?.Cep == "01001000" && registered.Address.Number == "10", "Cadastro da UBS não persistiu com contato e endereço normalizados.");
            Require(await registry.FindByCpfAsync(second, "52998224725") is null, "Cadastro vazou para o banco da outra UBS.");
            try { await registry.CreateAsync(first, actor, new UnitPatientRecord("52998224725", "Outra Pessoa", "other@example.test")); throw new InvalidOperationException("CPF duplicado foi aceito."); }
            catch (ApiConflictException) { }
            var appointments = new AppointmentDao(databases, TimeProvider.System);
            var lightning = new UnitLightningDao(databases, TimeProvider.System);
            var startsAt = DateTimeOffset.UtcNow.AddDays(1);
            var consultation = await appointments.CreateAsync(first, actor, new CreateAppointmentRequest("52998224725", "Cardiologia", "Profissional de teste", startsAt, startsAt.AddMinutes(30)), default);
            try { await lightning.CreateAsync(first, actor, new CreateUnitLightningSlotRequest(consultation.Id, true), default); throw new InvalidOperationException("Consulta não cancelada gerou vaga."); }
            catch (ApiConflictException) { }
            await appointments.ChangeStatusAsync(first, actor, consultation.Id, new AppointmentStatusRequest("cancelled", true), default);
            try { await lightning.CreateAsync(first, actor, new CreateUnitLightningSlotRequest(consultation.Id, false), default); throw new InvalidOperationException("Vaga sem confirmação foi criada."); }
            catch (ApiInputException) { }
            var slot = await lightning.CreateAsync(first, actor, new CreateUnitLightningSlotRequest(consultation.Id, true), default);
            Require(slot.Specialty == "Cardiologia" && slot.Status == "open" && (await lightning.ListAsync(second, default)).Count == 0, "Vaga não preservou especialidade ou vazou entre unidades.");
            try { await lightning.CreateAsync(first, actor, new CreateUnitLightningSlotRequest(consultation.Id, true), default); throw new InvalidOperationException("Consulta gerou vagas duplicadas."); }
            catch (ApiConflictException) { }
            try { await appointments.CreateAsync(first, actor, new CreateAppointmentRequest("52998224725", "Clínica", "PROFISSIONAL DE TESTE", startsAt, startsAt.AddMinutes(30)), default); throw new InvalidOperationException("Consulta sobrepôs vaga em aberto."); }
            catch (ApiConflictException) { }
            var later = startsAt.AddHours(1);
            var cancelled = await appointments.CreateAsync(first, actor, new CreateAppointmentRequest("52998224725", "Clínica", "Outro profissional", later, later.AddMinutes(30)), default);
            await appointments.ChangeStatusAsync(first, actor, cancelled.Id, new AppointmentStatusRequest("cancelled", true), default);
            await appointments.CreateAsync(first, actor, new CreateAppointmentRequest("52998224725", "Clínica", "Outro profissional", later, later.AddMinutes(30)), default);
            try { await lightning.CreateAsync(first, actor, new CreateUnitLightningSlotRequest(cancelled.Id, true), default); throw new InvalidOperationException("Vaga sobrepôs uma consulta já marcada."); }
            catch (ApiConflictException) { }
            await using (var firstDb = await databases.OpenAsync(first))
            await using (var audit = new NpgsqlCommand("SELECT COUNT(*) FROM nora_unit.patient_registry_events WHERE actor_id=$1 AND action='created'", firstDb))
            {
                audit.Parameters.AddWithValue(actor);
                Require((long)(await audit.ExecuteScalarAsync())! == 1, "Cadastro não registrou autor ou duplicou evento.");
            }

            await using (var firstDb = await databases.OpenAsync(first))
            await using (var create = new NpgsqlCommand("CREATE TABLE nora_unit.validation_probe(value text); INSERT INTO nora_unit.validation_probe VALUES ('synthetic')", firstDb))
                await create.ExecuteNonQueryAsync();
            await using (var secondDb = await databases.OpenAsync(second))
            await using (var query = new NpgsqlCommand("SELECT to_regclass('nora_unit.validation_probe') IS NULL", secondDb))
                Require(await query.ExecuteScalarAsync() is true, "Os bancos das UBS não estão separados.");

            try { await using var wrong = await databases.OpenAsync(incorrect); throw new InvalidOperationException("UBS recebeu conexão com identidade de outra unidade."); }
            catch (DatabaseUnavailableException) { }
            try { await migrations.MigrateAsync(incorrect); throw new InvalidOperationException("Migração aceitou banco de outra unidade."); }
            catch (DatabaseUnavailableException) { }
            try { await migrations.MigrateAsync(centralId); throw new InvalidOperationException("Banco central foi aceito como banco de UBS."); }
            catch (DatabaseUnavailableException) { }
            try { await using var missing = await databases.OpenAsync(Guid.NewGuid()); throw new InvalidOperationException("UBS sem conexão foi aceita."); }
            catch (DatabaseUnavailableException) { }
            Console.WriteLine("PASS [PostgreSQL] Bancos separados, cadastros isolados, autoria, migração reaplicável e recusa de conexão incorreta.");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"FAIL [PostgreSQL] Bancos separados por UBS: {error.Message}");
            return 1;
        }
        finally
        {
            await using var connection = await central.OpenAsync();
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
