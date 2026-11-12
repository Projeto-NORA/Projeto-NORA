using System.Windows;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Services;

/// <summary>
/// Rede de segurança dos executáveis: exceção não tratada em tratador assíncrono ou temporizador vira mensagem na faixa comum
/// em vez de derrubar o aplicativo. O harness de testes não instala este tratador, então erros de programação continuam visíveis nele.
/// </summary>
public static class UnhandledErrorHandler
{
    public static void Install(Application application, Func<WorkspaceViewModel?> workspace)
    {
        application.DispatcherUnhandledException += (_, args) =>
        {
            if (workspace() is not { } current) return;
            current.ReportUnexpected(args.Exception);
            args.Handled = true;
        };
    }
}
