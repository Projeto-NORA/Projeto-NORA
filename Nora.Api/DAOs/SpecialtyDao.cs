using System.Globalization;
using System.Text;
using Nora.Api.Database;
using Nora.Api.Models;
using Nora.Api.Services;

namespace Nora.Api.DAOs;

/// <summary>Lista única de especialidades da rede (banco central). Nunca exclui: só desativa.</summary>
public sealed class SpecialtyDao(DatabaseHelper database)
{
    /// <summary>Nome sem acentos, em minúsculas e com espaços colapsados: base da unicidade (não depende do locale do PostgreSQL nem da extensão unaccent).</summary>
    public static string Key(string name)
    {
        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(character)) { pendingSpace = builder.Length > 0; continue; }
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; }
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    /// <summary>Nome validado (2 a 120 caracteres, sem controles); espaços internos repetidos viram um só.</summary>
    public static string ValidName(string? name)
    {
        var text = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < 2 or > 120 || text.Any(char.IsControl)) throw new ApiInputException();
        return text;
    }

    public async Task<List<Specialty>> ListAsync(bool onlyActive, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT id,name,is_active FROM nora.specialties WHERE (NOT $1 OR is_active) ORDER BY name_key,id", onlyActive);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<Specialty>();
        while (await reader.ReadAsync(ct)) items.Add(Read(reader));
        return items;
    }

    /// <summary>As especialidades pedidas que existem (inclusive inativas); ids desconhecidos ficam de fora.</summary>
    public async Task<Dictionary<Guid, Specialty>> FindAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToArray();
        var found = new Dictionary<Guid, Specialty>();
        if (wanted.Length == 0) return found;
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT id,name,is_active FROM nora.specialties WHERE id=ANY($1)", wanted);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { var item = Read(reader); found[item.Id] = item; }
        return found;
    }

    public async Task<Specialty?> FindByNameAsync(string name, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "SELECT id,name,is_active FROM nora.specialties WHERE name_key=$1", Key(name));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>Nome repetido (mesma chave) → violação de unicidade, que a API devolve como 409.</summary>
    public async Task<Specialty> CreateAsync(string name, Guid actorId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "INSERT INTO nora.specialties(id,name,name_key,updated_by) VALUES($1,$2,$3,$4) RETURNING id,name,is_active", Guid.NewGuid(), name, Key(name), actorId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }

    public async Task<Specialty> RenameAsync(Guid id, string name, Guid actorId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "UPDATE nora.specialties SET name=$2,name_key=$3,updated_at=CURRENT_TIMESTAMP,updated_by=$4 WHERE id=$1 RETURNING id,name,is_active", id, name, Key(name), actorId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
        return Read(reader);
    }

    public async Task<Specialty> SetActiveAsync(Guid id, bool isActive, Guid actorId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var command = database.Command(connection, "UPDATE nora.specialties SET is_active=$2,updated_at=CURRENT_TIMESTAMP,updated_by=$3 WHERE id=$1 RETURNING id,name,is_active", id, isActive, actorId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ApiNotFoundException();
        return Read(reader);
    }

    private static Specialty Read(Npgsql.NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetBoolean(2));
}
