using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>
/// Janelas das listagens da Secretaria. A ficha em linhas (segundo clique, duplo clique, Enter e o "Ver ficha" do balão) vem de
/// <see cref="WorkspaceViewModel.BuildMunicipalDetailsAsync"/>, abre uma por vez e fecha sozinha se a sessão termina, como a do OBS. Os pop-ups de cadastro
/// (<see cref="ShowForm"/>) também fecham sozinhos quando a sessão termina, para nenhum campo digitado (senha provisória inclusive) ficar aberto sobre a tela de acesso.
/// </summary>
internal static class MunicipalDetailsHost
{
    private static bool opening;

    /// <summary>Liga a listagem à ficha da Secretaria (o tratador de abertura da <see cref="ListingBehavior"/>).</summary>
    public static void Attach(ListBox list, FrameworkElement anchor) => ListingBehavior.SetOpenHandler(list, item => OpenAsync(anchor, item));

    /// <summary>Abre a ficha do item. Enquanto uma ficha está sendo montada ou aberta, um segundo pedido (clique duplo no "Ver ficha") é ignorado.</summary>
    public static async Task OpenAsync(FrameworkElement anchor, object item)
    {
        if (opening || anchor.DataContext is not WorkspaceViewModel vm) return;
        opening = true;
        try
        {
            var details = await vm.BuildMunicipalDetailsAsync(item);
            if (details is null) return;
            var owner = Window.GetWindow(anchor);
            var dialog = new ItemDetailsWindow(details) { Owner = owner, Title = details.Title };
            using (CloseOnSignOut(vm, dialog.Close)) DialogService.ShowModal(dialog, owner);
        }
        finally { opening = false; }
    }

    /// <summary>Pop-up de cadastro da Secretaria (<see cref="DialogService.ShowForm"/>) que fecha sem salvar se a sessão termina enquanto ele está aberto.</summary>
    public static bool ShowForm(FrameworkElement anchor, WorkspaceViewModel vm, string title, FrameworkElement content, Func<Task<bool>> save, string primary, Func<string?> error)
    {
        using (CloseOnSignOut(vm, () => Window.GetWindow(content)?.Close()))
            return DialogService.ShowForm(Window.GetWindow(anchor), title, content, save, primary, error: error);
    }

    private static IDisposable CloseOnSignOut(WorkspaceViewModel vm, Action close)
    {
        PropertyChangedEventHandler handler = (_, changed) => { if (changed.PropertyName == nameof(WorkspaceViewModel.IsAuthenticated) && !vm.IsAuthenticated) close(); };
        vm.PropertyChanged += handler;
        return new Subscription(() => vm.PropertyChanged -= handler);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
