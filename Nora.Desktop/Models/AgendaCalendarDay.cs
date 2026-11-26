using Nora.Desktop.Generics;

namespace Nora.Desktop.Models;

/// <summary>Dia do calendário (mês, semana e mini-calendários do ano): seleção local e quantidade de consultas reais lidas da API; nada é inventado.</summary>
public sealed class AgendaCalendarDay(DateTime date, bool isCurrentMonth) : ObservableObject
{
    private static readonly string[] Weekdays = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];
    private bool isSelected;
    private int appointmentCount;
    private HashSet<string> statuses = new(StringComparer.Ordinal);
    public DateTime Date { get; } = date.Date;
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsOutsideMonth => !IsCurrentMonth;
    public bool IsToday => Date == DateTime.Today;
    public int DayNumber => Date.Day;
    /// <summary>"Seg", "Ter"… (cabeçalho da semana).</summary>
    public string WeekdayLabel => Weekdays[((int)Date.DayOfWeek + 6) % 7];
    public bool IsSelected { get => isSelected; set { if (isSelected == value) return; isSelected = value; Notify(); } }
    public int AppointmentCount
    {
        get => appointmentCount;
        set { if (appointmentCount == value) return; appointmentCount = value; Notify(); Notify(nameof(HasAppointments)); Notify(nameof(Intensity)); Notify(nameof(CountLabel)); Notify(nameof(AutomationLabel)); }
    }
    public bool HasAppointments => appointmentCount > 0;
    /// <summary>Quanto o dia está cheio, para o mini-calendário do ano: 0 sem consulta, 1 até 2 (ponto), 2 até 5 (ponto e fundo claro) e 3 com 6 ou mais (fundo forte).</summary>
    public int Intensity => appointmentCount switch { 0 => 0, <= 2 => 1, <= 5 => 2, _ => 3 };
    public bool HasScheduled => statuses.Contains("scheduled");
    public bool HasCalled => statuses.Contains("called");
    public bool HasCompleted => statuses.Contains("completed");
    public bool HasMissed => statuses.Contains("missed");
    public bool HasCancelled => statuses.Contains("cancelled");
    public void SetStatuses(IEnumerable<string> values)
    {
        var next = values.ToHashSet(StringComparer.Ordinal);
        if (statuses.SetEquals(next)) return;
        statuses = next;
        foreach (var name in new[] { nameof(HasScheduled), nameof(HasCalled), nameof(HasCompleted), nameof(HasMissed), nameof(HasCancelled), nameof(AutomationLabel) }) Notify(name);
    }
    public string CountLabel => appointmentCount == 1 ? "1 consulta" : $"{appointmentCount} consultas";
    public string AutomationLabel => $"{Date:dd 'de' MMMM}{(HasAppointments ? ", " + CountLabel : ", sem consultas")}" +
        (statuses.Count == 0 ? "" : ": " + string.Join(", ", new[] { (HasScheduled, "agendadas"), (HasCalled, "chamadas"), (HasCompleted, "concluídas"), (HasMissed, "faltas"), (HasCancelled, "canceladas") }.Where(item => item.Item1).Select(item => item.Item2)));
}

/// <summary>Um dos 12 meses da visão Ano: nome, os 42 dias do mini-calendário (os de fora do mês ficam em branco) e a quantidade de consultas do mês.</summary>
public sealed class AgendaYearMonth : ObservableObject
{
    private int appointmentCount;

    public AgendaYearMonth(int year, int month)
    {
        FirstDay = new DateTime(year, month, 1);
        var start = FirstDay.AddDays(-(((int)FirstDay.DayOfWeek + 6) % 7));
        Days = [.. Enumerable.Range(0, 42).Select(offset => new AgendaCalendarDay(start.AddDays(offset), start.AddDays(offset).Month == month))];
    }

    public DateTime FirstDay { get; }
    public string Name => FirstDay.ToString("MMMM") is { Length: > 0 } name ? char.ToUpper(name[0]) + name[1..] : "";
    public IReadOnlyList<AgendaCalendarDay> Days { get; }
    public bool IsCurrent => FirstDay.Year == DateTime.Today.Year && FirstDay.Month == DateTime.Today.Month;
    public int AppointmentCount
    {
        get => appointmentCount;
        set { if (appointmentCount == value) return; appointmentCount = value; Notify(); Notify(nameof(AutomationLabel)); }
    }
    public string AutomationLabel => $"{Name} de {FirstDay.Year}, {(appointmentCount == 0 ? "sem consultas" : appointmentCount == 1 ? "1 consulta" : appointmentCount + " consultas")}";
}
