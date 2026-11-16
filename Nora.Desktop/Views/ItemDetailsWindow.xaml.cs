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
            Width = 780; Height = 760;
            TabsHost.ContentTemplate = (DataTemplate)Resources["TabsTemplate"]; TabsHost.Content = DataContext;
            HealthHost.ContentTemplate = (DataTemplate)Resources["HealthTemplate"]; HealthHost.Content = DataContext;
        }
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
    }
    private void OnClose(object sender, RoutedEventArgs args) => Close();
    private void OnDragHeader(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
