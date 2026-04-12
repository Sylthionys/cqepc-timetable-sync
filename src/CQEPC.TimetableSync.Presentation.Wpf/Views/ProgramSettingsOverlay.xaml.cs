using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CQEPC.TimetableSync.Presentation.Wpf.Views;

public partial class ProgramSettingsOverlay : System.Windows.Controls.UserControl
{
    public ProgramSettingsOverlay()
    {
        InitializeComponent();
    }

    private void ThemeToggleButton_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyThemeToggleVisualState(animate: false);
    }

    private void ThemeToggleButton_OnChecked(object sender, RoutedEventArgs e)
    {
        ApplyThemeToggleVisualState(animate: true);
    }

    private void ThemeToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
    {
        ApplyThemeToggleVisualState(animate: true);
    }

    private void ApplyThemeToggleVisualState(bool animate)
    {
        var isDark = ThemeToggleButton.IsChecked == true;
        AnimateIconState(ThemeSunIcon, isDark ? 0d : 1d, isDark ? 0.82 : 1d, isDark ? 2d : 0d, isDark ? -2d : 0d, animate);
        AnimateIconState(ThemeMoonIcon, isDark ? 1d : 0d, isDark ? 1d : 0.82, isDark ? 0d : -2d, isDark ? 0d : 2d, animate);
    }

    private static void AnimateIconState(TextBlock icon, double opacity, double scale, double translateX, double translateY, bool animate)
    {
        if (icon.RenderTransform is not TransformGroup group
            || group.Children.Count < 2
            || group.Children[0] is not ScaleTransform scaleTransform
            || group.Children[1] is not TranslateTransform translateTransform)
        {
            return;
        }

        if (!animate)
        {
            icon.Opacity = opacity;
            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
            translateTransform.X = translateX;
            translateTransform.Y = translateY;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        icon.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = easing });
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateX, duration) { EasingFunction = easing });
        translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateY, duration) { EasingFunction = easing });
    }
}
