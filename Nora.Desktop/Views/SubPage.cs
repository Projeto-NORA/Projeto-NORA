using System.Windows;
using System.Windows.Input;

namespace Nora.Desktop.Views;

/// <summary>
/// Subabas da barra lateral: a janela define <c>SubPage.Current</c> (herdado pela árvore) e cada parte de uma página declara em
/// <c>SubPage.Key</c> as subabas em que aparece ("list" ou "list,register"). Sem subaba aberta (Current vazio), tudo aparece,
/// o que mantém as telas usáveis fora da janela principal (testes e prévias).
/// </summary>
public static class SubPage
{
    public static readonly DependencyProperty CurrentProperty = DependencyProperty.RegisterAttached("Current", typeof(string), typeof(SubPage),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnChanged));
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached("Key", typeof(string), typeof(SubPage),
        new PropertyMetadata(null, OnChanged));

    public static string? GetCurrent(DependencyObject element) => (string?)element.GetValue(CurrentProperty);
    public static void SetCurrent(DependencyObject element, string? value) => element.SetValue(CurrentProperty, value);
    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    /// <summary>A parte aparece na subaba atual?</summary>
    public static bool Matches(string? key, string? current) =>
        string.IsNullOrEmpty(key) || string.IsNullOrEmpty(current) || key.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(current);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not UIElement ui || GetKey(element) is not { } key) return;
        ui.Visibility = Matches(key, GetCurrent(element)) ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>Comandos compartilhados pelas telas e tratados pela janela principal.</summary>
public static class ChartCommands
{
    /// <summary>Abre o gráfico (Linha / Pizza) do cartão de indicador. Parâmetro: o <c>MetricCard</c> ou a chave do indicador.</summary>
    public static RoutedUICommand Show { get; } = new("Ver gráfico", "ShowChart", typeof(ChartCommands));
}
