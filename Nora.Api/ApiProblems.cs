namespace Nora.Api;

internal static class ApiProblems
{
    public static Task WriteAsync(HttpContext context, int status, string? customCode = null)
    {
        var (code, title, detail) = customCode switch
        {
            "invalid-current-password" => ("invalid-current-password", "Senha atual incorreta", "A senha atual informada não confere."),
            "password-change-required" => ("password-change-required", "Troca de senha obrigatória", "Defina uma nova senha antes de continuar."),
            "database-provisioning-failed" => ("database-provisioning-failed", "Banco da UBS indisponível", "Não foi possível criar ou preparar o banco da unidade. Tente novamente mais tarde."),
            _ => DefaultFor(status)
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

    private static (string Code, string Title, string Detail) DefaultFor(int status) => status switch
    {
            400 => ("invalid-input", "Dados inválidos", "Confira os campos informados. Senha deve ter de 8 a 256 caracteres e CPF deve ser válido."),
            401 => ("unauthorized", "Acesso necessário", "Credenciais inválidas ou sessão expirada. Entre novamente."),
            403 => ("forbidden", "Acesso não permitido", "Sua conta não pode executar esta operação."),
            409 => ("conflict", "Operação em conflito", "Cadastro já existe ou a operação não está disponível neste estado."),
            429 => ("too-many-requests", "Muitas tentativas", "Aguarde um pouco e tente novamente mais tarde."),
            503 => ("service-unavailable", "Serviço indisponível", "O serviço está temporariamente indisponível. Tente novamente mais tarde."),
            404 => ("not-found", "Recurso não encontrado", "Não existe um recurso para o endereço solicitado."),
            405 => ("method-not-allowed", "Método não permitido", "O método HTTP não é permitido para este recurso. Consulte o cabeçalho Allow."),
            500 => ("internal-error", "Erro interno", "Não foi possível concluir a solicitação. Informe o traceId ao suporte."),
            _ => ("http-error", "Falha na solicitação", "Não foi possível concluir a solicitação HTTP.")
    };
}
