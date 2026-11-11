using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class PatientRegistrationService(DatabaseHelper central, UnitPatientRegistryDao registry,
    IPasswordHasher<Account> passwords, IEmailSender email, IConfiguration configuration, TimeProvider time)
{
    private static readonly Account HashUser = new(new(Guid.Empty, "", "patient", "patient"), "", "", "");
    private sealed record Challenge(Guid Id, string Cpf, string FullName, string PasswordHash, Guid[] Units,
        DateTimeOffset ExpiresAt, string? EmailHash, string? CodeHash, DateTimeOffset? CodeExpiresAt, short Sends, short Attempts);

    public async Task<RegistrationStartResponse> StartAsync(RegistrationStartRequest request, CancellationToken ct)
    {
        var cpf = AccountService.NormalizeCpf(request.Cpf);
        var name = request.FullName?.Trim() ?? "";
        var units = request.UnitIds?.Distinct().ToArray() ?? [];
        if (!AccountService.ValidCpf(cpf) || name.Length is < 2 or > 160 || name.Any(char.IsControl) ||
            request.Password is null || request.Password.Length is < 8 or > 256 || units.Length is < 1 or > 20 || units.Any(id => id == Guid.Empty))
            throw new ApiInputException();
        var key = SigningKey();
        if (!email.IsConfigured) throw new EmailDeliveryUnavailableException();
        await using (var connection = await central.OpenAsync(ct))
        await using (var verify = central.Command(connection, "SELECT count(*) FROM nora.health_units WHERE id=ANY($1)", units))
            if ((long)(await verify.ExecuteScalarAsync(ct))! != units.Length) throw new ApiInputException();
        var matches = await FindMatchesAsync(units, cpf, ct);
        if (matches.Count == 0) return new(null, []);
        var now = time.GetUtcNow(); var challengeId = Guid.NewGuid();
        var passwordHash = passwords.HashPassword(HashUser, request.Password);
        await using (var connection = await central.OpenAsync(ct))
        await using (var transaction = await connection.BeginTransactionAsync(ct))
        {
            await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
            await using (var limit = central.Command(connection, "SELECT count(*) FROM nora.patient_registration_challenges WHERE cpf=$1 AND created_at>$2", cpf, now.AddHours(-1)))
                if ((long)(await limit.ExecuteScalarAsync(ct))! >= 5) throw new ApiRateLimitException();
            await central.ExecuteAsync(connection, "DELETE FROM nora.patient_registration_challenges WHERE expires_at<$1", ct, now.AddHours(-1));
            await central.ExecuteAsync(connection, "INSERT INTO nora.patient_registration_challenges(id,cpf,full_name,password_hash,unit_ids,created_at,expires_at) VALUES($1,$2,$3,$4,$5,$6,$7)", ct,
                challengeId, cpf, name, passwordHash, units, now, now.AddMinutes(15));
            await transaction.CommitAsync(ct);
        }
        return new(challengeId, matches.Values.Select(record => record.Email).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(address => new RegistrationEmailOption(OptionHash(key, challengeId, address), Mask(address))).ToList());
    }

    public async Task SendCodeAsync(RegistrationSendRequest request, CancellationToken ct)
    {
        if (request.ChallengeId == Guid.Empty || !IsHash(request.EmailOptionId)) throw new ApiInputException();
        var key = SigningKey();
        if (!email.IsConfigured) throw new EmailDeliveryUnavailableException();
        var now = time.GetUtcNow();
        string destination; string code; string codeHash;
        await using (var connection = await central.OpenAsync(ct))
        await using (var transaction = await connection.BeginTransactionAsync(ct))
        {
            var challenge = await ReadAsync(connection, request.ChallengeId, true, ct) ?? throw new ApiConflictException();
            if (challenge.ExpiresAt <= now || challenge.Sends >= 3) throw new ApiConflictException();
            var matches = await FindMatchesAsync(challenge.Units, challenge.Cpf, ct);
            destination = matches.Values.Select(record => record.Email).Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(address => EqualHash(OptionHash(key, challenge.Id, address), request.EmailOptionId!)) ?? throw new ApiConflictException();
            code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            codeHash = CodeHash(key, challenge.Id, code);
            var codeExpires = now.AddMinutes(10) < challenge.ExpiresAt ? now.AddMinutes(10) : challenge.ExpiresAt;
            await central.ExecuteAsync(connection, "UPDATE nora.patient_registration_challenges SET selected_email_hash=$2,code_hash=$3,code_expires_at=$4,sends=sends+1,attempts=0 WHERE id=$1", ct,
                challenge.Id, request.EmailOptionId!, codeHash, codeExpires);
            await transaction.CommitAsync(ct);
        }
        try
        {
            await email.SendAsync(destination, "Código para criar sua conta NORA", $"Seu código de confirmação é: {code}\n\nEle é válido por até 10 minutos. Se você não solicitou este cadastro, ignore esta mensagem.", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await InvalidateCodeAsync(request.ChallengeId, codeHash);
            throw;
        }
        catch (Exception)
        {
            await InvalidateCodeAsync(request.ChallengeId, codeHash);
            throw new EmailDeliveryUnavailableException();
        }
    }

    public async Task<RegistrationConfirmResponse> ConfirmAsync(RegistrationConfirmRequest request, CancellationToken ct)
    {
        if (request.ChallengeId == Guid.Empty) throw new ApiInputException();
        if (request.Code is null || request.Code.Length != 6 || request.Code.Any(c => !char.IsAsciiDigit(c))) return new(false, [], []);
        var key = SigningKey(); var now = time.GetUtcNow();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var challenge = await ReadAsync(connection, request.ChallengeId, true, ct);
        if (challenge is null || challenge.ExpiresAt <= now || challenge.CodeExpiresAt is null || challenge.CodeExpiresAt <= now ||
            challenge.CodeHash is null || challenge.EmailHash is null || challenge.Attempts >= 5)
        { await transaction.CommitAsync(ct); return new(false, [], []); }
        if (!EqualHash(challenge.CodeHash, CodeHash(key, challenge.Id, request.Code)))
        {
            await central.ExecuteAsync(connection, "UPDATE nora.patient_registration_challenges SET attempts=LEAST(attempts+1,5) WHERE id=$1", ct, challenge.Id);
            await transaction.CommitAsync(ct); return new(false, [], []);
        }
        await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, challenge.Cpf);
        var matches = await FindMatchesAsync(challenge.Units, challenge.Cpf, ct);
        var verifiedEmail = matches.Values.Select(record => record.Email).Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(address => EqualHash(OptionHash(key, challenge.Id, address), challenge.EmailHash)) ?? throw new ApiConflictException();
        var accountId = Guid.NewGuid();
        await using (var insert = central.Command(connection, "INSERT INTO nora.accounts(id,full_name,cpf,email,password_hash,scope,role) VALUES($1,$2,$3,$4,$5,'patient','patient') ON CONFLICT DO NOTHING RETURNING id", accountId, challenge.FullName, challenge.Cpf, verifiedEmail, challenge.PasswordHash))
            if (await insert.ExecuteScalarAsync(ct) is not Guid) throw new ApiConflictException();
        foreach (var unitId in matches.Keys)
        {
            await central.ExecuteAsync(connection, "INSERT INTO nora.patient_units(patient_id,unit_id) VALUES($1,$2)", ct, accountId, unitId);
            await central.ExecuteAsync(connection, "INSERT INTO nora.patient_unit_link_events(patient_id,unit_id) VALUES($1,$2)", ct, accountId, unitId);
        }
        await central.ExecuteAsync(connection, "DELETE FROM nora.patient_registration_challenges WHERE id=$1", ct, challenge.Id);
        await transaction.CommitAsync(ct);
        return new(true, matches.Keys.ToList(), challenge.Units.Where(id => !matches.ContainsKey(id)).ToList());
    }

    public async Task<PatientUnitLinkResponse> LinkUnitsAsync(SessionInfo session, PatientUnitLinkRequest request, CancellationToken ct)
    {
        var units = request.UnitIds?.Distinct().ToArray() ?? [];
        if (session.User.Scope != "patient" || session.User.Role != "patient") throw new ApiForbiddenException();
        if (units.Length is < 1 or > 20 || units.Any(id => id == Guid.Empty)) throw new ApiInputException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        string cpf;
        await using (var account = central.Command(connection, "SELECT a.cpf FROM nora.accounts a JOIN nora.sessions s ON s.account_id=a.id WHERE a.id=$1 AND a.is_active AND s.token_hash=$2 AND s.revoked_at IS NULL AND s.expires_at>$3 FOR SHARE OF a,s", session.User.Id, session.TokenHash, time.GetUtcNow()))
            cpf = await account.ExecuteScalarAsync(ct) as string ?? throw new ApiUnauthorizedException();
        await central.ExecuteAsync(connection, "SELECT pg_advisory_xact_lock(hashtextextended($1,62419805))", ct, cpf);
        await using (var verify = central.Command(connection, "SELECT count(*) FROM nora.health_units WHERE id=ANY($1)", units))
            if ((long)(await verify.ExecuteScalarAsync(ct))! != units.Length) throw new ApiInputException();
        var matches = await FindMatchesAsync(units, cpf, ct);
        foreach (var unitId in matches.Keys)
        {
            await using var insert = central.Command(connection, "INSERT INTO nora.patient_units(patient_id,unit_id) VALUES($1,$2) ON CONFLICT DO NOTHING RETURNING unit_id", session.User.Id, unitId);
            if (await insert.ExecuteScalarAsync(ct) is Guid)
                await central.ExecuteAsync(connection, "INSERT INTO nora.patient_unit_link_events(patient_id,unit_id) VALUES($1,$2)", ct, session.User.Id, unitId);
        }
        await transaction.CommitAsync(ct);
        return new(matches.Keys.ToList(), units.Where(id => !matches.ContainsKey(id)).ToList());
    }

    private async Task<Dictionary<Guid, UnitPatientRecord>> FindMatchesAsync(IEnumerable<Guid> units, string cpf, CancellationToken ct)
    {
        var matches = new Dictionary<Guid, UnitPatientRecord>();
        foreach (var unit in units)
        {
            var record = await registry.FindByCpfAsync(unit, cpf, ct);
            if (record != null) matches.Add(unit, record);
        }
        return matches;
    }
    private async Task<Challenge?> ReadAsync(NpgsqlConnection connection, Guid id, bool locked, CancellationToken ct)
    {
        await using var query = central.Command(connection, "SELECT id,cpf,full_name,password_hash,unit_ids,expires_at,selected_email_hash,code_hash,code_expires_at,sends,attempts FROM nora.patient_registration_challenges WHERE id=$1" + (locked ? " FOR UPDATE" : ""), id);
        await using var reader = await query.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<Guid[]>(4),
            reader.GetFieldValue<DateTimeOffset>(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8), reader.GetInt16(9), reader.GetInt16(10)) : null;
    }
    private async Task InvalidateCodeAsync(Guid id, string hash)
    {
        await using var connection = await central.OpenAsync(CancellationToken.None);
        await central.ExecuteAsync(connection, "UPDATE nora.patient_registration_challenges SET code_hash=NULL,code_expires_at=NULL WHERE id=$1 AND code_hash=$2", CancellationToken.None, id, hash);
    }
    private byte[] SigningKey()
    {
        var value = configuration["Nora:Registration:HashKey"];
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < 32) throw new EmailDeliveryUnavailableException();
        return Encoding.UTF8.GetBytes(value);
    }
    private static string Hash(byte[] key, string text) => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text)));
    private static string OptionHash(byte[] key, Guid id, string address) => Hash(key, $"email:{id:N}:{address.ToLowerInvariant()}");
    private static string CodeHash(byte[] key, Guid id, string code) => Hash(key, $"code:{id:N}:{code}");
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool EqualHash(string first, string second) => IsHash(first) && IsHash(second) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));
    private static string Mask(string address)
    {
        var at = address.IndexOf('@');
        return at > 0 ? address[..1] + "***" + address[at..] : "***";
    }
}
