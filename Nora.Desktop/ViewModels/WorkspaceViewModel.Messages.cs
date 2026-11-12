using Nora.Desktop.Models;

namespace Nora.Desktop.ViewModels;

/// <summary>Faixa comum de retorno das ações (informação, sucesso, erro) e aviso de dados possivelmente desatualizados.</summary>
public sealed partial class WorkspaceViewModel
{
    private string message = "";
    private MessageKind messageKind = MessageKind.Info;
    private bool dataStale, refreshFailed;
    private DateTimeOffset? lastSuccessfulRead;
    private string? refreshErrorText;

    public string Message => message;
    public MessageKind MessageKind => messageKind;
    public bool HasMessage => message.Length > 0;
    /// <summary>Nome do tipo, usado pelos gatilhos de estilo da faixa.</summary>
    public string MessageKindName => messageKind switch { MessageKind.Success => "success", MessageKind.Error => "error", _ => "info" };
    /// <summary>O tipo também aparece como ícone e como palavra, nunca só como cor.</summary>
    public string MessageIcon => messageKind switch { MessageKind.Success => "", MessageKind.Error => "", _ => "" };
    public string MessageKindLabel => messageKind switch { MessageKind.Success => "Sucesso", MessageKind.Error => "Erro", _ => "Informação" };
    public bool IsDataStale => dataStale;
    public string StaleLabel => !dataStale ? "" : lastSuccessfulRead is { } time
        ? $"Dados possivelmente desatualizados. Última leitura bem-sucedida às {time.ToLocalTime():HH:mm:ss}."
        : "Dados possivelmente desatualizados. Ainda não houve leitura bem-sucedida.";

    private static readonly string[] MessageNames = [nameof(Message), nameof(MessageKind), nameof(MessageKindName), nameof(MessageIcon), nameof(MessageKindLabel), nameof(HasMessage)];
    private static readonly string[] StaleNames = [nameof(IsDataStale), nameof(StaleLabel)];

    private void Say(MessageKind kind, string text)
    {
        if (disposed) return;
        message = text; messageKind = kind;
        foreach (var name in MessageNames) Notify(name);
    }

    private void ClearMessage()
    {
        if (message.Length == 0) return;
        message = ""; messageKind = MessageKind.Info;
        foreach (var name in MessageNames) Notify(name);
    }

    /// <summary>Trocar de seção apaga informação e sucesso; o erro permanece até a próxima ação ou até ser dispensado.</summary>
    private void ClearTransientMessage() { if (messageKind != MessageKind.Error) ClearMessage(); }

    /// <summary>Registra uma falha de leitura sem repetir o mesmo erro a cada ciclo: o aviso aparece uma vez e os dados ficam marcados.</summary>
    private void RegisterRefreshFailure(string text, bool show)
    {
        refreshFailed = true;
        MarkStale();
        if (refreshErrorText == text) return;
        refreshErrorText = text;
        if (show) Say(MessageKind.Error, text);
    }

    /// <summary>Leitura voltou a funcionar: remove o erro antigo da leitura (não o de uma ação posterior) e o aviso de dados antigos.</summary>
    private void RecoverFromRefreshFailure()
    {
        if (!refreshFailed) return;
        if (messageKind == MessageKind.Error && message == refreshErrorText) ClearMessage();
        refreshFailed = false; refreshErrorText = null;
    }

    private void MarkStale()
    {
        if (disposed) return;
        dataStale = true;
        foreach (var name in StaleNames) Notify(name);
    }

    private void MarkFresh()
    {
        if (disposed) return;
        lastSuccessfulRead = DateTimeOffset.Now;
        if (!dataStale) return;
        dataStale = false;
        foreach (var name in StaleNames) Notify(name);
    }

    private void ClearRefreshState()
    {
        refreshFailed = false; refreshErrorText = null; lastSuccessfulRead = null;
        if (!dataStale) return;
        dataStale = false;
        foreach (var name in StaleNames) Notify(name);
    }

    /// <summary>Último recurso para exceção inesperada de um tratador assíncrono: informa na faixa comum sem expor detalhes técnicos.</summary>
    public void ReportUnexpected(Exception error)
    {
        System.Diagnostics.Trace.TraceError("NORA: exceção não tratada: {0}", error);
        Say(MessageKind.Error, "Ocorreu um erro inesperado. Tente novamente; se continuar, reinicie o aplicativo.");
    }
}
