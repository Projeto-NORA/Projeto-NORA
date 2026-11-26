using System.Windows;
using System.Windows.Input;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Dados do perfil no pop-up da marca (sem moldura do Windows, título e X no topo, o fundo da janela principal escurecido enquanto ele está aberto): nome, e-mail e função em linhas,
/// "Salvar nome" à direita e a troca de senha num bloco próprio. Quem abre pode usar <c>ShowDialog()</c> direto: o escurecimento acompanha a abertura e o fechamento da janela.
/// </summary>
public partial class StaffProfileWindow : Window
{
    private readonly WorkspaceViewModel model;
    private IDialogHost? dimmedHost;

    public StaffProfileWindow(WorkspaceViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        Loaded += OnLoaded;
        Closed += OnClosed;
        MaxHeight = Math.Max(345, Math.Min(MaxHeight, SystemParameters.WorkArea.Height - 54));
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        // O mesmo escurecimento do DialogService.ShowModal: a janela dona escurece enquanto o pop-up existe (e volta ao normal ao fechar).
        dimmedHost = Owner as IDialogHost;
        dimmedHost?.SetDimmed(true);
        try
        {
            ShowProfile(await model.ReadStaffProfileAsync());
            ProfileStatus.Text = "";
            ProfileFields.IsEnabled = true;
        }
        catch (NoraApiException error) { ProfileStatus.Text = error.Message; }
        finally { ProfileProgress.Visibility = Visibility.Collapsed; }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        dimmedHost?.SetDimmed(false);
        dimmedHost = null;
    }

    private void OnClose(object sender, RoutedEventArgs args) => Close();

    private void OnDrag(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton == MouseButtonState.Pressed && args.OriginalSource is not System.Windows.Controls.Button) DragMove();
    }

    private async void OnSaveName(object sender, RoutedEventArgs args)
    {
        var name = FullName.Text.Trim();
        if (name.Length < 2) { ProfileStatus.Text = "Informe o nome completo."; FullName.Focus(); return; }
        ProfileFields.IsEnabled = false;
        ProfileStatus.Text = "Salvando seu nome…";
        ProfileProgress.Visibility = Visibility.Visible;
        try
        {
            ShowProfile(await model.SaveStaffNameAsync(name));
            ProfileStatus.Text = "Nome salvo.";
        }
        catch (NoraApiException error) { ProfileStatus.Text = error.Message; }
        finally
        {
            ProfileFields.IsEnabled = true;
            ProfileProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowProfile(StaffSelfProfile profile)
    {
        FullName.Text = profile.FullName;
        Email.Text = profile.Email;
        ScopeText.Text = profile.Scope == "municipal" ? profile.MunicipalityName ?? "Gestão municipal"
            : string.Join(", ", profile.Units.Select(item => item.Name));
        ScopeRow.Visibility = ScopeText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
