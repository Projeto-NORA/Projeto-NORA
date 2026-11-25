using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Nora.Desktop.Services;

namespace Nora.Desktop.Views;

/// <summary>Diálogo da marca criado pelo <see cref="DialogService"/>; não é aberto diretamente pelas telas.</summary>
public partial class NoraDialog : Window
{
    public DialogRequest Request { get; }

    public NoraDialog(DialogRequest request)
    {
        Request = request;
        InitializeComponent();
        Title = request.Title;
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        MessageText.Visibility = string.IsNullOrWhiteSpace(request.Message) ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = request.Primary;
        PrimaryButton.ToolTip = request.Secondary is null ? "Fecha este aviso." : "Confirma e continua.";
        SecondaryButton.Content = request.Secondary ?? "";
        SecondaryButton.ToolTip = "Fecha sem fazer nada.";
        SecondaryButton.Visibility = request.Secondary is null ? Visibility.Collapsed : Visibility.Visible;
        // Sem botão secundário, Esc fecha pelo primário (aviso apenas informativo).
        if (request.Secondary is null) { SecondaryButton.IsCancel = false; PrimaryButton.IsCancel = true; }
        var (glyph, foreground, surface) = request.Kind switch
        {
            DialogKind.Success => ("", "SuccessBrush", "SuccessSurfaceBrush"),
            DialogKind.Warning => ("", "WarningBrush", "WarningSurfaceBrush"),
            DialogKind.Error => ("", "ErrorBrush", "ErrorSurfaceBrush"),
            DialogKind.Question => ("", "AccentTextBrush", "InfoSurfaceBrush"),
            _ => ("", "InfoBrush", "InfoSurfaceBrush")
        };
        IconGlyph.Text = glyph;
        IconGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, foreground);
        IconCircle.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, surface);
        System.Windows.Automation.AutomationProperties.SetName(this, request.Title);
        Loaded += (_, _) =>
        {
            Frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Zoom.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            Zoom.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            PrimaryButton.Focus();
        };
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnSecondary(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
}
