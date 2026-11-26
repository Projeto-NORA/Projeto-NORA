using System.ComponentModel;
using System.Globalization;

namespace Nora.Desktop.Models;

/// <summary>
/// Tom de cor dos cartões de indicador e das etiquetas de situação. Cada tom tem um fundo suave e uma cor de texto no tema
/// (<c>Tone{Tom}SurfaceBrush</c> e <c>Tone{Tom}TextBrush</c>), com contraste de pelo menos 4,5:1 nos dois temas. O padrão é
/// <see cref="Neutral"/>: o visual de sempre. No XAML vale o nome em inglês ou em português (Azul, Verde, Amarelo, Rosa, Violeta, Turquesa, Neutro).
/// </summary>
[TypeConverter(typeof(ToneConverter))]
public enum Tone
{
    Neutral,
    Blue,
    Green,
    Yellow,
    Pink,
    Violet,
    Teal
}

/// <summary>Aceita, além dos nomes do enum, os nomes em português usados no pedido.</summary>
public sealed class ToneConverter : EnumConverter
{
    private static readonly Dictionary<string, Tone> PortugueseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Neutro"] = Tone.Neutral, ["Azul"] = Tone.Blue, ["Verde"] = Tone.Green, ["Amarelo"] = Tone.Yellow,
        ["Rosa"] = Tone.Pink, ["Violeta"] = Tone.Violet, ["Turquesa"] = Tone.Teal
    };

    public ToneConverter() : base(typeof(Tone)) { }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text && PortugueseNames.TryGetValue(text.Trim(), out var tone) ? tone : base.ConvertFrom(context, culture, value);
}
