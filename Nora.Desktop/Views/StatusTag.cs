using System.Windows;
using System.Windows.Controls;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>
/// Etiqueta arredondada de situação (Agendada, Concluída, Falta, Ativo, Desativado…), no tom do tema. O estado nunca depende só da cor:
/// o texto sempre diz a situação. Sem texto, a etiqueta some. O desenho está em <c>Controls.xaml</c> (estilo implícito do tipo).
/// </summary>
public sealed class StatusTag : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusTag), new PropertyMetadata(""));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(Tone), typeof(StatusTag), new PropertyMetadata(Tone.Neutral));

    static StatusTag() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusTag), new FrameworkPropertyMetadata(typeof(StatusTag)));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value ?? ""); }
    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}
