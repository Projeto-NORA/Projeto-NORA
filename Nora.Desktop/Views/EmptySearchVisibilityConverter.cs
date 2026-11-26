using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nora.Desktop.Views;

/// <summary>
/// Mostra "Nenhum … encontrado para esta busca." só quando a lista está vazia por causa da pesquisa: recebe a quantidade de itens da lista (já filtrada) e o texto de estado da tela
/// ("Nenhum … cadastrado.", "Carregando…", falha), que, quando existe, é o que explica a lista vazia.
/// </summary>
public sealed class EmptySearchVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is int count && count == 0 && string.IsNullOrEmpty(values[1] as string) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
