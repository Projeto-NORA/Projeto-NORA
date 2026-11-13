using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

/// <summary>Troca de senha da própria conta (Configurações) e troca obrigatória da senha provisória, comuns ao OBS e à Secretaria.</summary>
public sealed partial class WorkspaceViewModel
{
    private bool mustChangePassword;
    private string currentPasswordError = "", newPasswordError = "", confirmPasswordError = "";

    /// <summary>Senha provisória ainda não trocada: a tela mostra somente a definição da nova senha e o botão Sair.</summary>
    public bool MustChangePassword => mustChangePassword && IsAuthenticated;
    public string CurrentPasswordError { get => currentPasswordError; private set { if (currentPasswordError == value) return; currentPasswordError = value; Notify(); } }
    public string NewPasswordError { get => newPasswordError; private set { if (newPasswordError == value) return; newPasswordError = value; Notify(); } }
    public string ConfirmPasswordError { get => confirmPasswordError; private set { if (confirmPasswordError == value) return; confirmPasswordError = value; Notify(); } }
    public void ClearPasswordErrors() { CurrentPasswordError = ""; NewPasswordError = ""; ConfirmPasswordError = ""; }

    /// <summary>
    /// Confere os campos localmente, troca a senha na API e informa o resultado: senha atual errada vira mensagem no campo dela.
    /// As senhas chegam só como argumento e nunca ficam no estado. Devolve se a troca foi concluída (a tela então limpa os campos).
    /// </summary>
    public async Task<bool> ChangePasswordAsync(string current, string next, string confirm)
    {
        var changed = false;
        await RunAsync(async () =>
        {
            ClearPasswordErrors();
            CurrentPasswordError = current.Length == 0 ? "Informe a senha atual." : "";
            NewPasswordError = next.Length is < 8 or > 256 ? "A nova senha precisa ter de 8 a 256 caracteres."
                : next == current && current.Length > 0 ? "A nova senha precisa ser diferente da atual." : "";
            ConfirmPasswordError = NewPasswordError.Length == 0 && confirm != next ? "A confirmação não é igual à nova senha." : "";
            if (CurrentPasswordError.Length + NewPasswordError.Length + ConfirmPasswordError.Length > 0) return;
            try { await api.ChangePasswordAsync(current, next); }
            catch (NoraApiException error) when (error.Code == "invalid-current-password") { CurrentPasswordError = error.Message; return; }
            if (disposed || !IsAuthenticated) return;
            changed = true;
            var wasForced = mustChangePassword;
            mustChangePassword = false;
            NotifySession(); Notify(nameof(MustChangePassword));
            Say(MessageKind.Success, "Senha alterada. As outras sessões desta conta foram encerradas; esta continua aberta.");
            if (wasForced) { refreshTimer.Start(); await LoadAfterLoginAsync(); }
        });
        return changed;
    }

    /// <summary>A API exigiu a troca da senha provisória: encerra a leitura de dados e deixa só a tela de nova senha.</summary>
    private void EnterPasswordChange()
    {
        if (disposed || !IsAuthenticated) return;
        refreshTimer.Stop();
        ResetLoadedData();
        mustChangePassword = true;
        ClearPasswordErrors();
        NotifySession(); Notify(nameof(MustChangePassword));
        Say(MessageKind.Info, "Esta conta usa uma senha provisória. Defina uma nova senha para continuar.");
    }
}
