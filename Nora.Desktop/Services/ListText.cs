namespace Nora.Desktop.Services;

/// <summary>Texto das listagens abertas: a linha de dados do item junta com " | " só os campos informados.</summary>
public static class ListText
{
    public const string Separator = " | ";

    public static string Join(IEnumerable<string?> parts) => string.Join(Separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
}
