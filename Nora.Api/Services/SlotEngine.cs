namespace Nora.Api.Services;

/// <summary>Intervalo [Start, End) em ticks UTC.</summary>
internal readonly record struct Span(long Start, long End);

/// <summary>Conjunto de intervalos já unidos e ordenados; a consulta de sobreposição é uma busca binária (sem varrer a lista).</summary>
internal sealed class IntervalSet
{
    public static readonly IntervalSet Empty = new([], []);
    private readonly long[] starts;
    private readonly long[] ends;
    private IntervalSet(long[] starts, long[] ends) { this.starts = starts; this.ends = ends; }

    public static IntervalSet Build(List<Span> spans)
    {
        if (spans.Count == 0) return Empty;
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var mergedStarts = new List<long>(spans.Count); var mergedEnds = new List<long>(spans.Count);
        var start = spans[0].Start; var end = spans[0].End;
        for (var i = 1; i < spans.Count; i++)
        {
            if (spans[i].Start <= end) { end = Math.Max(end, spans[i].End); continue; }
            mergedStarts.Add(start); mergedEnds.Add(end); start = spans[i].Start; end = spans[i].End;
        }
        mergedStarts.Add(start); mergedEnds.Add(end);
        return new([.. mergedStarts], [.. mergedEnds]);
    }

    /// <summary>Algum intervalo do conjunto cruza [start, end)? Intervalos que apenas se tocam não se sobrepõem.</summary>
    public bool Overlaps(long start, long end)
    {
        int low = 0, high = starts.Length - 1, found = -1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            if (starts[middle] < end) { found = middle; low = middle + 1; } else high = middle - 1;
        }
        return found >= 0 && ends[found] > start;
    }
}

/// <summary>Linha da grade já com o nome do profissional e do consultório; <c>ProfessionalKey</c> é <c>lower(btrim(nome))</c> calculado pelo PostgreSQL.</summary>
internal sealed record ScheduleTemplateRow(Guid Id, Guid ProfessionalId, string ProfessionalName, string ProfessionalKey, int Weekday, TimeOnly Start, TimeOnly End, int SlotMinutes, Guid RoomId, string RoomName, Guid SpecialtyId, DateOnly? ValidFrom, DateOnly? ValidUntil);

/// <summary>Tudo o que decide se um atendimento da grade está livre, carregado do banco da UBS para uma janela.</summary>
internal sealed class ScheduleData
{
    /// <summary>Só linhas de profissional e consultório ativos.</summary>
    public required List<ScheduleTemplateRow> Templates { get; init; }
    public required IntervalSet UnitBlocks { get; init; }
    public required Dictionary<Guid, IntervalSet> ProfessionalBlocks { get; init; }
    /// <summary>Consultas que ocupam horário (scheduled, called, completed, missed), por chave do profissional.</summary>
    public required Dictionary<string, IntervalSet> Appointments { get; init; }
    /// <summary>Vagas relâmpago abertas ou ofertadas (seguram o horário de uma consulta cancelada), por chave do profissional.</summary>
    public required Dictionary<string, IntervalSet> Held { get; init; }
}

internal readonly record struct GeneratedSlot(ScheduleTemplateRow Template, long Start, long End);

internal enum SlotState { Free, Blocked, Booked, Held }

/// <summary>Calcula os atendimentos da grade semanal no fuso configurado e classifica cada um. Pura: sem acesso a banco nem relógio.</summary>
internal sealed class SlotEngine(TimeZoneInfo zone)
{
    public TimeZoneInfo Zone { get; } = zone;

    /// <summary>Atendimentos cujo início está em [from, to), na ordem dos dias. Horários inexistentes (salto de horário de verão) são pulados.</summary>
    public IEnumerable<GeneratedSlot> Generate(IReadOnlyList<ScheduleTemplateRow> templates, DateTimeOffset from, DateTimeOffset to)
    {
        if (templates.Count == 0 || to <= from) yield break;
        var byWeekday = new List<ScheduleTemplateRow>[7];
        foreach (var template in templates) (byWeekday[template.Weekday] ??= []).Add(template);
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, Zone).DateTime);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(to, Zone).DateTime);
        var fromTicks = from.UtcTicks; var toTicks = to.UtcTicks;
        for (var dayNumber = firstDay.DayNumber; dayNumber <= lastDay.DayNumber; dayNumber++) // pelo número do dia: DateOnly.AddDays(1) em 31/12/9999 lançaria exceção
        {
            var day = DateOnly.FromDayNumber(dayNumber);
            if (byWeekday[(int)day.DayOfWeek] is not { } dayTemplates) continue;
            foreach (var template in dayTemplates)
            {
                if (template.ValidFrom is { } validFrom && validFrom > day || template.ValidUntil is { } validUntil && validUntil < day) continue;
                var step = TimeSpan.FromMinutes(template.SlotMinutes);
                var cursor = day.ToDateTime(template.Start); var limit = day.ToDateTime(template.End);
                for (; cursor + step <= limit; cursor += step)
                {
                    if (Zone.IsInvalidTime(cursor)) continue;
                    var start = new DateTimeOffset(cursor, Zone.GetUtcOffset(cursor)).UtcTicks;
                    if (start >= fromTicks && start < toTicks) yield return new(template, start, start + step.Ticks);
                }
            }
        }
    }

    public static SlotState StateOf(ScheduleData data, GeneratedSlot slot)
    {
        if (data.UnitBlocks.Overlaps(slot.Start, slot.End) || data.ProfessionalBlocks.TryGetValue(slot.Template.ProfessionalId, out var blocks) && blocks.Overlaps(slot.Start, slot.End)) return SlotState.Blocked;
        if (data.Appointments.TryGetValue(slot.Template.ProfessionalKey, out var appointments) && appointments.Overlaps(slot.Start, slot.End)) return SlotState.Booked;
        if (data.Held.TryGetValue(slot.Template.ProfessionalKey, out var held) && held.Overlaps(slot.Start, slot.End)) return SlotState.Held;
        return SlotState.Free;
    }
}
