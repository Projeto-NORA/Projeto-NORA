namespace Nora.Desktop.ViewModels;

/// <summary>Um critério que poderia virar alerta: o que mede, de onde vem o dado e o que o usuário ainda precisa decidir (o limite).</summary>
public sealed record AlertCriterion(string Title, string Measures, string DataSource, bool DataReady, string Pending)
{
    public string DataStateLabel => DataReady ? "Dado disponível" : "Dado ainda indisponível";
}

/// <summary>
/// Central de alertas: nenhum alerta é avaliado porque os limites ainda não foram definidos. A lista mostra, por critério, o dado que já existe
/// e a decisão que falta, sem inventar número. Vale para a UBS (OBS) e para a rede (Secretaria).
/// </summary>
public sealed partial class WorkspaceViewModel
{
    private static readonly AlertCriterion[] UnitAlertCriteria =
    [
        new("Fila por especialidade", "Quantas pessoas aguardam em cada especialidade da UBS.", "Aba Fila: posição e espera de cada entrada.", true, "Falta definir a partir de quantas pessoas a fila de uma especialidade vira alerta."),
        new("Tempo de espera", "Espera média e maior espera atual da fila.", "Painel da UBS e Indicadores.", true, "Falta definir a partir de quanto tempo a espera vira alerta."),
        new("Faltas", "Faltas registradas e comparecimento no período.", "Indicadores: faltas e comparecimento por período e por especialidade.", true, "Falta definir o número ou o percentual de faltas que vira alerta."),
        new("Cancelamentos", "Cancelamentos no período, com os feitos pelo paciente.", "Indicadores: cancelamentos totais e feitos pelo paciente.", true, "Falta definir o número ou o percentual de cancelamentos que vira alerta."),
        new("Consultórios e ocupação da agenda", "Uso dos horários e consultórios em atendimento.", "Depende do cadastro de consultórios e horários, previsto para uma próxima etapa.", false, "Decidir os critérios depois que o cadastro existir.")
    ];

    private static readonly AlertCriterion[] NetworkAlertCriteria =
    [
        new("Fila por especialidade", "Quantas pessoas aguardam em cada especialidade, na rede e em cada UBS.", "Especialidades e Indicadores.", true, "Falta definir a partir de quantas pessoas a fila vira alerta."),
        new("Tempo de espera", "Espera média e maior espera atual, por UBS, região e especialidade.", "Unidades, Especialidades e Indicadores.", true, "Falta definir a partir de quanto tempo a espera vira alerta."),
        new("Faltas (absenteísmo)", "Faltas e comparecimento, por UBS, região e especialidade.", "Indicadores, Unidades e Especialidades.", true, "Falta definir o número ou o percentual de faltas que vira alerta."),
        new("Cancelamentos", "Cancelamentos no período, com os feitos pelo paciente.", "Indicadores e Unidades.", true, "Falta definir o número ou o percentual de cancelamentos que vira alerta."),
        new("Sobrecarga das UBS", "Relação entre demanda e capacidade de atendimento de cada UBS.", "Depende do cadastro de profissionais, consultórios e horários, previsto para uma próxima etapa.", false, "Decidir os critérios depois que a capacidade existir.")
    ];

    public IReadOnlyList<AlertCriterion> AlertCriteria => IsMunicipal ? NetworkAlertCriteria : UnitAlertCriteria;
}
