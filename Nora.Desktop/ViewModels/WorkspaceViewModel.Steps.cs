using System.Collections.ObjectModel;
using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

/// <summary>
/// Passos visíveis de uma operação demorada (regra do usuário de 16/11/2026): cada passo aparece girando enquanto roda, fica marcado como concluído quando termina
/// e como falho, com o motivo na faixa de retorno, se der erro. Fluxos de vários passos acumulam os concluídos em vez de substituí-los.
/// </summary>
public sealed partial class WorkspaceViewModel
{
    /// <summary>Roda <paramref name="work"/> sob um passo novo de <paramref name="steps"/>. A exceção continua subindo, depois de marcar o passo como falho.</summary>
    private static async Task WithStepAsync(ObservableCollection<ProgressStep> steps, string text, Func<Task> work, bool clear = true)
    {
        if (clear) steps.Clear();
        var step = new ProgressStep(text);
        steps.Add(step);
        try
        {
            await work();
            step.Text = DoneText(text);
            step.State = StepState.Done;
        }
        catch (Exception)
        {
            step.Text = $"Falhou: {text.TrimEnd('…', '.', ' ')}";
            step.State = StepState.Failed;
            throw;
        }
    }

    /// <summary>Texto do passo concluído, no passado, para a marca de concluído não ficar ao lado de um verbo em andamento ("✓ Salvando…").</summary>
    private static string DoneText(string running) => running switch
    {
        "Lendo a grade da UBS…" => "Grade da UBS lida",
        "Buscando horários livres…" => "Horários livres encontrados",
        "Conferindo o 1º da fila…" => "1º da fila conferido",
        "Oferecendo o horário…" => "Horário oferecido",
        "Conferindo este computador…" => "Computador conferido",
        "Restaurando a sessão salva…" => "Sessão restaurada",
        "Ativando este computador…" => "Computador ativado",
        "Lendo os computadores…" => "Computadores lidos",
        "Guardando a ativação neste computador…" => "Ativação guardada neste computador",
        "Gerando o código de ativação…" => "Código de ativação gerado",
        "Revogando o computador…" => "Computador revogado",
        "Salvando a aprovação das vagas…" => "Aprovação das vagas salva",
        "Aprovando a vaga…" => "Vaga aprovada",
        "Descartando…" => "Vaga descartada",
        "Salvando horários…" => "Horários salvos",
        _ => running.TrimEnd('…', '.', ' ')
    };
}
