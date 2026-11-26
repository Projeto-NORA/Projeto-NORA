using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace Nora.Desktop.Views;

/// <summary>
/// Mostra "nenhum … encontrado para esta busca" quando a lista tem itens, mas a pesquisa em linha escondeu todos. Diferente do estado "nenhum … cadastrado" (a lista de origem está vazia),
/// que vem do ViewModel. Uso: <c>views:SearchEmptyNotice.Target="{Binding ElementName=SearchEmpty}"</c> na lista, com o aviso (um <c>TextBlock</c> recolhido) como alvo.
/// O comportamento liga no <c>Loaded</c> da lista e desliga no <c>Unloaded</c>.
/// </summary>
public static class SearchEmptyNotice
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached("Target", typeof(UIElement), typeof(SearchEmptyNotice), new PropertyMetadata(null, OnTargetChanged));
    private static readonly DependencyProperty SubscriptionProperty = DependencyProperty.RegisterAttached("Subscription", typeof(NotifyCollectionChangedEventHandler), typeof(SearchEmptyNotice));

    public static UIElement? GetTarget(DependencyObject element) => (UIElement?)element.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject element, UIElement? value) => element.SetValue(TargetProperty, value);

    private static void OnTargetChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ListBox list) throw new InvalidOperationException("SearchEmptyNotice.Target só vale em um ListBox.");
        list.Loaded -= OnLoaded;
        list.Unloaded -= OnUnloaded;
        Unsubscribe(list);
        if (args.NewValue is null) return;
        list.Loaded += OnLoaded;
        list.Unloaded += OnUnloaded;
        if (list.IsLoaded) Subscribe(list);
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => Subscribe((ListBox)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs args) => Unsubscribe((ListBox)sender);

    private static void Subscribe(ListBox list)
    {
        if (list.GetValue(SubscriptionProperty) is not null) return;
        NotifyCollectionChangedEventHandler handler = (_, _) => Update(list);
        list.SetValue(SubscriptionProperty, handler);
        ((INotifyCollectionChanged)list.Items).CollectionChanged += handler;
        Update(list);
    }

    private static void Unsubscribe(ListBox list)
    {
        if (list.GetValue(SubscriptionProperty) is not NotifyCollectionChangedEventHandler handler) return;
        ((INotifyCollectionChanged)list.Items).CollectionChanged -= handler;
        list.ClearValue(SubscriptionProperty);
    }

    private static void Update(ListBox list)
    {
        if (GetTarget(list) is not { } target) return;
        var hiddenBySearch = list.Items.Count == 0 && list.ItemsSource is ICollection { Count: > 0 };
        target.Visibility = hiddenBySearch ? Visibility.Visible : Visibility.Collapsed;
    }
}
