using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class UnitRegistryReadService(UnitPatientRegistryDao registry, UnitMetricsDao metrics, DatabaseHelper central, TimeProvider time)
{
    /// <summary>Janela do score por paciente: últimos 12 meses, pela data marcada da consulta (sem limite superior: futuras canceladas entram).</summary>
    public const int AttendanceWindowMonths = 12;

    /// <summary>Intervalo de datas marcadas que entra no score (a mesma janela do registry, do attendance do paciente e da prioridade da Vaga Relâmpago).</summary>
    internal static (DateTimeOffset From, DateTimeOffset To) AttendanceWindow(DateTimeOffset now) => (now.AddMonths(-AttendanceWindowMonths), now.AddYears(10));

    /// <summary>
    /// Cadastro da UBS com presença; com <paramref name="withAttendance"/> também o comparecimento e o score em pontos de cada paciente.
    /// </summary>
    /// <param name="withAttendance">Só a listagem do OBS recebe o campo attendance; a da Secretaria não.</param>
    /// <param name="centralConnection">Conexão central que o executor de acesso já tem aberta (P3, docs/desempenho.md): a presença é lida nela em vez de abrir uma segunda. Nula só onde não há executor com conexão (Secretaria).</param>
    public async Task<List<UnitPatientRecord>> ListAsync(Guid unitId, bool withAttendance, NpgsqlConnection? centralConnection, CancellationToken ct)
    {
        var records = await registry.ListAsync(unitId, ct);
        if (records.Count == 0) return records;
        var online = new HashSet<string>(StringComparer.Ordinal);
        var now = time.GetUtcNow();
        var owned = centralConnection is null ? await central.OpenAsync(ct) : null;
        try
        {
            await using var query = central.Command(centralConnection ?? owned!, "SELECT a.cpf FROM nora.accounts a WHERE a.scope='patient' AND a.is_active AND a.cpf=ANY($1) AND EXISTS(SELECT 1 FROM nora.sessions s WHERE s.account_id=a.id AND s.revoked_at IS NULL AND s.expires_at>$2 AND s.presence_seen_at>$3)", records.Select(record => record.Cpf).ToArray(), now, now - AuthService.PresenceTimeout);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) online.Add(reader.GetString(0));
        }
        finally { if (owned is not null) await owned.DisposeAsync(); }
        var (from, to) = AttendanceWindow(now);
        var tallies = withAttendance ? await metrics.ReadAttendanceAsync(unitId, from, to, "", ct) : null;
        return records.Select(record => record with
        {
            IsOnline = online.Contains(record.Cpf),
            Attendance = tallies is null ? null : Attendance(tallies.GetValueOrDefault(record.Cpf))
        }).ToList();
    }

    /// <summary>Score do próprio paciente (CPF da sessão) na UBS, mesma janela de 12 meses.</summary>
    public async Task<PatientAttendanceSelf> ReadOwnAttendanceAsync(Guid unitId, string cpf, CancellationToken ct)
    {
        var (from, to) = AttendanceWindow(time.GetUtcNow());
        var tallies = await metrics.ReadAttendanceAsync(unitId, from, to, cpf, ct);
        var attendance = Attendance(tallies.GetValueOrDefault(cpf));
        return new(attendance.Completed, attendance.Missed, attendance.CancelledByPatient, attendance.CancellationWeight, attendance.Score, AttendanceWindowMonths, attendance.Points, attendance.Band, attendance.HasHistory);
    }

    internal static PatientAttendance Attendance(PatientTally tally)
    {
        var score = tally.Score; var points = tally.Points;
        return new(tally.Completed, tally.Missed, tally.CancelledByPatient, MetricsService.Round(tally.Weight), score is { } value ? MetricsService.Round(value) : null, points, AttendanceScore.Band(points), score is not null);
    }
}
