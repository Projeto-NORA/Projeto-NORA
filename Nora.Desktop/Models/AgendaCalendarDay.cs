using Nora.Desktop.Generics;

namespace Nora.Desktop.Models;

/// <summary>Dia do calendário: seleção local e quantidade de consultas reais lidas da API; nada é inventado.</summary>
public sealed class AgendaCalendarDay(DateTime date, bool isCurrentMonth) : ObservableObject
{
    private bool isSelected;
    private int appointmentCount;
    private HashSet<string> statuses = new(StringComparer.Ordinal);
    public DateTime Date { get; } = date.Date;
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsOutsideMonth => !IsCurrentMonth;
    public bool IsToday => Date == DateTime.Today;
    public int DayNumber => Date.Day;
    public bool IsSelected { get => isSelected; set { if (isSelected == value) return; isSelected = value; Notify(); } }
    public int AppointmentCount
    {
        get => appointmentCount;
        set { if (appointmentCount == value) return; appointmentCount = value; Notify(); Notify(nameof(HasAppointments)); Notify(nameof(CountLabel)); Notify(nameof(AutomationLabel)); }
    }
    public bool HasAppointments => appointmentCount > 0;
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
