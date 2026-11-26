using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Campos da conta nova no pop-up (<see cref="StaffAccountsView"/>). As senhas só existem nos campos: quem salva as lê daqui, como argumento do envio,
/// e <see cref="ClearPasswords"/> as apaga ao criar a conta ou fechar o pop-up.
/// </summary>
public partial class StaffCreateFormView : UserControl
{
    public StaffCreateFormView()
    {
        InitializeComponent();
        Unloaded += (_, _) => ClearPasswords();
    }

    public string Password => PasswordField.Password;
    public string Confirm => ConfirmField.Password;
    public void ClearPasswords() { PasswordField.Clear(); ConfirmField.Clear(); }
}
