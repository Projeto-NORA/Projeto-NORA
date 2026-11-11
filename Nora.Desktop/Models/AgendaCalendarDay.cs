using Nora.Desktop.Generics;

namespace Nora.Desktop.Models;

/// <summary>Representa somente a seleção local do calendário; não cria consultas ou disponibilidade fictícias.</summary>
public sealed class AgendaCalendarDay(DateTime date, bool isCurrentMonth) : ObservableObject
{
    private bool isSelected;
    public DateTime Date { get; } = date.Date;
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsOutsideMonth => !IsCurrentMonth;
    public bool IsToday => Date == DateTime.Today;
    public int DayNumber => Date.Day;
    public bool IsSelected { get => isSelected; set { if (isSelected == value) return; isSelected = value; Notify(); } }
}
