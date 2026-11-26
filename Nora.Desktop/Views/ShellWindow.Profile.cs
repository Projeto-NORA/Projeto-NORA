using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Menu do perfil: "Trocar perfil" (só no OBS) e a abertura sozinha do pop-up quando a conta tem várias UBS e nenhuma ativa.</summary>
public partial class ShellWindow
{
    /// <summary>
    /// Depois do login e depois de restaurar a sessão, a conta com várias UBS e nenhuma ativa abre sozinha o pop-up "Trocar perfil". As checagens desligam isto para a
    /// janela de teste não ficar presa num pop-up modal (a checagem do pop-up sozinho liga de novo).
    /// </summary>
    public static bool AutoOpenProfileChoice { get; set; } = true;
    private bool switchProfileOpen;

    private void InitializeProfileMenu(DesktopProfile profile)
    {
        // A Secretaria não escolhe UBS: o menu dela tem só Configurações, Dados do perfil e Sair da conta.
        if (profile.IsMunicipal) AccountButton.ContextMenu?.Items.Remove(SwitchProfileItem);
        ViewModel.Workspace.PropertyChanged += OnProfileChoiceChanged;
        Closed += (_, _) => ViewModel.Workspace.PropertyChanged -= OnProfileChoiceChanged;
    }

    private void OnSwitchProfile(object sender, RoutedEventArgs args) => OpenSwitchProfile();

    /// <summary>
    /// Abre o pop-up "Trocar perfil": pelo menu do perfil, pelo botão "Escolher UBS" dos avisos das telas e sozinho depois do login. Só no OBS, com a conta entrada e sem troca
    /// de senha pendente; um pop-up por vez.
    /// </summary>
    public void OpenSwitchProfile()
    {
        var workspace = ViewModel.Workspace;
        if (switchProfileOpen || !workspace.IsOperational || !workspace.IsAuthenticated || workspace.MustChangePassword) return;
        switchProfileOpen = true;
        try { DialogService.ShowModal(new SwitchProfileDialog(workspace), this); }
        finally { switchProfileOpen = false; }
    }

    /// <summary>O pedido de escolha da UBS só é atendido quando nenhuma ação do usuário está em andamento (o login ainda é uma), e fora do evento que o levantou.</summary>
    private void OnProfileChoiceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!AutoOpenProfileChoice || args.PropertyName is not (nameof(WorkspaceViewModel.ProfileChoicePending) or nameof(WorkspaceViewModel.CanOperate))) return;
        var workspace = ViewModel.Workspace;
        if (workspace.ProfileChoicePending && workspace.CanOperate) Dispatcher.BeginInvoke(DispatcherPriority.Background, OpenPendingProfileChoice);
    }

    private void OpenPendingProfileChoice()
    {
        var workspace = ViewModel.Workspace;
        if (!workspace.ProfileChoicePending || !workspace.CanOperate || !IsLoaded) return;
        workspace.AcknowledgeProfileChoice();
        if (workspace.NeedsObsUnitChoice) OpenSwitchProfile();
    }
}
