namespace Nora.Desktop.Models;

/// <summary>Textos prontos do item de alerta na listagem aberta: título, linha de dados e a etiqueta do tipo.</summary>
public sealed partial record AlertRow
{
    /// <summary>Primeira linha do item: o tipo do alerta e, quando é de uma especialidade, a especialidade.</summary>
    public string ItemTitle => Specialty.Length > 0 ? $"{Title} · {Specialty}" : Title;

    /// <summary>Linha de dados: valor, limite e "desde" (este só quando existe), separados por " | ".</summary>
    public string MetaLine => string.Join(" | ", new[] { $"Valor: {Value}", $"Limite: {Threshold}", Since is "—" or "" ? "" : $"Desde: {Since}" }.Where(text => text.Length > 0));

    /// <summary>Etiqueta do tipo (curta): o texto sempre diz o tipo, a cor só reforça.</summary>
    public string TagText => Kind switch
    {
        "high-queue" => "Fila",
        "long-wait" => "Espera",
        "missed-rate" => "Faltas",
        "occupancy" => "Ocupação",
        "goal-off" => "Meta",
        _ => "Alerta"
    };

    public Tone TagTone => Kind switch
    {
        "high-queue" => Tone.Yellow,
        "long-wait" => Tone.Violet,
        "missed-rate" => Tone.Pink,
        "occupancy" => Tone.Blue,
        "goal-off" => Tone.Teal,
        _ => Tone.Neutral
    };
}
