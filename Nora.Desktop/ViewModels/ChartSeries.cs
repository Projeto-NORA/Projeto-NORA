using System.Collections.ObjectModel;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Série diária de um gráfico de evolução: os dias do eixo (contínuos, dias sem registro valem zero), as linhas da tabela equivalente
/// (só os dias com registro) e o texto de estado quando não há o que desenhar. Cada tela tem a sua série; o componente do gráfico é um só.
/// </summary>
public sealed class ChartSeries : ObservableObject
{
    private IReadOnlyList<DailyPoint> days = [];
    private string state = "Sem indicadores carregados.";
    public ObservableCollection<DailyPoint> Rows { get; } = [];
    public IReadOnlyList<DailyPoint> Days => days;
    public string State => state;
    public bool HasData => Rows.Count > 0;

    public void Apply(IReadOnlyList<DailyPoint> axis, IReadOnlyList<DailyPoint> registered, string emptyState)
    {
        Rows.Clear();
        foreach (var row in registered) Rows.Add(row);
        days = axis; state = emptyState;
        Notify(nameof(Days)); Notify(nameof(State)); Notify(nameof(HasData));
    }

    public void Clear(string emptyState) => Apply([], [], emptyState);

    /// <summary>Série densa: dias sem registro valem zero (nenhum evento), para o eixo do tempo ser contínuo. O último dia é inclusivo.</summary>
    public static DailyPoint[] Dense(IReadOnlyList<DailyPoint> sparse, DateOnly first, DateOnly last)
    {
        if (sparse.Count > 0) { if (sparse[0].Date < first) first = sparse[0].Date; if (sparse[^1].Date > last) last = sparse[^1].Date; }
        var length = last.DayNumber - first.DayNumber + 1;
        if (length <= 0 || length > 400) return [.. sparse];
        var byDate = sparse.ToDictionary(point => point.Date);
        var result = new DailyPoint[length];
        for (var index = 0; index < length; index++) { var date = first.AddDays(index); result[index] = byDate.TryGetValue(date, out var point) ? point : new DailyPoint(date, 0, 0, 0); }
        return result;
    }
}
