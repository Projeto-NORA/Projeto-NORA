using Npgsql;

namespace Nora.Api.Database;

public sealed class DatabaseHelper : IAsyncDisposable
{
    private readonly NpgsqlDataSource? _source;
    public bool IsConfigured => _source is not null;
    public string Schema { get; }

    public DatabaseHelper(IConfiguration configuration)
    {
        Schema = configuration["Nora:Schema"] ?? "nora";
        if (!System.Text.RegularExpressions.Regex.IsMatch(Schema, "^[a-z][a-z0-9_]{0,62}$")) throw new InvalidOperationException("Nome de schema inválido.");
        var connection = configuration.GetConnectionString("Nora");
        if (!string.IsNullOrWhiteSpace(connection))
        {
            var safe = ParseConnectionString(connection, "ConnectionStrings__Nora");
            _source = NpgsqlDataSource.Create(safe.ConnectionString);
        }
    }

    internal static NpgsqlConnectionStringBuilder ParseConnectionString(string value, string setting)
    {
        try
        {
            return new NpgsqlConnectionStringBuilder(value) { IncludeErrorDetail = false, LogParameters = false };
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            // O erro do driver pode conter a chave mal interpretada, inclusive uma URI inteira com senha.
            throw new InvalidOperationException($"Configuração {setting} inválida. Use o formato Npgsql Host=...;Database=...;Username=...;Password=...;SSL Mode=VerifyFull.");
        }
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        if (_source is null) throw new DatabaseUnavailableException();
        return await _source.OpenConnectionAsync(ct);
    }

    public async Task<T> UseAsync<T>(Func<NpgsqlConnection, Task<T>> action, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        return await action(connection);
    }

    public NpgsqlCommand Command(NpgsqlConnection connection, string sql, params object[] values)
    {
        var command = new NpgsqlCommand(sql.Replace("nora.", Schema + "."), connection);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value });
        return command;
    }

    public async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = Command(connection, sql, values);
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_source is not null) await _source.DisposeAsync();
    }
}

public sealed class DatabaseUnavailableException : Exception;
