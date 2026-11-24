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
            var trimmed = value.Trim();
            var builder = trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                ? ParsePostgresUri(trimmed)
                : new NpgsqlConnectionStringBuilder(trimmed);
            builder.IncludeErrorDetail = false;
            builder.LogParameters = false;
            return builder;
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            // O erro do driver pode conter a chave mal interpretada, inclusive uma URI inteira com senha.
            throw new InvalidOperationException($"Configuração {setting} inválida. Confira a URL PostgreSQL ou os parâmetros de conexão.");
        }
    }

    private static NpgsqlConnectionStringBuilder ParsePostgresUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Host.Length == 0 || uri.UserInfo.Length == 0 ||
            uri.AbsolutePath.Length < 2 || uri.AbsolutePath[1..].Contains('/') ||
            uri.Fragment.Length != 0) throw new FormatException();
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2 || credentials[0].Length == 0 || credentials[1].Length == 0)
            throw new FormatException();
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath[1..]),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1])
        };
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) throw new FormatException();
            var key = Uri.UnescapeDataString(pair[0]).ToLowerInvariant();
            var option = Uri.UnescapeDataString(pair[1]);
            switch (key)
            {
                case "sslmode":
                    builder.SslMode = option.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "require" => SslMode.Require,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => throw new FormatException()
                    };
                    break;
                case "channel_binding":
                    builder.ChannelBinding = option.ToLowerInvariant() switch
                    {
                        "disable" => ChannelBinding.Disable,
                        "prefer" => ChannelBinding.Prefer,
                        "require" => ChannelBinding.Require,
                        _ => throw new FormatException()
                    };
                    break;
                default: throw new FormatException();
            }
        }
        // As URLs padrão do Neon pedem TLS; confirme também o certificado e o hostname.
        if (uri.Host.EndsWith(".neon.tech", StringComparison.OrdinalIgnoreCase) && builder.SslMode == SslMode.Require)
            builder.SslMode = SslMode.VerifyFull;
        return builder;
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
