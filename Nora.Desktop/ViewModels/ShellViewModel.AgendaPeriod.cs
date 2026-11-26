namespace Nora.Desktop.ViewModels;

public sealed partial class ShellViewModel
{
    private bool agendaPeriodRestored;

    /// <summary>A visão da Agenda (Dia, Semana, Mês ou Ano) que a pessoa usou da última vez neste programa; sem escolha, ou com um valor desconhecido, Mês.</summary>
    public string GetAgendaPeriod() => Workspace.AgendaPeriods.Contains(preferences.AgendaPeriod) ? preferences.AgendaPeriod : "Mês";

    /// <summary>
    /// Na primeira vez que a Agenda abre depois de o programa iniciar, assume a visão lembrada. Depois disso quem manda é a escolha da pessoa nesta sessão (a tela é recriada a
    /// cada abertura e não pode desfazê-la).
    /// </summary>
    public void RestoreAgendaPeriodOnce()
    {
        if (agendaPeriodRestored) return;
        agendaPeriodRestored = true;
        Workspace.AgendaViewMode = GetAgendaPeriod();
    }

    /// <summary>Lembra a visão da Agenda nas preferências.</summary>
    public void SetAgendaPeriod(string period)
    {
        if (!Workspace.AgendaPeriods.Contains(period) || preferences.AgendaPeriod == period) return;
        preferences.AgendaPeriod = period;
        Persist();
    }
}
