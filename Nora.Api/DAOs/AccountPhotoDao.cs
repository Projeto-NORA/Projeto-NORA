using Nora.Api.Database;

namespace Nora.Api.DAOs;

/// <summary>Foto de perfil guardada (conteúdo, tipo e ETag sem aspas).</summary>
public sealed record AccountPhoto(string ContentType, byte[] Content, string ETag);

/// <summary>SQL da foto de perfil (banco central, migração 026): uma linha por conta em <c>account_photos</c>.</summary>
public sealed class AccountPhotoDao(DatabaseHelper database)
{
    public Task<AccountPhoto?> GetAsync(Guid accountId, CancellationToken ct) => database.UseAsync(async connection =>
    {
        await using var command = database.Command(connection, "SELECT content_type,content,etag FROM nora.account_photos WHERE account_id=$1", accountId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new AccountPhoto(reader.GetString(0), (byte[])reader[1], reader.GetString(2)) : null;
    }, ct);

    /// <summary>Grava ou troca a foto da conta.</summary>
    public Task<int> SaveAsync(Guid accountId, AccountPhoto photo, DateTimeOffset now, CancellationToken ct) => database.UseAsync(connection => database.ExecuteAsync(connection,
        "INSERT INTO nora.account_photos(account_id,content_type,content,etag,updated_at) VALUES($1,$2,$3,$4,$5) " +
        "ON CONFLICT (account_id) DO UPDATE SET content_type=EXCLUDED.content_type,content=EXCLUDED.content,etag=EXCLUDED.etag,updated_at=EXCLUDED.updated_at",
        ct, accountId, photo.ContentType, photo.Content, photo.ETag, now), ct);

    /// <summary>Remove a foto (sem foto não faz nada: idempotente).</summary>
    public Task<int> DeleteAsync(Guid accountId, CancellationToken ct) => database.UseAsync(connection =>
        database.ExecuteAsync(connection, "DELETE FROM nora.account_photos WHERE account_id=$1", ct, accountId), ct);
}
