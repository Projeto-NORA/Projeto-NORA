using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.ViewModels;

namespace Nora.Desktop.Views;

/// <summary>Capacidade da rede por UBS e região, com o cadastro (profissionais, consultórios e grade) de uma UBS em leitura.</summary>
public partial class CapacityView : UserControl
{
    public CapacityView() => InitializeComponent();
    private void OnRetryCatalog(object sender, RoutedEventArgs e) => (DataContext as WorkspaceViewModel)?.RetryCapacityCatalog();
}
