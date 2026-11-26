using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Abre a ficha em linhas dos itens das listagens da Secretaria (segundo clique, duplo clique e Enter). A ficha padrão da janela principal só conhece os itens do OBS; aqui a ficha vem de
/// <see cref="WorkspaceViewModel.BuildMunicipalDetailsAsync"/> e fecha sozinha se a sessão termina, como a do OBS.
/// </summary>
internal static class MunicipalDetailsHost
{
    /// <summary>Liga a listagem à ficha da Secretaria (o tratador de abertura da <see cref="ListingBehavior"/>).</summary>
    public static void Attach(ListBox list, FrameworkElement anchor) => ListingBehavior.SetOpenHandler(list, item => OpenAsync(anchor, item));

    public static async Task OpenAsync(FrameworkElement anchor, object item)
    {
        if (anchor.DataContext is not WorkspaceViewModel vm) return;
        var details = await vm.BuildMunicipalDetailsAsync(item);
        if (details is null) return;
        var owner = Window.GetWindow(anchor);
        var dialog = new ItemDetailsWindow(details) { Owner = owner, Title = details.Title };
        PropertyChangedEventHandler closeOnSignOut = (_, changed) => { if (changed.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated) && !vm.IsAuthenticated) dialog.Close(); };
        vm.PropertyChanged += closeOnSignOut;
        dialog.Closed += (_, _) => vm.PropertyChanged -= closeOnSignOut;
        DialogService.ShowModal(dialog, owner);
    }
}
