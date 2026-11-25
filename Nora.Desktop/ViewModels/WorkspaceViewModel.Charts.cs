using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

/// <summary>Gráfico (Linha / Pizza) do mini botão dos cartões de indicador. Usa só dados já lidos da API: série diária quando existe; senão, a divisão por especialidade.</summary>
public sealed partial class WorkspaceViewModel
{
    public IndicatorChart BuildIndicatorChart(string title, string value)
    {
        AppointmentMetrics? appointments; WaitingMetrics? waiting; LightningMetrics? lightning; DailyMetrics[] daily; SpecialtyMetrics[] specialties; CapacityMetrics? capacity;
        if (IsMunicipal && metrics is { } network) { appointments = network.Totals.Appointments; waiting = network.Totals.Waiting; lightning = network.Totals.Lightning; daily = network.Daily; specialties = network.BySpecialty; capacity = network.Capacity; }
        else if ((Section == "home" ? panelMetrics : unitMetrics) is { } unit) { appointments = unit.Appointments; waiting = unit.Waiting; lightning = unit.Lightning; daily = unit.Daily; specialties = unit.BySpecialty; capacity = unit.Capacity; }
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
            line = daily.OrderBy(d => d.Date, StringComparer.Ordinal).Select(d => new ChartPoint(ShortDate(d.Date), measure(d))).ToArray();
            lineNote = "Evolução diária no período do filtro.";
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
        { pie = [new("Aceitas", lightning.AcceptedOffers), new("Recusadas", lightning.DeclinedOffers), new("Vencidas", lightning.ExpiredOffers)]; pieNote = "Respostas às ofertas de Vaga Relâmpago."; }
        else if ((key.Contains("capacidade") || key.Contains("ocupa") || key.Contains("horário")) && capacity is not null)
        { pie = [new("Ocupados", capacity.BookedSlots), new("Livres", Math.Max(0, capacity.OfferedSlots - capacity.BookedSlots))]; pieNote = "Horários da grade no período."; }
        else if ((key.Contains("espera") || key.Contains("fila") || key.Contains("aguard") || key.Contains("solicita")) && specialties.Length > 0)
        { pie = specialties.Where(s => s.ActiveEntries > 0).OrderBy(s => s.Specialty, StringComparer.CurrentCultureIgnoreCase).Select(s => new ChartPoint(s.Specialty, s.ActiveEntries)).ToArray(); pieNote = "Pessoas aguardando, por especialidade."; }
        else if ((key.Contains("paciente") || key.Contains("cadastr") || key.Contains("desativ")) && IsMunicipal && summary is not null)
        { pie = [new("Ativos", summary.Patients), new("Desativados", summary.InactivePatients ?? 0)]; pieNote = "Cadastros da rede."; }
        else if ((key.Contains("paciente") || key.Contains("cadastr")) && !IsMunicipal && ObsRegistry.Count > 0)
        { pie = [new("Ativos", ObsRegistry.Count(p => p.IsActive)), new("Desativados", ObsRegistry.Count(p => !p.IsActive))]; pieNote = "Cadastros desta UBS."; }
        else if (appointments is not null)
        { pie = [new("Concluídas", appointments.Completed), new("Faltas", appointments.Missed), new("Canceladas", appointments.Cancelled)]; pieNote = "Composição das consultas encerradas no período."; }
        else { pie = []; pieNote = "Sem dados para a composição."; }
        if (pie.All(p => p.Value <= 0)) pie = [];
        return new IndicatorChart(title, value, line, lineNote, pie, pie.Count == 0 ? "Sem dados para a composição." : pieNote);
    }

    private static string ShortDate(string iso) => DateOnly.TryParse(iso, out var date) ? date.ToString("dd/MM", Services.DisplayFormat.Culture) : iso;
}
