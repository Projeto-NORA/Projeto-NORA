using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nora.Desktop.Views;

/// <summary>
/// Liga uma listagem aberta (<c>ListBox</c> com o estilo <c>OpenList</c>) a um <see cref="SelectionBalloon"/>. A seleção em si é a do WPF
/// (<c>SelectionMode="Extended"</c>: clique, setas, Ctrl, Shift): nada dela é reimplementado aqui. O comportamento acrescenta:
/// <list type="bullet">
/// <item>o balão aparece com pelo menos um item selecionado e some quando a seleção se desfaz; o rótulo é o nome do item ou "N itens selecionados";</item>
/// <item>Esc e o X do balão desfazem a seleção; um clique no espaço passivo da tela também (um clique em linha, balão, botão, campo de texto, combo ou barra de rolagem não);</item>
/// <item>o segundo clique numa linha já selecionada e o Enter abrem a ficha, uma de cada vez, em fila (a ordem é a da lista, não a do clique);</item>
/// <item>a seleção é guardada pela chave dos itens: trocar a fonte, filtrar ou o ciclo automático de 20 s que recria as listas devolve a seleção aos itens
/// que continuam na lista, sem o balão piscar nem sumir (o que saiu da lista sai da seleção).</item>
/// </list>
/// Uso: <c>views:ListingBehavior.Balloon="{Binding ElementName=Balao}"</c> na lista, mais <c>LabelPath</c>, <c>KeyPath</c>, <c>PluralName</c> e, se quiser,
/// <c>ComplementPath</c> e <c>Feminine</c>. A ficha padrão é a da janela principal; <see cref="SetOpenHandler"/> troca (os testes usam).
/// O comportamento liga no <c>Loaded</c> da lista e desliga no <c>Unloaded</c>, então não deixa assinatura para trás quando a tela sai.
/// </summary>
public static class ListingBehavior
{
    public static readonly DependencyProperty BalloonProperty = DependencyProperty.RegisterAttached("Balloon", typeof(SelectionBalloon), typeof(ListingBehavior), new PropertyMetadata(null, OnBalloonChanged));
    /// <summary>Propriedade do item que serve de nome no balão (padrão <c>Name</c>); sem ela, vale o <c>ToString()</c> do item.</summary>
    public static readonly DependencyProperty LabelPathProperty = DependencyProperty.RegisterAttached("LabelPath", typeof(string), typeof(ListingBehavior), new PropertyMetadata("Name"));
    /// <summary>Propriedade do item que o identifica (padrão <c>Id</c>); sem ela, a identidade é o próprio item.</summary>
    public static readonly DependencyProperty KeyPathProperty = DependencyProperty.RegisterAttached("KeyPath", typeof(string), typeof(ListingBehavior), new PropertyMetadata("Id"));
    /// <summary>Propriedade do item mostrada ao lado do nome quando só um item está selecionado (ex.: o CPF).</summary>
    public static readonly DependencyProperty ComplementPathProperty = DependencyProperty.RegisterAttached("ComplementPath", typeof(string), typeof(ListingBehavior), new PropertyMetadata(null));
    /// <summary>Plural do item no rótulo com vários selecionados: "3 <c>pacientes</c> selecionados".</summary>
    public static readonly DependencyProperty PluralNameProperty = DependencyProperty.RegisterAttached("PluralName", typeof(string), typeof(ListingBehavior), new PropertyMetadata("itens"));
    /// <summary>Plural feminino: "3 consultas selecionadas".</summary>
    public static readonly DependencyProperty FeminineProperty = DependencyProperty.RegisterAttached("Feminine", typeof(bool), typeof(ListingBehavior), new PropertyMetadata(false));
    /// <summary>Abre a ficha de um item e só devolve quando ela fecha (a fila espera cada uma). Só em código; sem ele, a ficha da janela principal.</summary>
    public static readonly DependencyProperty OpenHandlerProperty = DependencyProperty.RegisterAttached("OpenHandler", typeof(Func<object, Task>), typeof(ListingBehavior), new PropertyMetadata(null));
    private static readonly DependencyProperty AttachmentProperty = DependencyProperty.RegisterAttached("Attachment", typeof(Attachment), typeof(ListingBehavior), new PropertyMetadata(null));

    /// <summary>Levantado na lista toda vez que o balão é atualizado com a seleção atual: a tela ajusta, por item, as ações que só valem para alguns (<see cref="GetSelectedItems(ListBox)"/>).</summary>
    public static readonly RoutedEvent BalloonRefreshedEvent = EventManager.RegisterRoutedEvent("BalloonRefreshed", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ListingBehavior));
    public static void AddBalloonRefreshedHandler(DependencyObject element, RoutedEventHandler handler) => (element as UIElement)?.AddHandler(BalloonRefreshedEvent, handler);
    public static void RemoveBalloonRefreshedHandler(DependencyObject element, RoutedEventHandler handler) => (element as UIElement)?.RemoveHandler(BalloonRefreshedEvent, handler);

    /// <summary>Intervalo da espera pelo segundo clique de um duplo clique; os testes trocam.</summary>
    internal static Func<TimeSpan> DoubleClickInterval { get; set; } = () => TimeSpan.FromMilliseconds(GetDoubleClickTime());

    public static SelectionBalloon? GetBalloon(DependencyObject element) => (SelectionBalloon?)element.GetValue(BalloonProperty);
    public static void SetBalloon(DependencyObject element, SelectionBalloon? value) => element.SetValue(BalloonProperty, value);
    public static string GetLabelPath(DependencyObject element) => (string)element.GetValue(LabelPathProperty);
    public static void SetLabelPath(DependencyObject element, string value) => element.SetValue(LabelPathProperty, value);
    public static string GetKeyPath(DependencyObject element) => (string)element.GetValue(KeyPathProperty);
    public static void SetKeyPath(DependencyObject element, string value) => element.SetValue(KeyPathProperty, value);
    public static string? GetComplementPath(DependencyObject element) => (string?)element.GetValue(ComplementPathProperty);
    public static void SetComplementPath(DependencyObject element, string? value) => element.SetValue(ComplementPathProperty, value);
    public static string GetPluralName(DependencyObject element) => (string)element.GetValue(PluralNameProperty);
    public static void SetPluralName(DependencyObject element, string value) => element.SetValue(PluralNameProperty, value);
    public static bool GetFeminine(DependencyObject element) => (bool)element.GetValue(FeminineProperty);
    public static void SetFeminine(DependencyObject element, bool value) => element.SetValue(FeminineProperty, value);
    public static Func<object, Task>? GetOpenHandler(DependencyObject element) => (Func<object, Task>?)element.GetValue(OpenHandlerProperty);
    public static void SetOpenHandler(DependencyObject element, Func<object, Task>? value) => element.SetValue(OpenHandlerProperty, value);

    /// <summary>Os itens selecionados na ordem da lista: <c>SelectedItems</c> guarda a ordem dos cliques, e as ações em lote e a fila de fichas seguem a tela.</summary>
    public static IReadOnlyList<object> GetSelectedItems(ListBox list)
    {
        if (list.SelectedItems.Count == 0) return [];
        var selected = new HashSet<object>(list.SelectedItems.Cast<object>(), ReferenceEqualityComparer.Instance);
        return list.Items.Cast<object>().Where(selected.Contains).ToList();
    }

    public static IReadOnlyList<T> GetSelectedItems<T>(ListBox list) where T : class => GetSelectedItems(list).OfType<T>().ToList();

    /// <summary>Desfaz a seleção (o que o X, o Esc e o clique fora fazem).</summary>
    public static void ClearSelection(ListBox list)
    {
        if (GetAttachment(list) is { } attachment) attachment.ClearSelection();
        else list.UnselectAll();
    }

    private static Attachment? GetAttachment(DependencyObject list) => (Attachment?)list.GetValue(AttachmentProperty);

    private static void OnBalloonChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ListBox list) throw new InvalidOperationException("ListingBehavior.Balloon só vale em um ListBox (a listagem aberta).");
        list.Loaded -= OnListLoaded;
        list.Unloaded -= OnListUnloaded;
        Detach(list);
        if (args.NewValue is null) return;
        list.Loaded += OnListLoaded;
        list.Unloaded += OnListUnloaded;
        if (list.IsLoaded) Attach(list);
    }

    private static void OnListLoaded(object sender, RoutedEventArgs args) => Attach((ListBox)sender);
    private static void OnListUnloaded(object sender, RoutedEventArgs args) => Detach((ListBox)sender);

    private static void Attach(ListBox list)
    {
        if (GetAttachment(list) is not null || GetBalloon(list) is not { } balloon) return;
        var attachment = new Attachment(list, balloon);
        list.SetValue(AttachmentProperty, attachment);
        attachment.Start();
    }

    private static void Detach(ListBox list)
    {
        if (GetAttachment(list) is not { } attachment) return;
        attachment.Stop();
        list.ClearValue(AttachmentProperty);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    /// <summary>O estado de uma lista ligada ao balão enquanto ela está na tela.</summary>
    private sealed class Attachment(ListBox list, SelectionBalloon balloon)
    {
        private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Properties = new();
        private readonly HashSet<object> keys = [];
        private Window? window;
        private bool started, restoring, reconcileQueued, opening;
        private object? pressedItem;
        private int pressedClicks;
        private IReadOnlyList<object> selectionBeforePress = [];
        private DispatcherTimer? pendingOpen;

        public void Start()
        {
            if (started) return;
            started = true;
            if (list.SelectionMode != SelectionMode.Extended) list.SelectionMode = SelectionMode.Extended;
            RebuildKeys();
            list.SelectionChanged += OnSelectionChanged;
            ((INotifyCollectionChanged)list.Items).CollectionChanged += OnItemsChanged;
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl)).AddValueChanged(list, OnItemsSourceChanged);
            list.PreviewKeyDown += OnPreviewKeyDown;
            balloon.PreviewKeyDown += OnPreviewKeyDown;
            balloon.CloseRequested += OnCloseRequested;
            list.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
            list.PreviewMouseLeftButtonUp += OnPreviewMouseUp;
            window = Window.GetWindow(list);
            if (window is not null) window.PreviewMouseLeftButtonDown += OnWindowMouseDown;
            Refresh();
        }

        public void Stop()
        {
            if (!started) return;
            started = false;
            CancelPendingOpen();
            list.SelectionChanged -= OnSelectionChanged;
            ((INotifyCollectionChanged)list.Items).CollectionChanged -= OnItemsChanged;
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl)).RemoveValueChanged(list, OnItemsSourceChanged);
            list.PreviewKeyDown -= OnPreviewKeyDown;
            balloon.PreviewKeyDown -= OnPreviewKeyDown;
            balloon.CloseRequested -= OnCloseRequested;
            list.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
            list.PreviewMouseLeftButtonUp -= OnPreviewMouseUp;
            if (window is not null) window.PreviewMouseLeftButtonDown -= OnWindowMouseDown;
            window = null;
        }

        public void ClearSelection()
        {
            CancelPendingOpen();
            list.UnselectAll();
        }

        // ----- seleção, balão e chaves -----

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (restoring) return;
            CancelPendingOpen();
            // Quem saiu da seleção, mas continua na lista, foi desfeito pela pessoa. Quem sumiu da lista (fonte trocada, filtro, ciclo de 20 s) guarda a chave
            // para voltar à seleção quando reaparecer.
            // Um conjunto dos itens da lista, montado uma vez: desfazer uma seleção grande não percorre a lista inteira para cada item.
            if (args.RemovedItems.Count > 0)
            {
                var items = new HashSet<object>(list.Items.Cast<object>(), ReferenceEqualityComparer.Instance);
                foreach (var removed in args.RemovedItems)
                    if (items.Contains(removed)) keys.Remove(KeyOf(removed));
            }
            foreach (var added in args.AddedItems) keys.Add(KeyOf(added));
            Refresh();
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args) => ScheduleReconcile();
        private void OnItemsSourceChanged(object? sender, EventArgs args) => ScheduleReconcile();

        /// <summary>
        /// A lista recriada devolve a seleção pelas chaves antes de qualquer quadro ser desenhado (prioridade Send): o balão não pisca. O que não voltou
        /// à lista sai da seleção.
        /// </summary>
        private void ScheduleReconcile()
        {
            if (reconcileQueued || !started) return;
            reconcileQueued = true;
            list.Dispatcher.BeginInvoke(DispatcherPriority.Send, Reconcile);
        }

        private void Reconcile()
        {
            reconcileQueued = false;
            if (!started) return;
            var selected = new HashSet<object>(list.SelectedItems.Cast<object>(), ReferenceEqualityComparer.Instance);
            var present = new HashSet<object>();
            restoring = true;
            try
            {
                foreach (var item in list.Items)
                {
                    var key = KeyOf(item);
                    present.Add(key);
                    if (keys.Contains(key) && !selected.Contains(item)) list.SelectedItems.Add(item);
                }
            }
            finally { restoring = false; }
            keys.IntersectWith(present);
            Refresh();
        }

        private void RebuildKeys()
        {
            keys.Clear();
            foreach (var item in list.SelectedItems) keys.Add(KeyOf(item));
        }

        private void Refresh()
        {
            if (!started || reconcileQueued) return;
            var selected = GetSelectedItems(list);
            if (selected.Count == 0)
            {
                // Seleção vazia com chaves guardadas: a lista foi recriada e a seleção ainda vai voltar. Não esconde o balão (nem o deixa piscar).
                if (keys.Count > 0) { ScheduleReconcile(); return; }
                if (balloon.Visibility != Visibility.Collapsed) balloon.Visibility = Visibility.Collapsed;
                return;
            }
            var single = selected.Count == 1;
            balloon.Label = single ? LabelOf(selected[0]) : $"{selected.Count} {GetPluralName(list)} {(GetFeminine(list) ? "selecionadas" : "selecionados")}";
            balloon.Complement = single && GetComplementPath(list) is { Length: > 0 } path ? ReadProperty(selected[0], path)?.ToString() ?? "" : "";
            balloon.ApplySelectionCount(selected.Count);
            if (balloon.Visibility != Visibility.Visible) balloon.Visibility = Visibility.Visible;
            list.RaiseEvent(new RoutedEventArgs(BalloonRefreshedEvent, list));
        }

        private void OnCloseRequested(object sender, RoutedEventArgs args)
        {
            ClearSelection();
            args.Handled = true;
        }

        // ----- teclado -----

        private void OnPreviewKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape && list.SelectedItems.Count > 0)
            {
                args.Handled = true;
                ClearSelection();
                return;
            }
            // Enter abre a(s) ficha(s) da seleção. Num botão, campo ou combo de dentro da linha ele continua sendo deles.
            if (args.Key != Key.Enter || ReferenceEquals(sender, balloon) || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) != 0) return;
            if (args.OriginalSource is TextBoxBase or ButtonBase or ComboBox) return;
            var selected = GetSelectedItems(list);
            if (selected.Count == 0) return;
            args.Handled = true;
            CancelPendingOpen();
            Post(selected);
        }

        // ----- mouse -----

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs args)
        {
            // O ListBox troca a seleção no MouseDown: o estado de antes do clique precisa ser guardado agora.
            pressedItem = ItemUnder(args.OriginalSource as DependencyObject);
            pressedClicks = args.ClickCount;
            selectionBeforePress = GetSelectedItems(list);
            if (args.ClickCount >= 2) CancelPendingOpen();
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs args)
        {
            var pressed = pressedItem;
            var before = selectionBeforePress;
            var clicks = pressedClicks;
            pressedItem = null;
            selectionBeforePress = [];
            // Ctrl e Shift estão montando a seleção, não pedindo para abri-la.
            if (pressed is null || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
            if (!ReferenceEquals(ItemUnder(args.OriginalSource as DependencyObject), pressed)) return;
            // O primeiro clique numa linha ainda não selecionada só a seleciona.
            if (!before.Any(item => ReferenceEquals(item, pressed))) return;
            // O clique sem modificador já reduziu a seleção a uma linha; como o segundo clique abre a fila inteira, a seleção anterior volta junto.
            if (before.Count > 1) RestoreSelection(before);
            if (clicks >= 2)
            {
                CancelPendingOpen();
                Post(before);
                return;
            }
            // Segundo clique, mas num clique só (a linha já estava selecionada): espera o intervalo do duplo clique, para o segundo clique de um duplo clique
            // rápido não cair em cima da ficha que acabou de abrir.
            CancelPendingOpen();
            pendingOpen = new DispatcherTimer(DispatcherPriority.Input, list.Dispatcher) { Interval = DoubleClickInterval() };
            pendingOpen.Tick += (_, _) => { CancelPendingOpen(); Post(before); };
            pendingOpen.Start();
        }

        private void OnWindowMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (list.SelectedItems.Count == 0 || !list.IsVisible || opening) return;
            var origin = args.OriginalSource as DependencyObject;
            // Clique num popup (lista aberta de um combo, filtros) chega pela árvore lógica, mas vem de outra fonte de apresentação: não é da tela.
            if (origin is not null && sender is Visual root && PresentationSource.FromDependencyObject(origin) is { } source && !ReferenceEquals(source, PresentationSource.FromVisual(root))) return;
            if (KeepsSelection(origin)) return;
            ClearSelection();
        }

        /// <summary>Um clique numa linha da lista, no balão ou num controle que a pessoa opera (botão, campo de texto, combo, barra de rolagem, menu) mantém a seleção; só o espaço passivo a desfaz.</summary>
        private bool KeepsSelection(DependencyObject? origin)
        {
            for (var current = origin; current is not null; current = ParentOf(current))
            {
                switch (current)
                {
                    case ListBoxItem row when ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), list):
                    case SelectionBalloon:
                    case ButtonBase:
                    case TextBoxBase:
                    case ScrollBar:
                    case Popup:
                    case ComboBox:
                    case DatePicker:
                    case ContextMenu:
                        return true;
                }
            }
            return false;
        }

        /// <summary>Pai visual, ou lógico quando não há visual (o <c>Run</c> de um texto e o conteúdo de um popup só se ligam à tela pela árvore lógica).</summary>
        private static DependencyObject? ParentOf(DependencyObject element) =>
            element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

        private object? ItemUnder(DependencyObject? origin)
        {
            if (origin is null || ItemsControl.ContainerFromElement(list, origin) is not ListBoxItem container) return null;
            var item = list.ItemContainerGenerator.ItemFromContainer(container);
            return ReferenceEquals(item, DependencyProperty.UnsetValue) ? null : item;
        }

        private void RestoreSelection(IReadOnlyList<object> items)
        {
            restoring = true;
            try
            {
                list.SelectedItems.Clear();
                foreach (var item in items) list.SelectedItems.Add(item);
            }
            finally { restoring = false; }
            RebuildKeys();
            Refresh();
        }

        private void CancelPendingOpen()
        {
            pendingOpen?.Stop();
            pendingOpen = null;
        }

        // ----- ficha -----

        /// <summary>Abre depois que o evento do mouse ou do teclado termina de percorrer a árvore (a ficha é modal e não pode prender o clique pela metade).</summary>
        private void Post(IReadOnlyList<object> queue) => list.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _ = OpenQueueAsync(queue));

        /// <summary>Uma ficha por vez: disparar todas de uma vez empilharia modais. Cada uma espera a anterior fechar.</summary>
        private async Task OpenQueueAsync(IReadOnlyList<object> queue)
        {
            if (opening || queue.Count == 0 || !started) return;
            opening = true;
            try
            {
                foreach (var queued in queue)
                {
                    // A lista pode ter sido recriada enquanto a ficha anterior estava aberta: abre o item atual de mesma chave; o que saiu da lista fica de fora.
                    if (!started || !list.IsLoaded || FindCurrent(queued) is not { } item) continue;
                    var handler = GetOpenHandler(list);
                    if (handler is not null) await handler(item);
                    else if (Window.GetWindow(list) is ShellWindow shell) await shell.OpenItemDetailsAsync(item);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (Window.GetWindow(list) is ShellWindow shell) shell.ViewModel.Workspace.ReportUnexpected(error);
                else System.Diagnostics.Trace.TraceError("NORA: a ficha da listagem falhou: {0}", error);
            }
            finally { opening = false; }
        }

        private object? FindCurrent(object item)
        {
            if (IsInItems(item)) return item;
            var key = KeyOf(item);
            return list.Items.Cast<object>().FirstOrDefault(candidate => Equals(KeyOf(candidate), key));
        }

        // ----- itens -----

        private bool IsInItems(object item) => list.Items.Cast<object>().Any(candidate => ReferenceEquals(candidate, item));

        private object KeyOf(object item) => ReadProperty(item, GetKeyPath(list)) ?? item;

        private string LabelOf(object item) => ReadProperty(item, GetLabelPath(list))?.ToString() is { Length: > 0 } text ? text : item.ToString() ?? "";

        private static object? ReadProperty(object item, string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var property = Properties.GetOrAdd((item.GetType(), path), key => key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance));
            return property?.GetValue(item);
        }
    }
}
