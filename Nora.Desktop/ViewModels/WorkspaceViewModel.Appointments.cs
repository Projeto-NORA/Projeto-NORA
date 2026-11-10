using System.Collections.ObjectModel;
using System.Globalization;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private UnitPatientRecord? appointmentPatient;
    private AppointmentInfo? selectedAppointment;
    private string appointmentSpecialty = "", appointmentProfessional = "", appointmentStart = "09:00", appointmentEnd = "09:30";
    private IReadOnlyList<AppointmentInfo>? todayOverview;
    private DateTime todayOverviewDate;
    private string TodayCount(params string[] statuses) => HasActiveObsUnit && todayOverview is not null && todayOverviewDate == DateTime.Today
        ? todayOverview.Count(item => statuses.Contains(item.Status, StringComparer.Ordinal)).ToString() : "—";
    public string TodayCompletedCount => TodayCount("completed");
    public string TodayScheduledCount => TodayCount("scheduled", "called");
    public string TodayMissedCount => TodayCount("missed");
    public string TodayCancelledCount => TodayCount("cancelled");
    private void SetTodayOverview(DateTime date, IReadOnlyList<AppointmentInfo>? records)
    {
        todayOverviewDate = date; todayOverview = records;
        Notify(nameof(TodayCompletedCount)); Notify(nameof(TodayScheduledCount)); Notify(nameof(TodayMissedCount)); Notify(nameof(TodayCancelledCount));
    }
    public ObservableCollection<AppointmentInfo> Appointments { get; } = [];
    public UnitPatientRecord? AppointmentPatient { get => appointmentPatient; set { appointmentPatient = value; Notify(); } }
    public AppointmentInfo? SelectedAppointment { get => selectedAppointment; set { selectedAppointment = value; Notify(); } }
    public string AppointmentSpecialty { get => appointmentSpecialty; set { appointmentSpecialty = value; Notify(); } }
    public string AppointmentProfessional { get => appointmentProfessional; set { appointmentProfessional = value; Notify(); } }
    public string AppointmentStart { get => appointmentStart; set { appointmentStart = value; Notify(); } }
    public string AppointmentEnd { get => appointmentEnd; set { appointmentEnd = value; Notify(); } }

    private (DateTimeOffset From, DateTimeOffset To) AgendaRange()
    {
        var first = IsAgendaWeek ? StartOfWeek(selectedAgendaDate) : selectedAgendaDate.Date;
        var last = first.AddDays(IsAgendaWeek ? 7 : 1);
        return (new DateTimeOffset(first), new DateTimeOffset(last));
    }
    public Task LoadAppointmentsAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS em Pacientes antes de consultar a agenda.");
        var (from, to) = AgendaRange(); var records = await api.AppointmentsAsync(unit.Id, from, to);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id || AgendaRange() != (from, to)) return;
        SelectedAppointment = null; Replace(Appointments, records); Message = "Agenda atualizada.";
    });
    public Task CreateAppointmentAsync() => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS em Pacientes antes de agendar.");
        var patient = AppointmentPatient ?? throw new NoraApiException("Selecione o paciente cadastrado na UBS.");
        if (!TimeSpan.TryParseExact(AppointmentStart.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var start) ||
            !TimeSpan.TryParseExact(AppointmentEnd.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var end) || end <= start ||
            string.IsNullOrWhiteSpace(AppointmentSpecialty) || string.IsNullOrWhiteSpace(AppointmentProfessional))
            throw new NoraApiException("Informe especialidade, profissional e horários válidos no formato HH:mm.");
        await api.CreateAppointmentAsync(unit.Id, patient.Cpf, AppointmentSpecialty.Trim(), AppointmentProfessional.Trim(), new DateTimeOffset(selectedAgendaDate.Date.Add(start)), new DateTimeOffset(selectedAgendaDate.Date.Add(end)));
        SetTodayOverview(default, null);
        AppointmentPatient = null;
        try
        {
            var (from, to) = AgendaRange(); var records = await api.AppointmentsAsync(unit.Id, from, to);
            if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id || AgendaRange() != (from, to)) return;
            Replace(Appointments, records); Message = "Consulta agendada.";
        }
        catch (NoraApiException error) when (!error.Unauthorized) { Message = "Consulta agendada; atualize a agenda para vê-la."; }
    });
    public Task ChangeAppointmentStatusAsync(string status, bool confirmed) => RunAsync(async () =>
    {
        var unit = ActiveObsUnit ?? throw new NoraApiException("Confirme uma UBS antes de alterar a consulta.");
        var appointment = SelectedAppointment ?? throw new NoraApiException("Selecione uma consulta.");
        var updated = await api.ChangeAppointmentStatusAsync(unit.Id, appointment.Id, status, confirmed);
        SetTodayOverview(default, null);
        if (disposed || !IsAuthenticated || ActiveObsUnit?.Id != unit.Id) return;
        var index = Appointments.IndexOf(appointment); if (index >= 0) Appointments[index] = updated;
        SelectedAppointment = updated; Message = "Situação da consulta atualizada.";
    });
    public void MoveAgenda(int direction)
    {
        selectedAgendaDate = selectedAgendaDate.AddDays(direction * (IsAgendaWeek ? 7 : 1));
        BuildAgendaDays(); ClearAgendaList(); Notify(nameof(AgendaRangeLabel)); Notify(nameof(SelectedAgendaDateLabel));
    }
    private void ClearAgendaList() { Appointments.Clear(); SelectedAppointment = null; }
    private void ClearAppointmentData() { ClearAgendaList(); SetTodayOverview(default, null); AppointmentPatient = null; AppointmentSpecialty = ""; AppointmentProfessional = ""; }
}
