using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>Campos da redefinição de senha provisória no pop-up (<see cref="StaffAccountsView"/>); as senhas só existem nos campos e são apagadas ao fechar.</summary>
public partial class StaffResetFormView : UserControl
{
    public StaffResetFormView()
    {
        InitializeComponent();
        Unloaded += (_, _) => ClearPasswords();
    }

    public string Password => PasswordField.Password;
    public string Confirm => ConfirmField.Password;
    public void ClearPasswords() { PasswordField.Clear(); ConfirmField.Clear(); }
}
