using System.Windows;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class StaffProfileWindow : Window
{
    private readonly WorkspaceViewModel model;

    public StaffProfileWindow(WorkspaceViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        Loaded += OnReadProfile;
        MaxHeight = Math.Max(345, Math.Min(MaxHeight, SystemParameters.WorkArea.Height - 54));
    }

    private async void OnReadProfile(object sender, RoutedEventArgs args)
    {
        Loaded -= OnReadProfile;
        try
        {
            ShowProfile(await model.ReadStaffProfileAsync());
            ProfileStatus.Text = "";
            ProfileFields.IsEnabled = true;
        }
        catch (NoraApiException error) { ProfileStatus.Text = error.Message; }
        finally { ProfileProgress.Visibility = Visibility.Collapsed; }
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
    }
}
