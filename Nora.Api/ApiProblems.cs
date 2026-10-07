namespace Nora.Api;

internal static class ApiProblems
{
    public static Task WriteAsync(HttpContext context, int status)
    {
        var (code, title, detail) = status switch
        {
            404 => ("not-found", "Recurso não encontrado", "Não existe um recurso para o endereço solicitado."),
            405 => ("method-not-allowed", "Método não permitido", "O método HTTP não é permitido para este recurso. Consulte o cabeçalho Allow."),
            500 => ("internal-error", "Erro interno", "Não foi possível concluir a solicitação. Informe o traceId ao suporte."),
            _ => ("http-error", "Falha na solicitação", "Não foi possível concluir a solicitação HTTP.")
        };
        context.Response.Headers.CacheControl = "no-store";
        return Results.Problem(
            statusCode: status,
            type: $"urn:nora:problem:{code}",
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = context.TraceIdentifier }
        ).ExecuteAsync(context);
    }
}
