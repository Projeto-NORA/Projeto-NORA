using Nora.Desktop.Generics;

namespace Nora.Desktop.Models;

/// <summary>Dia do calendário: seleção local e quantidade de consultas reais lidas da API; nada é inventado.</summary>
public sealed class AgendaCalendarDay(DateTime date, bool isCurrentMonth) : ObservableObject
{
    private bool isSelected;
    private int appointmentCount;
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
    public string CountLabel => appointmentCount == 1 ? "1 consulta" : $"{appointmentCount} consultas";
    public string AutomationLabel => $"{Date:dd 'de' MMMM}{(HasAppointments ? ", " + CountLabel : ", sem consultas")}";
}
