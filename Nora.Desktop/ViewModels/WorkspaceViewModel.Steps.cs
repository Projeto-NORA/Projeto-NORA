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
            step.State = StepState.Done;
        }
        catch (Exception)
        {
            step.State = StepState.Failed;
            throw;
        }
    }
}
