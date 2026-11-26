using System.Windows;
using System.Windows.Media.Animation;

namespace Nora.Desktop.Views;

/// <summary>Anima uma largura de coluna em pixels (o WPF não traz animação para <see cref="GridLength"/>). Usada ao recolher e reabrir a barra lateral.</summary>
public sealed class GridLengthAnimation : AnimationTimeline
{
    // Propriedades de dependência (e não campos): o WPF clona a animação ao iniciá-la e só copia o que é DP.
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register(nameof(From), typeof(double), typeof(GridLengthAnimation), new PropertyMetadata(0d));
    public static readonly DependencyProperty ToProperty = DependencyProperty.Register(nameof(To), typeof(double), typeof(GridLengthAnimation), new PropertyMetadata(0d));
    public static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation));

    public double From { get => (double)GetValue(FromProperty); set => SetValue(FromProperty, value); }
    public double To { get => (double)GetValue(ToProperty); set => SetValue(ToProperty, value); }
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }

    public override Type TargetPropertyType => typeof(GridLength);

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress ?? 0;
        if (EasingFunction is { } easing) progress = easing.Ease(progress);
        return new GridLength(Math.Max(0, From + (To - From) * progress), GridUnitType.Pixel);
    }
}
