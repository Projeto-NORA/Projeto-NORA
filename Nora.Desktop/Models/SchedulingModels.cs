using System.Globalization;
using System.Text.Json.Serialization;
using Nora.Desktop.Services;

namespace Nora.Desktop.Models;

// Contratos da agenda por catálogo, capacidade, metas e alertas (docs/api.md, seção "Capacidade, agendamento e metas").

/// <summary>Especialidade da lista única da rede (banco central); a Secretaria mantém, a UBS escolhe dela.</summary>
public sealed record Specialty(Guid Id, string Name, bool IsActive)
{
    [JsonIgnore] public string StatusDisplay => IsActive ? "Ativa" : "Desativada";
    [JsonIgnore] public string SelectorLabel => IsActive ? Name : Name + " (desativada)";
}
public sealed record SpecialtyList(Specialty[] Specialties);

/// <summary>Profissional da UBS: registro de conselho opcional (sigla, número e UF, todos ou nenhum), sem validação externa.</summary>
public sealed record Professional(Guid Id, string FullName, string? CouncilAcronym, string? CouncilNumber, string? CouncilState, bool IsActive, Guid[] SpecialtyIds)
{
    /// <summary>Nomes das especialidades, preenchidos pelo cliente a partir da lista da rede.</summary>
    [JsonIgnore] public string SpecialtiesDisplay { get; init; } = "";
    [JsonIgnore] public string StatusDisplay => IsActive ? "Ativo" : "Desativado";
    [JsonIgnore] public string CouncilDisplay => string.IsNullOrWhiteSpace(CouncilAcronym) ? "Não informado" : $"{CouncilAcronym} {CouncilNumber}/{CouncilState}";
    [JsonIgnore] public string SelectorLabel => IsActive ? FullName : FullName + " (desativado)";
    /// <summary>Segunda linha do item da listagem: especialidades e registro no conselho, só o que foi informado, separados por " | ".</summary>
    [JsonIgnore] public string MetaLine => string.Join(" | ", new[] { SpecialtiesDisplay, string.IsNullOrWhiteSpace(CouncilAcronym) ? "" : CouncilDisplay }.Where(part => part.Length > 0));
    [JsonIgnore] public Tone StatusTone => IsActive ? Tone.Green : Tone.Neutral;

    // Igualdade pelo conteúdo: o record compararia o vetor de especialidades pela referência, e cada leitura da API traz um vetor novo.
    // Sem isto toda releitura trocaria todos os itens das listas e os seletores perderiam a escolha em andamento.
    public bool Equals(Professional? other) => other is not null && Id == other.Id && FullName == other.FullName && CouncilAcronym == other.CouncilAcronym && CouncilNumber == other.CouncilNumber
        && CouncilState == other.CouncilState && IsActive == other.IsActive && SpecialtiesDisplay == other.SpecialtiesDisplay && SpecialtyIds.AsSpan().SequenceEqual(other.SpecialtyIds);
    public override int GetHashCode() => HashCode.Combine(Id, FullName, IsActive, SpecialtiesDisplay, SpecialtyIds.Length);
}
public sealed record ProfessionalList(Professional[] Professionals);

public sealed record Room(Guid Id, string Name, bool IsActive)
{
    [JsonIgnore] public string StatusDisplay => IsActive ? "Ativo" : "Desativado";
    [JsonIgnore] public string SelectorLabel => IsActive ? Name : Name + " (desativado)";
    [JsonIgnore] public Tone StatusTone => IsActive ? Tone.Green : Tone.Neutral;
}
public sealed record RoomList(Room[] Rooms);

/// <summary>Linha da grade semanal. <c>weekday</c> 0 = domingo a 6 = sábado; horários "HH:mm"; vigência "yyyy-MM-dd" ou nula (ilimitada).</summary>
public sealed record ScheduleTemplate(Guid Id, Guid ProfessionalId, int Weekday, string StartTime, string EndTime, int SlotMinutes, Guid RoomId, Guid SpecialtyId, string? ValidFrom, string? ValidUntil)
{
    [JsonIgnore] public string ProfessionalName { get; init; } = "";
    [JsonIgnore] public string RoomName { get; init; } = "";
    [JsonIgnore] public string SpecialtyName { get; init; } = "";
    [JsonIgnore] public string WeekdayDisplay => WeekdayName(Weekday);
    [JsonIgnore] public string RangeDisplay => $"{StartTime}–{EndTime}";
    [JsonIgnore] public string SlotDisplay => $"{SlotMinutes} min";
    /// <summary>Primeira linha do item da grade: "08:00 às 12:00 · atendimentos de 20 min".</summary>
    [JsonIgnore] public string TitleDisplay => $"{StartTime} às {EndTime} · atendimentos de {SlotMinutes} min";
    /// <summary>Segunda linha do item da grade: profissional, especialidade, consultório e vigência (só quando há limite), separados por " | ".</summary>
    [JsonIgnore] public string MetaLine => string.Join(" | ", new[] { ProfessionalName, SpecialtyName, RoomName, ValidFrom is null && ValidUntil is null ? "" : ValidityDisplay }.Where(part => part.Length > 0));
    [JsonIgnore] public string ValidityDisplay => (ValidFrom, ValidUntil) switch
    {
        (null, null) => "Sem limite",
        (var from, null) => "A partir de " + DateText(from),
        (null, var until) => "Até " + DateText(until),
        var (from, until) => DateText(from) + " a " + DateText(until)
    };
    public static string WeekdayName(int weekday) => weekday is >= 0 and <= 6 ? new[] { "Domingo", "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado" }[weekday] : "—";
    private static string DateText(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? DisplayFormat.Date(date) : value ?? "";
}
public sealed record ScheduleList(ScheduleTemplate[] Schedule);

/// <summary>Bloqueio de agenda (folga ou feriado): de um profissional ou, sem profissional, da UBS inteira.</summary>
public sealed record ScheduleBlock(Guid Id, Guid? ProfessionalId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Reason)
{
    [JsonIgnore] public string ScopeDisplay { get; init; } = "";
    [JsonIgnore] public string PeriodDisplay => IntervalDisplay(StartsAt, EndsAt);
    /// <summary>Segunda linha do item de bloqueio: quem é atingido, o início e o fim (dia inteiro mostra só as datas; um dia só vira uma data).</summary>
    [JsonIgnore] public string MetaLine
    {
        get
        {
            var from = StartsAt.ToLocalTime(); var to = EndsAt.ToLocalTime();
            if (from.TimeOfDay != TimeSpan.Zero || to.TimeOfDay != TimeSpan.Zero) return string.Join(" | ", new[] { ScopeDisplay, "início " + from.ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture), "fim " + to.ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) }.Where(part => part.Length > 0));
            var last = to.Date.AddDays(-1);
            return string.Join(" | ", last == from.Date
                ? new[] { ScopeDisplay, from.ToString("dd/MM/yyyy", DisplayFormat.Culture) + " (dia inteiro)" }.Where(part => part.Length > 0)
                : new[] { ScopeDisplay, "início " + from.ToString("dd/MM/yyyy", DisplayFormat.Culture), "fim " + last.ToString("dd/MM/yyyy", DisplayFormat.Culture) }.Where(part => part.Length > 0));
        }
    }
    /// <summary>Dia inteiro (da meia-noite local à meia-noite local seguinte, em um ou mais dias) aparece só com as datas.</summary>
    public static string IntervalDisplay(DateTimeOffset starts, DateTimeOffset ends)
    {
        var from = starts.ToLocalTime(); var to = ends.ToLocalTime();
        if (from.TimeOfDay == TimeSpan.Zero && to.TimeOfDay == TimeSpan.Zero)
        {
            var last = to.Date.AddDays(-1);
            return last == from.Date ? from.ToString("dd/MM/yyyy", DisplayFormat.Culture) + " (dia inteiro)" : $"{from:dd/MM/yyyy} a {last:dd/MM/yyyy} (dias inteiros)";
        }
        return $"{from:dd/MM/yyyy HH:mm} a {to:dd/MM/yyyy HH:mm}";
    }
}
public sealed record ScheduleBlockList(ScheduleBlock[] Blocks);

/// <summary>Horário livre devolvido por GET .../availability.</summary>
public sealed record AvailableSlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid ProfessionalId, string ProfessionalName, Guid SpecialtyId, string SpecialtyName, Guid RoomId, string RoomName)
{
    [JsonIgnore] public string Display => $"{StartsAt.ToLocalTime().ToString("ddd dd/MM", DisplayFormat.Culture)} · {StartsAt.ToLocalTime():HH:mm}–{EndsAt.ToLocalTime():HH:mm} · {RoomName}";
    [JsonIgnore] public string DateDisplay => StartsAt.ToLocalTime().ToString("ddd dd/MM/yyyy", DisplayFormat.Culture);
    [JsonIgnore] public string TimeDisplay => $"{StartsAt.ToLocalTime():HH:mm}–{EndsAt.ToLocalTime():HH:mm}";
}
public sealed record AvailabilityList(AvailableSlot[] Slots);

public sealed record ActiveRooms(int RoomsInUse, int RoomsPlannedToday);

/// <summary>Capacidade da grade na janela: horários ofertados, ocupados e a ocupação (0 a 1; nula sem horários ofertados).</summary>
public sealed record CapacityMetrics(long OfferedSlots, long BookedSlots, double? Occupancy);

/// <summary>Meta definida pela Secretaria: <c>unitId</c> nulo = meta da rede. Score e taxa de faltas em fração (0 a 1); espera em minutos.</summary>
public sealed record Goal(string Metric, Guid? UnitId, decimal Target);
public sealed record GoalList(Goal[] Goals);

/// <summary>Situação de uma meta nos indicadores: <c>met</c>, <c>near</c>, <c>off</c> ou <c>no-data</c>.</summary>
public sealed record GoalStatus(string Metric, decimal Target, double? Value, string Status);

/// <summary>Alerta avaliado pela API, sem dado pessoal. <c>since</c> só existe para espera longa.</summary>
public sealed record AlertItem(string Kind, Guid UnitId, string UnitName, string? Specialty, double Value, double Threshold, DateTimeOffset? Since, string? Metric);
public sealed record AlertList(AlertItem[] Alerts);

public static class GoalMetric
{
    public const string Score = "attendance_score", MissedRate = "missed_rate", AverageWait = "average_wait_minutes";
    public static readonly string[] All = [Score, MissedRate, AverageWait];
    public static string Title(string metric) => metric switch { Score => "Comparecimento", MissedRate => "Taxa de faltas", AverageWait => "Espera média da fila", _ => metric };
    /// <summary>"mín." para o que precisa ficar acima (score) e "máx." para o que precisa ficar abaixo.</summary>
    public static string Bound(string metric) => metric == Score ? "mín." : "máx.";
    public static string Format(string metric, double value) => metric == AverageWait ? DisplayFormat.Wait(value) : DisplayFormat.Percent(value);
}

/// <summary>Situação de uma meta pronta para exibir: o estado vem escrito e com ícone, nunca só em cor.</summary>
public sealed record GoalCell(string Title, string Status, string StatusDisplay, string Glyph, string Detail)
{
    public string AutomationLabel => $"{Title}: {StatusDisplay}. {Detail}";
    public static GoalCell None(string title, string detail = "Sem meta definida pela Secretaria.") => new(title, "none", "Sem meta", "", detail);
    public static GoalCell From(GoalStatus goal)
    {
        var (status, glyph) = goal.Status switch
        {
            "met" => ("Atingida", ""),
            "near" => ("Perto da meta", ""),
            "off" => ("Fora da meta", ""),
            _ => ("Sem dado", "")
        };
        var target = $"meta {GoalMetric.Bound(goal.Metric)} {GoalMetric.Format(goal.Metric, (double)goal.Target)}";
        var detail = goal.Value is { } value ? $"{GoalMetric.Format(goal.Metric, value)} · {target}" : $"Sem valor no período · {target}";
        return new(GoalMetric.Title(goal.Metric), goal.Status, status, glyph, detail);
    }
    /// <summary>Uma célula por métrica, na ordem fixa (comparecimento, faltas, espera); métrica sem meta vira "Sem meta".</summary>
    public static GoalCell[] ForAll(IEnumerable<GoalStatus>? goals) =>
        [.. GoalMetric.All.Select(metric => goals?.FirstOrDefault(goal => goal.Metric == metric) is { } found ? From(found) : None(GoalMetric.Title(metric)))];
}

/// <summary>Linha da tabela de metas por UBS (e da rede): as três métricas lado a lado.</summary>
public sealed record GoalUnitRow(string Name, GoalCell Score, GoalCell Missed, GoalCell Wait);

/// <summary>Linha da capacidade (UBS ou região): horários ofertados e ocupados e a ocupação.</summary>
public sealed record CapacityRow(string Name, string Region, long Offered, long Booked, double? Occupancy)
{
    public string OccupancyDisplay => DisplayFormat.Percent(Occupancy);
    public double OccupancySort => Occupancy ?? -1;
    public double OccupancyBar => Math.Clamp((Occupancy ?? 0) * 100, 0, 100);
    public string OfferedDisplay => DisplayFormat.Count(Offered);
    public string BookedDisplay => DisplayFormat.Count(Booked);
}

/// <summary>Alerta pronto para exibir: tipo, especialidade, valor, limite e desde quando.</summary>
public sealed partial record AlertRow(string UnitName, string Title, string Specialty, string Value, string Threshold, string Since, string Description, string Kind = "")
{
    public string AutomationLabel => $"{Title}{(Specialty.Length > 0 ? " · " + Specialty : "")}: {Value}, limite {Threshold}";
    /// <summary>Texto da tabela: "—" quando o alerta não é de uma especialidade.</summary>
    public string SpecialtyDisplay => Specialty.Length > 0 ? Specialty : "—";

    public static AlertRow From(AlertItem item)
    {
        var (title, value, threshold) = item.Kind switch
        {
            "high-queue" => ("Fila alta", Count(item.Value, "pessoa", "pessoas"), Count(item.Threshold, "pessoa", "pessoas")),
            "long-wait" => ("Espera longa", AboveDays(item.Value, item.Threshold), Count(item.Threshold, "dia", "dias")),
            "missed-rate" => ("Taxa de faltas acima do limite", AbovePercent(item.Value, item.Threshold), DisplayFormat.Percent(item.Threshold)),
            "occupancy" => ("Ocupação da grade acima do limite", AbovePercent(item.Value, item.Threshold), DisplayFormat.Percent(item.Threshold)),
            "goal-off" => ("Meta fora: " + GoalMetric.Title(item.Metric ?? ""), item.Metric is { } metric ? GoalMetric.Format(metric, item.Value) : "—", item.Metric is { } m ? GoalMetric.Bound(m) + " " + GoalMetric.Format(m, item.Threshold) : "—"),
            _ => ("Alerta", DisplayFormat.Count((long)Math.Round(item.Value)), DisplayFormat.Count((long)Math.Round(item.Threshold)))
        };
        var specialty = string.IsNullOrWhiteSpace(item.Specialty) ? "" : item.Specialty.Trim();
        var since = item.Since is { } at ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", DisplayFormat.Culture) : "—";
        var description = $"{title}{(specialty.Length > 0 ? " em " + specialty : "")}: {value} (limite {threshold}){(item.Since is null ? "" : ", desde " + since)}.";
        return new(item.UnitName, title, specialty, value, threshold, since, description, item.Kind);
    }
    private static string Count(double value, string singular, string plural) { var n = (long)Math.Round(value); return $"{DisplayFormat.Count(n)} {(n == 1 ? singular : plural)}"; }

    // A API só dispara acima do limite e manda o valor com uma casa; arredondar demais mostraria "30 dias, limite 30 dias" ou "20,0%, limite 20,0%".
    // O valor ganha casas até aparecer acima do limite; se nem assim passar, o texto diz "mais de" em vez de repetir o limite.
    private static string AboveDays(double value, double threshold)
    {
        if (Math.Round(value, 1, MidpointRounding.AwayFromZero) <= threshold) return $"mais de {Count(threshold, "dia", "dias")}";
        var whole = Math.Abs(value - Math.Round(value)) < 0.05;
        return whole ? Count(value, "dia", "dias") : $"{value.ToString("0.0", DisplayFormat.Culture)} dias";
    }
    private static string AbovePercent(double value, double threshold)
    {
        var limit = Math.Round(threshold * 100, 6);
        for (var places = 1; places <= 3; places++)
        {
            var shown = Math.Round(value * 100, places, MidpointRounding.AwayFromZero);
            if (shown > limit) return shown.ToString("F" + places, DisplayFormat.Culture) + "%";
        }
        return "mais de " + DisplayFormat.Percent(threshold);
    }
}

/// <summary>Corpo de POST/PUT da grade semanal (horários "HH:mm", vigência "yyyy-MM-dd" ou nula).</summary>
public sealed record ScheduleDraft(Guid ProfessionalId, int Weekday, string StartTime, string EndTime, int SlotMinutes, Guid RoomId, Guid SpecialtyId, string? ValidFrom, string? ValidUntil);
