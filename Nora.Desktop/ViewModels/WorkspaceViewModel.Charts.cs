using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

/// <summary>Gráfico (Linha / Pizza) do mini botão dos cartões de indicador. Usa só dados já lidos da API: série diária quando existe; senão, a divisão por especialidade.</summary>
public sealed partial class WorkspaceViewModel
{
    public IndicatorChart BuildIndicatorChart(string title)
    {
        AppointmentMetrics? appointments; WaitingMetrics? waiting; LightningMetrics? lightning; DailyMetrics[] daily; SpecialtyMetrics[] specialties; CapacityMetrics? capacity;
        if (IsMunicipal && metrics is { } network) { appointments = network.Totals.Appointments; waiting = network.Totals.Waiting; lightning = network.Totals.Lightning; daily = network.Daily; specialties = network.BySpecialty; capacity = network.Capacity; }
        else if ((Section == "home" ? PanelData ?? panelMetrics : unitMetrics) is { } unit) { appointments = unit.Appointments; waiting = unit.Waiting; lightning = unit.Lightning; daily = unit.Daily; specialties = unit.BySpecialty; capacity = unit.Capacity; }
        else { appointments = null; waiting = null; lightning = null; daily = []; specialties = []; capacity = null; }

        var key = title.ToLowerInvariant();
        Func<DailyMetrics, double>? measure = key switch
        {
            _ when key.Contains("falta") => d => d.Missed,
            _ when key.Contains("cancel") => d => d.Cancelled,
            _ when key.Contains("conclu") => d => d.Completed,
            _ when key.Contains("compareciment") || key.Contains("score") => d => d.Completed + d.Missed == 0 ? 0 : Math.Round(100d * d.Completed / (d.Completed + d.Missed), 1),
            _ when key.Contains("consulta") || key.Contains("agend") => d => d.Scheduled,
            _ => null
        };
        IReadOnlyList<ChartPoint> line; string lineNote;
        if (measure is not null && daily.Length > 0)
        {
            line = DailyLine(daily, measure, out var grouping);
            lineNote = grouping == ChartGrouping.Day ? "Evolução diária no período do filtro." : $"Evolução no período do filtro. {ChartData.StepNote(grouping)}";
        }
        else
        {
            Func<SpecialtyMetrics, double> bySpecialty = key switch
            {
                _ when key.Contains("espera") => s => Math.Round(s.AverageWaitMinutes ?? 0),
                _ when key.Contains("fila") || key.Contains("aguard") || key.Contains("solicita") => s => s.ActiveEntries,
                _ when key.Contains("falta") => s => s.Missed,
                _ when key.Contains("cancel") => s => s.Cancelled,
                _ => s => s.Completed + s.Missed + s.Cancelled
            };
            line = specialties.OrderBy(s => s.Specialty, StringComparer.CurrentCultureIgnoreCase).Select(s => new ChartPoint(s.Specialty, bySpecialty(s))).ToArray();
            lineNote = line.Count > 0 ? "A API não traz série histórica deste número: a linha mostra o valor por especialidade no período." : "Sem dados de evolução para este indicador.";
        }

        IReadOnlyList<ChartPoint> pie; string pieNote;
        if ((key.Contains("oferta") || key.Contains("vaga") || key.Contains("aceit") || key.Contains("recus") || key.Contains("expir")) && lightning is not null)
        { pie = [new("Aceitas", lightning.AcceptedOffers, "StatusCompletedBrush"), new("Recusadas", lightning.DeclinedOffers, "StatusMissedBrush"), new("Vencidas", lightning.ExpiredOffers, "StatusCancelledBrush")]; pieNote = "Respostas às ofertas de Vaga Relâmpago."; }
        else if ((key.Contains("capacidade") || key.Contains("ocupa") || key.Contains("horário")) && capacity is not null)
        { pie = [new("Ocupados", capacity.BookedSlots, "StatusScheduledBrush"), new("Livres", Math.Max(0, capacity.OfferedSlots - capacity.BookedSlots), "ChartBrush6")]; pieNote = "Horários da grade no período."; }
        else if ((key.Contains("espera") || key.Contains("fila") || key.Contains("aguard") || key.Contains("solicita")) && specialties.Length > 0)
        { pie = specialties.Where(s => s.ActiveEntries > 0).OrderBy(s => s.Specialty, StringComparer.CurrentCultureIgnoreCase).Select(s => new ChartPoint(s.Specialty, s.ActiveEntries)).ToArray(); pieNote = "Pessoas aguardando, por especialidade."; }
        else if ((key.Contains("paciente") || key.Contains("cadastr") || key.Contains("desativ")) && IsMunicipal && summary is not null)
        { pie = [new("Ativos", summary.Patients, "StatusCompletedBrush"), new("Desativados", summary.InactivePatients ?? 0, "ChartBrush6")]; pieNote = "Cadastros da rede."; }
        else if ((key.Contains("paciente") || key.Contains("cadastr")) && !IsMunicipal && ObsRegistry.Count > 0)
        { pie = [new("Ativos", ObsRegistry.Count(p => p.IsActive), "StatusCompletedBrush"), new("Desativados", ObsRegistry.Count(p => !p.IsActive), "ChartBrush6")]; pieNote = "Cadastros desta UBS."; }
        else if (appointments is not null)
        { pie = [new("Concluídas", appointments.Completed, "StatusCompletedBrush"), new("Faltas", appointments.Missed, "StatusMissedBrush"), new("Canceladas", appointments.Cancelled, "StatusCancelledBrush")]; pieNote = "Composição das consultas encerradas no período."; }
        else { pie = []; pieNote = "Sem dados para a composição."; }
        if (pie.All(p => p.Value <= 0)) pie = [];
        return new IndicatorChart(title, line, lineNote, pie, pie.Count == 0 ? "Sem dados para a composição." : pieNote);
    }

    /// <summary>
    /// A linha do indicador no tempo. Um ponto por dia até 92 dias; acima disso os dias são somados por semana ou por mês (<see cref="ChartData.For"/>) e a medida é calculada
    /// sobre a soma, para a razão de comparecimento continuar certa.
    /// </summary>
    private static ChartPoint[] DailyLine(DailyMetrics[] daily, Func<DailyMetrics, double> measure, out ChartGrouping grouping)
    {
        var ordered = daily.OrderBy(day => day.Date, StringComparer.Ordinal).ToArray();
        var dates = ordered.Select(day => DateOnly.TryParse(day.Date, out var date) ? date : (DateOnly?)null).ToArray();
        var known = dates.OfType<DateOnly>().ToArray();
        grouping = known.Length < 2 ? ChartGrouping.Day : ChartData.For(known[^1].DayNumber - known[0].DayNumber + 1);
        if (grouping == ChartGrouping.Day) return ordered.Select(day => new ChartPoint(ShortDate(day.Date), measure(day))).ToArray();
        var mode = grouping;
        return ordered.Select((day, index) => (day, date: dates[index])).Where(item => item.date is not null)
            .GroupBy(item => mode == ChartGrouping.Week ? item.date!.Value.AddDays(-(((int)item.date.Value.DayOfWeek + 6) % 7)) : new DateOnly(item.date!.Value.Year, item.date.Value.Month, 1))
            .Select(group =>
            {
                var sum = new DailyMetrics(group.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), group.Sum(item => item.day.Scheduled), group.Sum(item => item.day.Completed), group.Sum(item => item.day.Missed), group.Sum(item => item.day.Cancelled));
                var label = mode == ChartGrouping.Month ? group.Key.ToString("MMM/yy", Services.DisplayFormat.Culture) : group.Key.ToString("dd/MM", Services.DisplayFormat.Culture);
                return new ChartPoint(label, measure(sum));
            }).ToArray();
    }

    private static string ShortDate(string iso) => DateOnly.TryParse(iso, out var date) ? date.ToString("dd/MM", Services.DisplayFormat.Culture) : iso;
}
