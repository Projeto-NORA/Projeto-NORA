namespace Nora.Desktop.Models;

/// <summary>
/// Um campo da ficha em linhas. <c>Block</c> é para o texto longo (ex.: o corpo de um comunicado): o rótulo fica em cima e o valor embaixo, na largura toda,
/// alinhado à esquerda e com as quebras de linha como foram escritas.
/// </summary>
public sealed record DetailField(string Label, string Value, bool CanCopy = false, string Group = "Informações", bool Block = false);
public sealed record ItemDetails(string Title, string Subtitle, IReadOnlyList<DetailField> Fields);
