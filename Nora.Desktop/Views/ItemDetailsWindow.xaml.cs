using System.Windows;
using System.Windows.Input;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class ItemDetailsWindow : Window
{
    public ItemDetailsWindow(ItemDetails details, IClipboardService? clipboard = null, HealthRecordsViewModel? health = null)
    {
        InitializeComponent();
        DataContext = new ItemDetailsViewModel(details, clipboard, health);
        // A aba de exames e vacinas tem formulários: a janela da ficha do paciente é mais larga.
        if (health is not null)
        {
            SizeToContent = SizeToContent.Manual; Width = 780; Height = 760;
            TabsHost.ContentTemplate = (DataTemplate)Resources["TabsTemplate"]; TabsHost.Content = DataContext;
            HealthHost.ContentTemplate = (DataTemplate)Resources["HealthTemplate"]; HealthHost.Content = DataContext;
        }
        // A ficha nunca passa da altura da janela dona (nem da área de trabalho): a curta tem a altura do conteúdo e a longa rola.
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
        SourceInitialized += (_, _) => MaxHeight = Math.Max(320, Math.Min(SystemParameters.WorkArea.Height, Owner is { IsLoaded: true } owner ? owner.ActualHeight : double.PositiveInfinity) - 40);
    }
    /// <summary>Área de ações no rodapé da ficha (botões à direita), para as telas que quiserem ações junto da ficha; sem ela, o rodapé só mostra o aviso de cópia.</summary>
    public FrameworkElement? Actions
    {
        get => ActionsHost.Content as FrameworkElement;
        set { ActionsHost.Content = value; ActionsHost.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible; }
    }
    private void OnClose(object sender, RoutedEventArgs args) => Close();
    private void OnDragHeader(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
