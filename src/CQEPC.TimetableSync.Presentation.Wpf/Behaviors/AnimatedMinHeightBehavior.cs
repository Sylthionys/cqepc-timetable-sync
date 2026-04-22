using System.Windows;
using System.Windows.Media.Animation;

namespace CQEPC.TimetableSync.Presentation.Wpf.Behaviors;

public static class AnimatedMinHeightBehavior
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value",
        typeof(double),
        typeof(AnimatedMinHeightBehavior),
        new PropertyMetadata(0d, HandleValueChanged));

    public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

    public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);

    private static void HandleValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        var nextMinHeight = (double)args.NewValue;
        if (!element.IsLoaded)
        {
            element.MinHeight = nextMinHeight;
            return;
        }

        var currentHeight = element.ActualHeight;
        if (currentHeight <= 0d || Math.Abs(currentHeight - nextMinHeight) < 1d)
        {
            element.BeginAnimation(FrameworkElement.HeightProperty, null);
            element.ClearValue(FrameworkElement.HeightProperty);
            element.MinHeight = nextMinHeight;
            return;
        }

        element.BeginAnimation(FrameworkElement.HeightProperty, null);
        element.MinHeight = 0d;
        element.Height = currentHeight;

        var animation = new DoubleAnimation
        {
            From = currentHeight,
            To = nextMinHeight,
            Duration = TimeSpan.FromMilliseconds(170),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut,
            },
            FillBehavior = FillBehavior.Stop,
        };

        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(FrameworkElement.HeightProperty, null);
            element.ClearValue(FrameworkElement.HeightProperty);
            element.MinHeight = nextMinHeight;
        };

        element.BeginAnimation(FrameworkElement.HeightProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
