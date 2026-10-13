using System.Windows;
using System.Windows.Input;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

public partial class ItemDetailsWindow : Window
{
    public ItemDetailsWindow(ItemDetails details, IClipboardService? clipboard = null)
    {
        InitializeComponent();
        DataContext = new ItemDetailsViewModel(details, clipboard);
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
    }
    private void OnClose(object sender, RoutedEventArgs args) => Close();
    private void OnDragHeader(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
