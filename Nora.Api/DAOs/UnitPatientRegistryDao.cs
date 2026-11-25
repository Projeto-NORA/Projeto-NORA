using System.Net.Mail;
using Npgsql;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Internal database boundary. Callers must authorize staff and the selected UBS before use.</summary>
/// <param name="central">Banco central: o índice de UBS onde cada CPF tem cadastro (conflito de horário entre UBS). Nulo só em teste de unidade isolado, que não tem central.</param>
/// <param name="time">Relógio da caixa de saída do e-mail (o reconciliador compara com o mesmo relógio).</param>
public sealed class UnitPatientRegistryDao(UnitDatabaseRouter databases, DatabaseHelper? central = null, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    /// <summary>Devolve o e-mail de referência que havia antes (já em minúsculas, como é gravado), para o chamador saber se ele mudou.</summary>
    public async Task<string> UpdateEmailAsync(Guid unitId, Guid actorId, string value, string? newEmail, CancellationToken ct = default)
    {
        var cpf = AccountService.NormalizeCpf(value);
        var email = ValidEmail(newEmail);
        if (unitId == Guid.Empty || actorId == Guid.Empty || !AccountService.ValidCpf(cpf)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        string previous;
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.patient_registry r SET email=$2 FROM (SELECT cpf,email FROM nora_unit.patient_registry WHERE cpf=$1 FOR UPDATE) old WHERE r.cpf=old.cpf RETURNING old.email", connection))
        {
            update.Parameters.AddWithValue(cpf); update.Parameters.AddWithValue(email);
            previous = await update.ExecuteScalarAsync(ct) as string ?? throw new ApiNotFoundException();
        }
        await QueueEmailChangeAsync(connection, cpf, previous, email, ct);
        await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry_events(cpf,actor_id,action) VALUES($1,$2,'email_updated')", connection))
        {
            audit.Parameters.AddWithValue(cpf); audit.Parameters.AddWithValue(actorId);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return previous;
    }

    public async Task CreateAsync(Guid unitId, Guid actorId, UnitPatientRecord patient, CancellationToken ct = default)
    {
        var normalized = Normalize(unitId, actorId, patient);
        var cpf = normalized.Cpf; var name = normalized.FullName; var email = normalized.Email; var phone = normalized.Phone; var patientAddress = normalized.Address;
        // O índice central de cadastros é gravado e confirmado ANTES do cadastro na UBS: sobrar uma linha é inofensivo (só faz conferir um banco a mais); faltar abriria um furo na conferência entre UBS.
        if (central is not null)
        {
            await using var index = await central.OpenAsync(ct);
            await central.ExecuteAsync(index, "INSERT INTO nora.unit_registry_index(cpf,unit_id) VALUES($1,$2) ON CONFLICT DO NOTHING", ct, cpf, unitId);
        }
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry(cpf,full_name,email,created_by,phone,cep,street,address_number,complement,neighborhood,city,state,ibge) VALUES($1,$2,$3,$4,NULLIF($5,''),NULLIF($6,''),NULLIF($7,''),NULLIF($8,''),NULLIF($9,''),NULLIF($10,''),NULLIF($11,''),NULLIF($12,''),NULLIF($13,'')) ON CONFLICT(cpf) DO NOTHING", connection))
        {
            foreach (var value in new object[] { cpf, name, email, actorId, phone ?? "", patientAddress?.Cep ?? "", patientAddress?.Street ?? "", patientAddress?.Number ?? "", patientAddress?.Complement ?? "", patientAddress?.Neighborhood ?? "", patientAddress?.City ?? "", patientAddress?.State ?? "", patientAddress?.Ibge ?? "" })
                insert.Parameters.Add(new NpgsqlParameter { Value = value });
            if (await insert.ExecuteNonQueryAsync(ct) != 1) throw new ApiConflictException();
        }
        await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry_events(cpf,actor_id,action) VALUES($1,$2,'created')", connection))
        {
            audit.Parameters.AddWithValue(cpf); audit.Parameters.AddWithValue(actorId);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>Devolve o e-mail de referência que havia antes da edição.</summary>
    public async Task<string> UpdateAsync(Guid unitId, Guid actorId, UnitPatientRecord patient, CancellationToken ct = default)
    {
        var normalized = Normalize(unitId, actorId, patient); var address = normalized.Address;
        await using var connection = await databases.OpenAsync(unitId, ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        string previous;
        await using (var update = new NpgsqlCommand("UPDATE nora_unit.patient_registry r SET full_name=$2,email=$3,phone=NULLIF($4,''),cep=NULLIF($5,''),street=NULLIF($6,''),address_number=NULLIF($7,''),complement=NULLIF($8,''),neighborhood=NULLIF($9,''),city=NULLIF($10,''),state=NULLIF($11,''),ibge=NULLIF($12,'') FROM (SELECT cpf,email FROM nora_unit.patient_registry WHERE cpf=$1 FOR UPDATE) old WHERE r.cpf=old.cpf RETURNING old.email", connection))
        {
            foreach (var value in new object[] { normalized.Cpf, normalized.FullName, normalized.Email, normalized.Phone ?? "", address?.Cep ?? "", address?.Street ?? "", address?.Number ?? "", address?.Complement ?? "", address?.Neighborhood ?? "", address?.City ?? "", address?.State ?? "", address?.Ibge ?? "" })
                update.Parameters.AddWithValue(value);
            previous = await update.ExecuteScalarAsync(ct) as string ?? throw new ApiNotFoundException();
        }
        await QueueEmailChangeAsync(connection, normalized.Cpf, previous, normalized.Email, ct);
        await using (var audit = new NpgsqlCommand("INSERT INTO nora_unit.patient_registry_events(cpf,actor_id,action) VALUES($1,$2,'updated')", connection))
        { audit.Parameters.AddWithValue(normalized.Cpf); audit.Parameters.AddWithValue(actorId); await audit.ExecuteNonQueryAsync(ct); }
        await transaction.CommitAsync(ct);
        return previous;
    }

    /// <summary>
    /// E-mail de referência como é gravado (sem espaços nas pontas, em minúsculas), ou 400 se estiver fora do formato ou passar de 254 caracteres. Quem grava algo
    /// derivado dele antes do cadastro (a sugestão de troca do e-mail da conta) valida por aqui primeiro, para a entrada inválida nunca chegar ao banco.
    /// </summary>
    internal static string ValidEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant() ?? "";
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email) throw new ApiInputException();
        return email;
    }

    /// <summary>Cadastro normalizado e validado (CPF, nome de 2 a 160 caracteres, e-mail de <see cref="ValidEmail"/>, telefone e endereço), ou 400.</summary>
    internal static UnitPatientRecord Normalize(Guid unitId, Guid actorId, UnitPatientRecord patient)
    {
        var cpf = AccountService.NormalizeCpf(patient.Cpf); var name = patient.FullName?.Trim() ?? "";
        if (unitId == Guid.Empty || actorId == Guid.Empty || !AccountService.ValidCpf(cpf) || name.Length is < 2 or > 160 || name.Any(char.IsControl)) throw new ApiInputException();
        return new(cpf, name, ValidEmail(patient.Email), AccountService.NormalizePhone(patient.Phone), AccountService.NormalizeAddress(patient.Address));
    }

    public async Task<List<UnitPatientRecord>> ListAsync(Guid unitId, CancellationToken ct = default)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT cpf,full_name,email,phone,cep,street,address_number,complement,neighborhood,city,state,ibge,is_active,created_at FROM nora_unit.patient_registry ORDER BY lower(full_name),cpf", connection);
        await using var reader = await query.ExecuteReaderAsync(ct);
        var result = new List<UnitPatientRecord>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }

    public async Task<UnitPatientRecord?> FindByCpfAsync(Guid unitId, string? value, CancellationToken ct = default)
    {
        var cpf = AccountService.NormalizeCpf(value);
        if (!AccountService.ValidCpf(cpf)) throw new ApiInputException();
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT cpf,full_name,email,phone,cep,street,address_number,complement,neighborhood,city,state,ibge,is_active,created_at FROM nora_unit.patient_registry WHERE cpf=$1 AND is_active", connection);
        query.Parameters.AddWithValue(cpf);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private static UnitPatientRecord Read(NpgsqlDataReader reader)
    {
        string? Optional(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
        AddressInfo? address = reader.IsDBNull(4) ? null : new(reader.GetString(4), reader.GetString(5), Optional(6), Optional(7), Optional(8), reader.GetString(9), reader.GetString(10), Optional(11));
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), Optional(3), address, reader.GetBoolean(12), false, reader.GetFieldValue<DateTimeOffset>(13));
    }

    /// <summary>
    /// Caixa de saída da sugestão de troca de e-mail da conta: na MESMA transação da UBS que muda o e-mail de referência, se ele mudou, guarda a linha (uma por CPF; a última mudança vale). Só sai depois de a sugestão ser registrada
    /// no banco central (<see cref="Services.EmailSuggestionReconciler"/>), então o e-mail novo nunca fica sem a sugestão correspondente nem ela sem o e-mail.
    /// </summary>
    private async Task QueueEmailChangeAsync(NpgsqlConnection connection, string cpf, string previous, string email, CancellationToken ct)
    {
        if (string.Equals(previous, email, StringComparison.Ordinal)) return;
        await using var queue = new NpgsqlCommand("INSERT INTO nora_unit.email_change_outbox(cpf,id,email,created_at) VALUES($1,gen_random_uuid(),$2,$3) ON CONFLICT (cpf) DO UPDATE SET id=EXCLUDED.id,email=EXCLUDED.email,created_at=EXCLUDED.created_at", connection);
        queue.Parameters.AddWithValue(cpf); queue.Parameters.AddWithValue(email); queue.Parameters.AddWithValue(clock.GetUtcNow());
        await queue.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Linha da caixa de saída do CPF (id da origem, e-mail e instante da correção na UBS), ou nulo.</summary>
    internal async Task<(Guid Id, string Email, DateTimeOffset CreatedAt)?> ReadOutboxAsync(Guid unitId, string cpf, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT id,email,created_at FROM nora_unit.email_change_outbox WHERE cpf=$1", connection);
        query.Parameters.AddWithValue(cpf); await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)) : null;
    }

    /// <summary>CPFs com linha na caixa de saída criada antes de <paramref name="before"/> (mais antigas primeiro), no máximo <paramref name="limit"/>.</summary>
    internal async Task<List<string>> ListStaleOutboxAsync(Guid unitId, DateTimeOffset before, int limit, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT cpf FROM nora_unit.email_change_outbox WHERE created_at<$1 ORDER BY created_at,cpf LIMIT $2", connection);
        query.Parameters.AddWithValue(before); query.Parameters.AddWithValue(limit); await using var reader = await query.ExecuteReaderAsync(ct);
        var cpfs = new List<string>();
        while (await reader.ReadAsync(ct)) cpfs.Add(reader.GetString(0));
        return cpfs;
    }

    /// <summary>Apaga a linha da caixa de saída se ainda for a mesma (uma correção nova no meio do caminho troca o id e a linha nova fica).</summary>
    internal async Task DeleteOutboxAsync(Guid unitId, string cpf, Guid id, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var delete = new NpgsqlCommand("DELETE FROM nora_unit.email_change_outbox WHERE cpf=$1 AND id=$2", connection);
        delete.Parameters.AddWithValue(cpf); delete.Parameters.AddWithValue(id); await delete.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Nomes do cadastro de referência (ativo ou não) dos CPFs informados, numa consulta só. CPF sem cadastro não aparece.</summary>
    public async Task<Dictionary<string, string>> ReadNamesAsync(Guid unitId, string[] cpfs, CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (cpfs.Length == 0) return names;
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT cpf,full_name FROM nora_unit.patient_registry WHERE cpf=ANY($1)", connection);
        query.Parameters.AddWithValue(cpfs);
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) names[reader.GetString(0)] = reader.GetString(1);
        return names;
    }

    public async Task<bool> IsActiveAsync(Guid unitId, string cpf, CancellationToken ct)
    {
        await using var connection = await databases.OpenAsync(unitId, ct);
        await using var query = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM nora_unit.patient_registry WHERE cpf=$1 AND is_active)", connection);
        query.Parameters.AddWithValue(cpf); return await query.ExecuteScalarAsync(ct) is true;
    }
}
