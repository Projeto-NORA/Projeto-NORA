namespace Nora.Api.Database;

/// <summary>Migrações de UBS embutidas e a versão esperada do schema (o maior número de migração).</summary>
internal static class UnitSchema
{
    private const string Marker = ".Database.UnitMigrations.";

    /// <summary>Recursos das migrações de UBS em ordem de aplicação.</summary>
    public static IEnumerable<string> ResourceNames => typeof(UnitSchema).Assembly.GetManifestResourceNames()
        .Where(name => name.Contains(Marker, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

    /// <summary>Versão que <c>nora_unit.schema_version</c> deve ter depois de aplicar todas as migrações (ex.: 011).</summary>
    public static int ExpectedVersion { get; } = ResourceNames.Max(name =>
    {
        var file = name[(name.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
        return int.Parse(file[..file.IndexOf('_', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture);
    });
}
