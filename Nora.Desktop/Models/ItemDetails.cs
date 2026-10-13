namespace Nora.Desktop.Models;

public sealed record DetailField(string Label, string Value, bool CanCopy = false, string Group = "Informações");
public sealed record ItemDetails(string Title, string Subtitle, IReadOnlyList<DetailField> Fields);
