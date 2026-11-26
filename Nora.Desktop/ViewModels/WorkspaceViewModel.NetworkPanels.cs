using System.Collections.ObjectModel;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Um gráfico da grade de gráficos das telas de números da rede: o título, a linha (uma ou mais) e a pizza, cada uma com a sua nota de origem.
/// A igualdade compara o conteúdo, para a releitura que não mudou nada não recriar o gráfico (e não desfazer o Linha | Pizza escolhido).
/// </summary>
public sealed record NetworkChartItem(string Title, IReadOnlyList<ChartLine> Lines, IReadOnlyList<ChartPoint> Slices, string LineNote, string PieNote)
{
    public bool Equals(NetworkChartItem? other) => other is not null && Title == other.Title && LineNote == other.LineNote && PieNote == other.PieNote
        && Slices.SequenceEqual(other.Slices)
        && Lines.Count == other.Lines.Count && Lines.Zip(other.Lines).All(pair => pair.First.Name == pair.Second.Name && pair.First.BrushKey == pair.Second.BrushKey && pair.First.Points.SequenceEqual(pair.Second.Points));
    public override int GetHashCode() => HashCode.Combine(Title, LineNote, PieNote, Slices.Count, Lines.Count);
}

/// <summary>Uma linha da lista de alertas da rede: o alerta avaliado pela API pronto para a listagem aberta (título, linha de dados, etiqueta e ficha).</summary>
public sealed record NetworkAlertEntry(string Key, string UnitName, string Title, string Specialty, string Value, string Threshold, string Since, string Description, string Category, Tone Tone)
{
    /// <summary>O que vai no alto do item: o tipo do alerta e, quando há, a especialidade.</summary>
    public string Name => Specialty.Length > 0 ? $"{Title} | {Specialty}" : Title;
    /// <summary>A linha de dados: UBS, valor, limite e, só na espera longa, desde quando; sem o que não foi informado.</summary>
    public string MetaLine => string.Join(" | ", new[] { UnitName, Value, $"limite {Threshold}", Since == "—" ? "" : $"desde {Since}" }.Where(part => part.Length > 0));
    public string AutomationLabel => $"{Name}. {UnitName}: {Value}, limite {Threshold}.";

    public static NetworkAlertEntry From(AlertItem item)
    {
        var row = AlertRow.From(item);
        var (category, tone) = item.Kind switch
        {
            "high-queue" => ("Fila", Tone.Violet),
            "long-wait" => ("Espera", Tone.Yellow),
            "missed-rate" => ("Faltas", Tone.Pink),
            "occupancy" => ("Ocupação", Tone.Blue),
            "goal-off" => ("Meta", Tone.Pink),
            _ => ("Alerta", Tone.Neutral)
        };
        var key = $"{item.UnitId}|{item.Kind}|{(item.Specialty ?? "").Trim().ToUpperInvariant()}|{item.Metric}";
        return new(key, row.UnitName, row.Title, row.Specialty, row.Value, row.Threshold, row.Since, row.Description, category, tone);
    }
}

/// <summary>
/// Telas de números da rede da Secretaria (Visão geral, Indicadores): a pesquisa em linha do cabeçalho, a grade de gráficos do modo gráficos e
/// os cartões da Vaga Relâmpago da rede.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private string networkSearch = "";

    /// <summary>Pesquisa em linha do cabeçalho: filtra os cartões, os gráficos e as linhas de tabela da tela (cada tela diz por quais campos).</summary>
    public string NetworkSearch { get => networkSearch; set { value ??= ""; if (networkSearch == value) return; networkSearch = value; Notify(); } }

    // ---- Grade de gráficos ----

    public ObservableCollection<NetworkChartItem> NetworkCharts { get; } = [];
    public string NetworkChartsState => !IsAuthenticated ? "Entre na Secretaria para ver os gráficos."
        : metrics is null ? MetricsStateText
        : NetworkCharts.Count == 0 ? "Nenhum dado para gráfico neste período e nestes filtros." : "";

    /// <summary>
    /// Refaz a grade de gráficos com o que já foi lido (indicadores do período e resumo da rede). Sem dado em um indicador, o gráfico dele não entra.
    /// O eixo do tempo agrupa por dia, semana ou mês conforme o tamanho do período (<see cref="ChartData.For"/>).
    /// </summary>
    private void RebuildNetworkCharts()
    {
        var charts = new List<NetworkChartItem>();
        if (metrics is { } network)
        {
            var days = NetworkChart.Days;
            var span = days.Count == 0 ? 0 : days[^1].Date.DayNumber - days[0].Date.DayNumber + 1;
            var grouping = ChartData.For(Math.Max(span, days.Count));
            var withConsultations = days.Any(day => day.Completed + day.Missed + day.Cancelled > 0);
            var step = ChartData.StepNote(grouping);
            charts.Add(new("Consultas por situação", withConsultations ? ChartData.Evolution(days, grouping) : [], ChartData.Outcomes(days), withConsultations ? step : NetworkChart.State, "Consultas encerradas no período."));
            charts.Add(new("Comparecimento", AttendanceLine(days, grouping), [], "Em cada ponto: concluídas ÷ (concluídas + faltas). O número do cartão pondera também os cancelamentos feitos pelo paciente.", ""));
            charts.Add(new("Consultas por região", [], TopSlices(network.ByRegion.Select(region => (region.Region, (double)(region.Appointments.Completed + region.Appointments.Missed + region.Appointments.Cancelled)))), "", "Consultas encerradas no período, por região."));
            charts.Add(new("Consultas por especialidade", [], TopSlices(network.BySpecialty.Select(item => (item.Specialty, (double)(item.Completed + item.Missed + item.Cancelled)))), "", "Consultas encerradas no período, por especialidade."));
            var waits = network.BySpecialty.Where(item => item.AverageWaitMinutes is not null).OrderBy(item => item.Specialty, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new ChartPoint(item.Specialty, Math.Round(item.AverageWaitMinutes ?? 0), null, $"{item.Specialty}: {DisplayFormat.Wait(item.AverageWaitMinutes)}")).ToArray();
            charts.Add(new("Espera na fila", waits.Length == 0 ? [] : [new ChartLine("Espera média (min)", "ChartBrush5", waits)], TopSlices(network.BySpecialty.Select(item => (item.Specialty, (double)item.ActiveEntries))),
                "Espera média até a oferta aceita, em minutos, por especialidade. A API não traz a série no tempo deste número.", "Pessoas aguardando agora, por especialidade."));
            var lightning = network.Totals.Lightning;
            charts.Add(new("Ofertas de Vaga Relâmpago", [], [new("Aceitas", lightning.AcceptedOffers, "StatusCompletedBrush"), new("Recusadas", lightning.DeclinedOffers, "StatusMissedBrush"), new("Vencidas", lightning.ExpiredOffers, "StatusCancelledBrush")], "", "Respostas às ofertas das vagas do período."));
            if (network.Capacity is { OfferedSlots: > 0 } capacity)
                charts.Add(new("Ocupação da agenda", [], [new("Ocupados", capacity.BookedSlots, "StatusScheduledBrush"), new("Livres", Math.Max(0, capacity.OfferedSlots - capacity.BookedSlots), "ChartBrush6")], "", "Horários da grade com início no período."));
        }
        if (summary is { } totals && IsMunicipal)
            charts.Add(new("Cadastros de pacientes", [], [new("Ativos", totals.Patients, "StatusCompletedBrush"), new("Desativados", totals.InactivePatients ?? 0, "ChartBrush6")], "", "Pessoas da rede (CPF contado uma vez), no escopo da UBS escolhida."));
        // O que não tem dado em nenhum dos dois desenhos fica de fora (o gráfico vazio só ocuparia lugar); só o das consultas fica sempre e, sem dado, explica o motivo.
        Replace(NetworkCharts, charts.Where((chart, index) => (index == 0 && metrics is not null) || chart.Lines.Any(line => line.Points.Count > 0) || chart.Slices.Any(slice => slice.Value > 0)).ToArray());
        Notify(nameof(NetworkChartsState));
    }

    /// <summary>Linha do comparecimento por ponto do eixo; pontos sem consulta concluída nem falta ficam de fora (zero seria queda falsa).</summary>
    private static IReadOnlyList<ChartLine> AttendanceLine(IReadOnlyList<DailyPoint> days, ChartGrouping grouping)
    {
        var points = ChartData.Group(days, grouping).Where(bucket => bucket.Completed + bucket.Missed > 0)
            .Select(bucket =>
            {
                var rate = Math.Round(100d * bucket.Completed / (bucket.Completed + bucket.Missed), 1);
                return new ChartPoint(bucket.Label, rate, null, $"{bucket.Description}: comparecimento de {rate.ToString("0.#", DisplayFormat.Culture)}%");
            }).ToArray();
        return points.Length == 0 ? [] : [new ChartLine("Comparecimento (%)", "ChartBrush1", points)];
    }

    /// <summary>As cinco maiores fatias e, se sobrar, "Outras": a pizza continua legível com muitas regiões ou especialidades.</summary>
    private static IReadOnlyList<ChartPoint> TopSlices(IEnumerable<(string Name, double Value)> source)
    {
        var items = source.Where(item => item.Value > 0).OrderByDescending(item => item.Value).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var slices = items.Take(5).Select(item => new ChartPoint(string.IsNullOrWhiteSpace(item.Name) ? "Sem informação" : item.Name, item.Value)).ToList();
        if (items.Length > 5) slices.Add(new ChartPoint("Outras", items.Skip(5).Sum(item => item.Value), "ChartBrush6"));
        return slices;
    }

    // ---- Vaga Relâmpago da rede ----

    public ObservableCollection<MetricCard> NetworkLightningCards { get; } = [];

    /// <summary>Os oito cartões da Vaga Relâmpago da rede, no período e na UBS escolhidos em Filtros.</summary>
    private MetricCard[] BuildNetworkLightningCards() =>
    [
        new("Vagas abertas", LightningOpen, "Estado atual das vagas no período", Tone.Teal),
        new("Ofertas aceitas", LightningAccepted, "Ofertas das vagas no período", Tone.Green),
        new("Vagas registradas", LightningRegistered, "Vagas criadas no período", Tone.Blue),
        new("Ofertas criadas", LightningCreated, "Não mede envio de notificações", Tone.Teal),
        new("Recusadas", LightningDeclined, "Ofertas das vagas no período", Tone.Pink),
        new("Expiradas", LightningExpired, "Ofertas das vagas no período", Tone.Yellow),
        new("Pendentes", LightningPending, "Estado atual das ofertas", Tone.Violet),
        new("Canceladas", LightningCancelled, "Ofertas das vagas no período", Tone.Neutral)
    ];
}
