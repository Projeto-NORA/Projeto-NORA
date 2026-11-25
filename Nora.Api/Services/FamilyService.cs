using System.Buffers.Binary;
using System.Buffers.Text;
using Npgsql;
using Nora.Api.DAOs;
using Nora.Api.Database;
using Nora.Api.Models;

namespace Nora.Api.Services;

/// <summary>
/// Família: o responsável pede o vínculo com um dependente (CPF e UBS onde ele tem cadastro de referência), a equipe da UBS decide depois de conferir os documentos, e o vínculo aprovado
/// permite ao responsável agir pelo dependente naquela UBS (<see cref="UnitAccessService.ExecuteGuardianAsync{T}"/>). A UBS decide a idade: o servidor não a valida. O dependente não precisa de conta.
/// </summary>
public sealed class FamilyService(DatabaseHelper central, FamilyDao family, UnitPatientRegistryDao registry, HealthUnitDao units, UnitAccessService access, UnitLightningDao lightning, UnitLightningService lightningService, UnitPatientUnlinkDao unlink, TimeProvider time)
{
    public const int MaxStaffListLimit = 200;

    /// <summary>CPF mascarado para o responsável, que só conhece o que digitou: <c>***.456.789-**</c>.</summary>
    public static string MaskCpf(string cpf) => cpf.Length == 11 ? $"***.{cpf[3..6]}.{cpf[6..9]}-**" : "***";

    // ---- Responsável ----

    /// <summary>
    /// Pede o vínculo com um dependente. O CPF precisa ter cadastro ativo de referência na UBS (senão 404); não vale o próprio CPF (400), UBS inexistente ou sem banco pronto (400). Pedido ou vínculo ativo
    /// igual → 409 <c>family-link-exists</c>; mais de 20 pedidos e vínculos ativos juntos → 409 <c>family-link-limit</c>.
    /// </summary>
    public async Task<DependentLink> RequestAsync(SessionInfo session, FamilyRequest request, CancellationToken ct)
    {
        RequirePatient(session);
        var cpf = AccountService.NormalizeCpf(request.Cpf);
        if (!AccountService.ValidCpf(cpf) || request.UnitId is not { } unitId || unitId == Guid.Empty) throw new ApiInputException();
        if (!(await units.ReadyIdsAsync([unitId], ct)).Contains(unitId)) throw new ApiInputException();
        var unit = await units.FindAsync(unitId, ct) ?? throw new ApiInputException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var guardianCpf = await access.RevalidateAsync(connection, session, ct);
        if (guardianCpf == cpf) throw new ApiInputException();
        await family.LockGuardianAsync(connection, session.User.Id, ct);
        var (active, duplicate) = await family.CountActiveAsync(connection, session.User.Id, cpf, unitId, ct);
        if (duplicate) throw new ApiCodedException(StatusCodes.Status409Conflict, "family-link-exists");
        if (active >= FamilyDao.MaxActivePerGuardian) throw new ApiCodedException(StatusCodes.Status409Conflict, "family-link-limit");
        if (!await registry.IsActiveAsync(unitId, cpf, ct)) throw new ApiNotFoundException();
        var id = Guid.NewGuid(); var now = time.GetUtcNow();
        try { await family.InsertAsync(connection, id, session.User.Id, cpf, unitId, now, ct); }
        catch (PostgresException conflict) when (conflict.SqlState == "23505") { throw new ApiCodedException(StatusCodes.Status409Conflict, "family-link-exists"); }
        await transaction.CommitAsync(ct);
        return new(id, "pending", unitId, unit.Name, MaskCpf(cpf), null, now, null, null, null);
    }

    /// <summary>
    /// Pedidos e vínculos do responsável. O nome do dependente vem do cadastro de referência da UBS e só é mostrado depois da aprovação (aprovado ou encerrado); antes disso fica nulo. Se o banco de uma
    /// UBS estiver indisponível ou desatualizado, o nome daquela UBS fica nulo em vez de derrubar a lista: o vínculo é dado central.
    /// Ordem: primeiro os que têm nome, de A a Z pelo nome do dependente (pt-BR, sem diferenciar maiúsculas; desempate pelo nome da UBS e pelo id); depois os sem nome (pedidos
    /// pendentes, recusados ou cancelados, ou UBS fora do ar), na ordem do banco, mais recentes primeiro. O OrderBy do LINQ é estável.
    /// </summary>
    public async Task<List<DependentLink>> ListAsync(SessionInfo session, CancellationToken ct)
    {
        RequirePatient(session);
        var rows = await family.ListForGuardianAsync(session.User.Id, ct);
        var names = new Dictionary<(Guid, string), string>();
        foreach (var unit in rows.Where(NameVisible).GroupBy(row => row.UnitId))
            foreach (var (cpf, name) in await TryNamesAsync(unit.Key, unit.Select(row => row.DependentCpf).Distinct().ToArray(), ct)) names[(unit.Key, cpf)] = name;
        var byName = StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), ignoreCase: true);
        return rows.Select(row => Guardian(row, NameVisible(row) ? names.GetValueOrDefault((row.UnitId, row.DependentCpf)) : null))
            .OrderBy(item => item.DependentName is null).ThenBy(item => item.DependentName ?? "", byName)
            .ThenBy(item => item.DependentName is null ? "" : item.UnitName, byName).ThenBy(item => item.DependentName is null ? Guid.Empty : item.Id).ToList();
    }

    /// <summary>O nome do dependente só aparece depois da aprovação (vínculo aprovado ou, depois, encerrado).</summary>
    private static bool NameVisible(FamilyLinkRow row) => (row.Status is "approved" or "ended") && row.DecidedAt is not null;

    private async Task<Dictionary<string, string>> TryNamesAsync(Guid unitId, string[] cpfs, CancellationToken ct)
    {
        try { return await registry.ReadNamesAsync(unitId, cpfs, ct); }
        catch (Exception error) when (error is ApiCodedException or DatabaseUnavailableException or NpgsqlException) { return []; }
    }

    /// <summary>Cancela um pedido pendente do próprio responsável. De outro responsável ou inexistente → 404; já decidido ou cancelado → 409 <c>family-link-closed</c>.</summary>
    public Task<DependentLink> CancelAsync(SessionInfo session, Guid id, FamilyConfirmRequest request, CancellationToken ct) => CloseAsOwnerAsync(session, id, request, "pending", "cancelled", ct);

    /// <summary>
    /// Encerra um vínculo aprovado a pedido do próprio responsável. De outro responsável ou inexistente → 404; fora de "aprovado" → 409 <c>family-link-closed</c>.
    /// Se o dependente fica sem ninguém que responda por ele na UBS, as consultas futuras marcadas por responsáveis são canceladas (<see cref="UnitPatientUnlinkDao.CancelGuardianBookingsAsync"/>)
    /// no banco da UBS antes do commit central, e a oferta pendente dele cai depois (<see cref="ReleaseOffersAsync"/>).
    /// </summary>
    public Task<DependentLink> EndAsync(SessionInfo session, Guid id, FamilyConfirmRequest request, CancellationToken ct) => CloseAsOwnerAsync(session, id, request, "approved", "ended", ct);

    private async Task<DependentLink> CloseAsOwnerAsync(SessionInfo session, Guid id, FamilyConfirmRequest request, string from, string to, CancellationToken ct)
    {
        RequirePatient(session);
        if (!request.Confirmed || id == Guid.Empty) throw new ApiInputException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await access.RevalidateAsync(connection, session, ct);
        var row = await family.LockAsync(connection, id, session.User.Id, null, ct) ?? throw new ApiNotFoundException();
        if (row.Status != from) throw new ApiCodedException(StatusCodes.Status409Conflict, "family-link-closed");
        if (to == "ended") await family.LockDependentAsync(connection, row.UnitId, row.DependentCpf, ct);
        await family.CloseAsync(connection, id, from, to, session.User.Id, time.GetUtcNow(), ct);
        var updated = await family.ReadAsync(connection, id, ct);
        var lostAccess = to == "ended" && !await family.HasAccessAsync(connection, row.UnitId, row.DependentCpf, ct);
        if (lostAccess) await unlink.CancelGuardianBookingsAsync(row.UnitId, row.DependentCpf, session.User.Id, session.User.Id, ct);
        await transaction.CommitAsync(ct);
        if (lostAccess) await ReleaseOffersAsync(row.UnitId, row.DependentCpf, session.User.Id, "patient", session.User.Id, ct);
        return Guardian(updated, NameVisible(updated) ? (await TryNamesAsync(updated.UnitId, [updated.DependentCpf], ct)).GetValueOrDefault(updated.DependentCpf) : null);
    }

    /// <summary>
    /// Quando o vínculo acaba e o CPF não tem mais ninguém que possa responder nesta UBS (nem outro vínculo aprovado, nem conta própria vinculada), a oferta de Vaga Relâmpago ainda pendente dele é cancelada na hora,
    /// a vaga é reaberta e redistribuída, como nos demais caminhos que tiram a elegibilidade. Roda depois do commit central: se o banco da UBS não responder, a decisão já gravada vale e a oferta vence sozinha em até 10 minutos.
    /// As consultas, ao contrário, são canceladas antes do commit central (ainda com o vínculo e o lock do dependente seguros, na mesma ordem central → UBS de todos os executores): se o banco da UBS
    /// não responder, o encerramento inteiro falha (503) e nada muda, em vez de deixar consultas marcadas para um dependente sem responsável. Resta a janela do commit central depois do da UBS:
    /// as consultas ficam canceladas com o vínculo ainda aprovado, e repetir o encerramento conclui. A redistribuição das ofertas fica depois do commit porque o distribuidor espera o vínculo.
    /// </summary>
    private async Task ReleaseOffersAsync(Guid unitId, string dependentCpf, Guid actorId, string source, Guid? guardianId, CancellationToken ct)
    {
        try { if (await lightning.CancelPendingOffersAsync(unitId, dependentCpf, actorId, source, guardianId, ct) > 0) await lightningService.TryAdvanceAsync(unitId, ct); }
        catch (Exception error) when (error is ApiCodedException or DatabaseUnavailableException or NpgsqlException) { }
    }

    /// <summary>
    /// Ofertas de Vaga Relâmpago ainda abertas de todos os dependentes aprovados, numa só chamada: uma revalidação de sessão e uma consulta por UBS (todos os dependentes daquela UBS juntos). É a rota da consulta
    /// periódica do app para notificar o responsável; não grava nada e não segura a transação central durante as idas às UBS. UBS de schema desatualizado ou cadastro de dependente inativo ficam de fora;
    /// um banco pronto fora do ar responde 503.
    /// </summary>
    public async Task<List<DependentOffers>> PendingOffersAsync(SessionInfo session, CancellationToken ct)
    {
        RequirePatient(session);
        List<(Guid Id, string DependentCpf, Guid UnitId, string UnitName)> links;
        await using (var connection = await central.OpenAsync(ct))
        await using (var transaction = await connection.BeginTransactionAsync(ct))
        {
            await access.RevalidateAsync(connection, session, ct);
            links = await family.LockApprovedAsync(connection, session.User.Id, ct);
            await transaction.CommitAsync(ct);
        }
        var result = new List<DependentOffers>();
        if (links.Count == 0) return result;
        var ready = await units.ReadyIdsAsync(links.Select(link => link.UnitId).Distinct().ToArray(), ct);
        var found = new Dictionary<(Guid UnitId, string Cpf), (string Name, List<UnitLightningOffer> Offers)>();
        foreach (var unit in links.Where(link => ready.Contains(link.UnitId)).GroupBy(link => link.UnitId))
        {
            try
            {
                foreach (var dependent in (await lightning.ListPendingForCpfsAsync(unit.Key, unit.Select(link => link.DependentCpf).Distinct().ToArray(), ct)).GroupBy(row => row.Cpf))
                    found[(unit.Key, dependent.Key)] = (dependent.First().Name, dependent.Select(row => row.Offer).ToList());
            }
            catch (ApiCodedException outdated) when (outdated.Code == "unit-database-outdated") { }
        }
        foreach (var link in links)
            if (found.TryGetValue((link.UnitId, link.DependentCpf), out var dependent)) result.Add(new(link.Id, link.UnitId, link.UnitName, dependent.Name, dependent.Offers));
        return result;
    }

    // ---- Equipe da UBS (chamado dentro do executor de OBS, que já validou conta, sessão e concessão) ----

    /// <summary>
    /// Uma página dos pedidos pendentes (padrão) ou dos vínculos aprovados da UBS, do mais antigo ao mais novo (<c>requested_at, id</c>), com os dados para conferir os documentos. <paramref name="after"/>
    /// é o cursor devolvido pela página anterior (ausente = primeira página; inválido → 400). Lê um item a mais para saber se há outra página sem contar a tabela.
    /// </summary>
    public async Task<FamilyLinkPage> ListForUnitAsync(Guid unitId, string? status, int? limit, string? after, CancellationToken ct)
    {
        var wanted = status ?? "pending";
        var count = limit ?? 100;
        if (wanted is not ("pending" or "approved") || count is < 1 or > MaxStaffListLimit) throw new ApiInputException();
        var rows = await family.ListForUnitAsync(unitId, wanted, count + 1, after is null ? null : DecodeCursor(after), ct);
        var hasMore = rows.Count > count;
        if (hasMore) rows.RemoveAt(count);
        var names = await registry.ReadNamesAsync(unitId, rows.Select(row => row.DependentCpf).Distinct().ToArray(), ct);
        return new(rows.Select(row => Staff(row, names)).ToList(), hasMore ? EncodeCursor(rows[^1]) : null, hasMore);
    }

    /// <summary>Bytes do cursor da lista da equipe: <c>requested_at</c> em ticks UTC (8, big-endian) e o id (16, big-endian), em base64url sem preenchimento (32 caracteres).</summary>
    private const int CursorBytes = 24;

    private static string EncodeCursor(FamilyLinkRow row)
    {
        Span<byte> bytes = stackalloc byte[CursorBytes];
        BinaryPrimitives.WriteInt64BigEndian(bytes, row.RequestedAt.UtcTicks);
        row.Id.TryWriteBytes(bytes[8..], bigEndian: true, out _);
        return Base64Url.EncodeToString(bytes);
    }

    private static (DateTimeOffset RequestedAt, Guid Id) DecodeCursor(string cursor)
    {
        Span<byte> bytes = stackalloc byte[CursorBytes];
        if (!Base64Url.IsValid(cursor, out var length) || length != CursorBytes) throw new ApiInputException();
        Base64Url.DecodeFromChars(cursor, bytes);
        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) throw new ApiInputException();
        return (new DateTimeOffset(ticks, TimeSpan.Zero), new Guid(bytes[8..], bigEndian: true));
    }

    /// <summary>Aprova um pedido pendente da UBS. Confirmação obrigatória. Exige que o dependente ainda tenha cadastro ativo na UBS (senão 409 <c>dependent-inactive</c>).</summary>
    public async Task<FamilyLinkForStaff> ApproveAsync(Guid unitId, Guid actorId, Guid id, FamilyConfirmRequest request, CancellationToken ct)
    {
        if (!request.Confirmed) throw new ApiInputException();
        return await DecideAsync(unitId, actorId, id, "approved", null, ct);
    }

    /// <summary>Recusa um pedido pendente da UBS, com motivo opcional (1 a 280 caracteres). Confirmação obrigatória.</summary>
    public async Task<FamilyLinkForStaff> RejectAsync(Guid unitId, Guid actorId, Guid id, FamilyRejectRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (!request.Confirmed || (reason is not null && (reason.Length is < 1 or > 280 || reason.Any(char.IsControl)))) throw new ApiInputException();
        return await DecideAsync(unitId, actorId, id, "rejected", reason, ct);
    }

    private async Task<FamilyLinkForStaff> DecideAsync(Guid unitId, Guid actorId, Guid id, string to, string? reason, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ApiInputException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var row = await family.LockAsync(connection, id, null, unitId, ct) ?? throw new ApiNotFoundException();
        if (row.Status != "pending") throw new ApiCodedException(StatusCodes.Status409Conflict, "family-link-closed");
        if (to == "approved")
        {
            if (!await registry.IsActiveAsync(unitId, row.DependentCpf, ct)) throw new ApiCodedException(StatusCodes.Status409Conflict, "dependent-inactive");
            await family.LockDependentAsync(connection, unitId, row.DependentCpf, ct);
        }
        await family.DecideAsync(connection, id, to, actorId, time.GetUtcNow(), reason, ct);
        var updated = await family.ReadAsync(connection, id, ct);
        await transaction.CommitAsync(ct);
        return Staff(updated, await TryNamesAsync(unitId, [updated.DependentCpf], ct));
    }

    /// <summary>A equipe encerra um vínculo aprovado da UBS. Espera as operações do responsável em andamento (o executor segura o vínculo). Confirmação obrigatória. Consultas e oferta do dependente que fica sem ninguém: como em <see cref="EndAsync(SessionInfo, Guid, FamilyConfirmRequest, CancellationToken)"/>, com a equipe como autora.</summary>
    public async Task<FamilyLinkForStaff> EndAsync(Guid unitId, Guid actorId, Guid id, FamilyConfirmRequest request, CancellationToken ct)
    {
        if (!request.Confirmed || id == Guid.Empty) throw new ApiInputException();
        await using var connection = await central.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var row = await family.LockAsync(connection, id, null, unitId, ct) ?? throw new ApiNotFoundException();
        if (row.Status != "approved") throw new ApiCodedException(StatusCodes.Status409Conflict, "family-link-closed");
        await family.LockDependentAsync(connection, unitId, row.DependentCpf, ct);
        await family.CloseAsync(connection, id, "approved", "ended", actorId, time.GetUtcNow(), ct);
        var updated = await family.ReadAsync(connection, id, ct);
        var lostAccess = !await family.HasAccessAsync(connection, unitId, row.DependentCpf, ct);
        if (lostAccess) await unlink.CancelGuardianBookingsAsync(unitId, row.DependentCpf, actorId, null, ct);
        await transaction.CommitAsync(ct);
        if (lostAccess) await ReleaseOffersAsync(unitId, row.DependentCpf, actorId, "staff", null, ct);
        return Staff(updated, await TryNamesAsync(unitId, [updated.DependentCpf], ct));
    }

    // ---- Apoio às rotas delegadas ----

    /// <summary>
    /// UBS com banco pronto onde o CPF do dependente pode ter consulta marcada pelo app (vínculos de família aprovados e vínculos da conta própria do dependente, se existir), mais a UBS do agendamento.
    /// É a base do limite de uma consulta futura por especialidade na rede, contado por CPF do dependente.
    /// </summary>
    public async Task<List<Guid>> NetworkUnitsAsync(string dependentCpf, Guid unitId, CancellationToken ct)
    {
        var all = (await family.NetworkUnitIdsAsync(dependentCpf, ct)).Append(unitId).Distinct().ToArray();
        var ready = await units.ReadyIdsAsync(all, ct);
        return all.Where(ready.Contains).ToList();
    }

    private static DependentLink Guardian(FamilyLinkRow row, string? name) => new(row.Id, row.Status, row.UnitId, row.UnitName, MaskCpf(row.DependentCpf), name, row.RequestedAt, row.DecidedAt, row.DecisionReason, row.ClosedAt);

    private static FamilyLinkForStaff Staff(FamilyLinkRow row, Dictionary<string, string> names) => new(row.Id, row.Status, row.GuardianName, row.GuardianCpf, row.DependentCpf,
        names.GetValueOrDefault(row.DependentCpf), row.RequestedAt, row.DecidedByName, row.DecidedAt, row.DecisionReason);

    private static void RequirePatient(SessionInfo session)
    {
        if (session.User.Scope != "patient" || session.User.Role != "patient") throw new ApiForbiddenException();
    }
}
