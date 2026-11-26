using System.Runtime.CompilerServices;
using System.Windows;

namespace Nora.Desktop.Views;

/// <summary>
/// Layout da tela dentro da janela principal. <c>PageLayout.Fill="True"</c> numa tela, ou numa subtela (junto de <see cref="SubPage.Key"/>), faz a área de
/// conteúdo entregar a ela a altura inteira e desligar a rolagem externa: a listagem rola sozinha e o balão de ações fica no pé da tela. Vale enquanto o
/// elemento está visível; telas sem Fill continuam rolando como sempre. Fora da janela principal (prévias e testes) não tem efeito.
/// </summary>
public static class PageLayout
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached("Fill", typeof(bool), typeof(PageLayout), new PropertyMetadata(false, OnFillChanged));

    private static readonly ConditionalWeakTable<FrameworkElement, Registration> Registrations = new();

    public static bool GetFill(DependencyObject element) => (bool)element.GetValue(FillProperty);
    public static void SetFill(DependencyObject element, bool value) => element.SetValue(FillProperty, value);

    private static void OnFillChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement page) return;
        if (args.NewValue is true) Registrations.GetValue(page, owner => new Registration(owner)).Enable();
        else if (Registrations.TryGetValue(page, out var registration)) registration.Disable();
    }

    /// <summary>Liga o elemento à janela principal enquanto ele está na tela.</summary>
    private sealed class Registration(FrameworkElement page)
    {
        private ShellWindow? shell;
        private bool enabled;

        public void Enable()
        {
            if (enabled) return;
            enabled = true;
            page.Loaded += OnLoaded;
            page.Unloaded += OnUnloaded;
            page.IsVisibleChanged += OnVisibleChanged;
            if (page.IsLoaded) Connect();
        }

        public void Disable()
        {
            if (!enabled) return;
            enabled = false;
            page.Loaded -= OnLoaded;
            page.Unloaded -= OnUnloaded;
            page.IsVisibleChanged -= OnVisibleChanged;
            Disconnect();
        }

        private void Connect()
        {
            shell = Window.GetWindow(page) as ShellWindow;
            shell?.RegisterFillPage(page);
        }

        private void Disconnect()
        {
            shell?.UnregisterFillPage(page);
            shell = null;
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => Connect();
        private void OnUnloaded(object sender, RoutedEventArgs args) => Disconnect();
        private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs args) => shell?.UpdatePageFill();
    }
}
