using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Senha atual, nova e confirmação. As senhas só existem nos campos: vão ao ViewModel como argumento e os campos são limpos ao concluir ou sair.</summary>
public partial class ChangePasswordView : UserControl
{
    /// <summary>Conteúdo opcional à esquerda do botão primário (ex.: o botão Sair da troca obrigatória de senha).</summary>
    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(nameof(Secondary), typeof(object), typeof(ChangePasswordView), new PropertyMetadata(null));
    public object? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }

    private WorkspaceViewModel? observed;
    public ChangePasswordView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        Loaded += (_, _) => Observe();
        Unloaded += (_, _) => { ClearAll(); Unobserve(); };
    }
    private void Observe() { Unobserve(); observed = DataContext as WorkspaceViewModel; if (observed != null) observed.PropertyChanged += OnChanged; }
    private void Unobserve() { if (observed != null) observed.PropertyChanged -= OnChanged; observed = null; }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(WorkspaceViewModel.IsAuthenticated) or nameof(WorkspaceViewModel.MustChangePassword)) ClearAll();
    }
    private void ClearAll() { Current.Clear(); Next.Clear(); Confirm.Clear(); }
    private async void OnChange(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { CanOperate: true } vm) return;
        var changed = await vm.ChangePasswordAsync(Current.Password, Next.Password, Confirm.Password);
        if (changed) ClearAll();
        else if (vm.CurrentPasswordError.Length > 0) { Current.Clear(); Current.Focus(); }
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnChange(sender, e); e.Handled = true;
    }
    private void OnAdvanceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        (sender == Current ? Next : Confirm).Focus();
        e.Handled = true;
    }
}
