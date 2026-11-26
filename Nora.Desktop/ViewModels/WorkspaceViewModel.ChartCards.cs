using System.Collections.ObjectModel;
using System.Globalization;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Painéis da grade de gráficos (modo gráficos do Painel da UBS e dos Indicadores): um painel por indicador que tem dado, montado só com o que a API já devolveu.
/// Cada construtor devolve <c>null</c> quando não há o que desenhar, e o painel então não aparece na grade. Períodos longos usam o agrupamento por semana ou mês de <see cref="ChartData"/>.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    /// <summary>Acima deste número de dias o eixo não é preenchido com zeros desde o começo da janela: começa no primeiro dia com consulta.</summary>
    private const int DenseAxisLimit = 400;

    /// <summary>Eixo contínuo do gráfico: dias sem registro valem zero. Em janelas longas começa no primeiro dia com consulta, para a linha não perder anos vazios.</summary>
    private static DailyPoint[] BuildAxis(IReadOnlyList<DailyPoint> sparse, DateOnly first, DateOnly last)
    {
        if (last.DayNumber - first.DayNumber + 1 > DenseAxisLimit)
        {
            if (sparse.Count == 0) return [];
            if (sparse[0].Date > first) first = sparse[0].Date;
        }
        if (sparse.Count > 0) { if (sparse[0].Date < first) first = sparse[0].Date; if (sparse[^1].Date > last) last = sparse[^1].Date; }
        var length = last.DayNumber - first.DayNumber + 1;
        if (length <= 0) return [.. sparse];
        var byDate = sparse.ToDictionary(point => point.Date);
        var result = new DailyPoint[length];
        for (var index = 0; index < length; index++) { var date = first.AddDays(index); result[index] = byDate.TryGetValue(date, out var point) ? point : new DailyPoint(date, 0, 0, 0); }
        return result;
    }

    /// <summary>Uma linha (dia, semana ou mês) escrita por extenso: "03/11/2026", "02/11 a 08/11/2026", "Novembro de 2026".</summary>
    private static string BucketPeriod(ChartBucket bucket)
    {
        switch (bucket.Grouping)
        {
            case ChartGrouping.Day: return DisplayFormat.Date(bucket.Start);
            case ChartGrouping.Week: return $"{bucket.Start.ToString("dd/MM", DisplayFormat.Culture)} a {DisplayFormat.Date(bucket.Start.AddDays(6))}";
            default:
                var name = bucket.Start.ToString("MMMM", DisplayFormat.Culture);
                return $"{char.ToUpper(name[0], DisplayFormat.Culture)}{name[1..]} de {bucket.Start.Year}";
        }
    }

    private static IReadOnlyList<ChartPoint> PieSlices(params (string Label, double Value, string? Brush)[] items) =>
        items.All(item => item.Value <= 0) ? [] : [.. items.Select(item => new ChartPoint(item.Label, item.Value, item.Brush))];

    /// <summary>Consultas por situação ao longo do tempo (três linhas) e a distribuição das encerradas (pizza).</summary>
    private static UnitChartCard AppointmentsChartCard(IReadOnlyList<DailyPoint> days, long completed, long missed, long cancelled, string lineNote, string pieNote, string emptyNote)
    {
        var lines = ChartData.Evolution(days);
        var hasLine = lines.Any(item => item.Points.Any(point => point.Value != 0));
        var step = days.Count == 0 ? "" : ChartData.StepNote(ChartData.For(days.Count));
        var slices = PieSlices(("Concluídas", completed, "StatusCompletedBrush"), ("Faltas", missed, "StatusMissedBrush"), ("Canceladas", cancelled, "StatusCancelledBrush"));
        // Sem nenhuma consulta na linha, a explicação aparece no lugar do gráfico (quando a pizza também está vazia).
        return new("Consultas por situação", hasLine ? lines : [], slices, hasLine ? string.Join(' ', new[] { lineNote, step }.Where(text => text.Length > 0)) : emptyNote, pieNote);
    }

    /// <summary>Comparecimento (concluídas ÷ concluídas + faltas) em cada ponto do eixo; sem consulta encerrada o painel não existe.</summary>
    private static UnitChartCard? AttendanceChartCard(IReadOnlyList<DailyPoint> days)
    {
        if (days.Count == 0) return null;
        var points = ChartData.Group(days).Where(bucket => bucket.Completed + bucket.Missed > 0)
            .Select(bucket => { var value = Math.Round(100d * bucket.Completed / (bucket.Completed + bucket.Missed), 1); return new ChartPoint(bucket.Label, value, null, $"{BucketPeriod(bucket)}: {value.ToString("0.#", DisplayFormat.Culture)}% de comparecimento"); }).ToArray();
        if (points.Length == 0) return null;
        var step = ChartData.StepNote(ChartData.For(days.Count));
        return new("Comparecimento", [new ChartLine("Comparecimento (%)", "ChartBrush6", points)], [], ("Concluídas em relação às concluídas e faltas." + (step.Length > 0 ? " " + step : "")).Trim());
    }

    private static UnitChartCard? LightningChartCard(LightningMetrics? lightning, string span)
    {
        if (lightning is null) return null;
        var slices = PieSlices(("Aceitas", lightning.AcceptedOffers, "StatusCompletedBrush"), ("Recusadas", lightning.DeclinedOffers, "StatusMissedBrush"), ("Vencidas", lightning.ExpiredOffers, "StatusCancelledBrush"));
        return slices.Count == 0 ? null : new("Vaga Relâmpago", [], slices, "", $"Respostas às ofertas {span}. Aceitar a oferta não confirma o atendimento.");
    }

    private static UnitChartCard? WaitChartCard(IReadOnlyList<SpecialtyMetrics> specialties)
    {
        var ordered = specialties.OrderBy(item => item.Specialty, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var waits = ordered.Where(item => item.AverageWaitMinutes is not null).Select(item => new ChartPoint(item.Specialty, Math.Round(item.AverageWaitMinutes!.Value), null, $"{item.Specialty}: {DisplayFormat.Wait(item.AverageWaitMinutes)}")).ToArray();
        var queue = ordered.Where(item => item.ActiveEntries > 0).Select(item => new ChartPoint(item.Specialty, item.ActiveEntries)).ToArray();
        // Uma especialidade só não forma linha: nesse caso só a pizza da fila existe.
        if (waits.Length < 2 && queue.Length == 0) return null;
        return new("Espera e fila por especialidade", waits.Length < 2 ? [] : [new ChartLine("Espera média (min)", "ChartBrush3", waits)], queue,
            "A API não traz série histórica da espera: a linha mostra a espera média, em minutos, de cada especialidade.", "Pessoas aguardando agora, por especialidade.");
    }

    private static UnitChartCard? OccupancyChartCard(CapacityMetrics? capacity, string note)
    {
        if (capacity is null || capacity.OfferedSlots <= 0) return null;
        var slices = PieSlices(("Ocupados", capacity.BookedSlots, "StatusScheduledBrush"), ("Livres", Math.Max(0, capacity.OfferedSlots - capacity.BookedSlots), "ChartBrush6"));
        return slices.Count == 0 ? null : new("Ocupação da agenda", [], slices, "", note);
    }

    private static UnitChartCard? SpecialtyTotalsChartCard(IReadOnlyList<SpecialtyMetrics> specialties, string span)
    {
        var slices = specialties.OrderBy(item => item.Specialty, StringComparer.CurrentCultureIgnoreCase).Where(item => item.Completed + item.Missed + item.Cancelled > 0)
            .Select(item => new ChartPoint(item.Specialty, item.Completed + item.Missed + item.Cancelled)).ToArray();
        return slices.Length == 0 ? null : new("Consultas por especialidade", [], slices, "", $"Consultas encerradas {span}, por especialidade.");
    }

    /// <summary>Troca a grade de painéis mexendo só no que mudou, e devolve a cada painel novo o desenho (Linha ou Pizza) que a pessoa tinha escolhido para o título.</summary>
    private static void ReplaceChartCards(ObservableCollection<UnitChartCard> target, IReadOnlyList<UnitChartCard> fresh, Dictionary<string, ChartKind> kinds)
    {
        foreach (var card in target) kinds[card.Title] = card.Kind;
        foreach (var card in fresh) if (kinds.TryGetValue(card.Title, out var kind)) card.Kind = kind;
        Replace(target, fresh);
    }

    /// <summary>Comparação da pesquisa: sem diferenciar maiúsculas nem acentos. Pesquisa vazia aceita tudo.</summary>
    private static bool SearchMatches(string query, string text) =>
        query.Length == 0 || CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
