using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Views;

public partial class ProgramSettingsOverlay : System.Windows.Controls.UserControl
{
    public ProgramSettingsOverlay()
    {
        InitializeComponent();
    }

    private void HandleGoogleTimeZonePopupOpened(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                ProgramGoogleTimeZoneSearchBox.UpdateLayout();
                ProgramGoogleTimeZoneSearchBox.Focus();
                Keyboard.Focus(ProgramGoogleTimeZoneSearchBox);
                ProgramGoogleTimeZoneSearchBox.CaretIndex = ProgramGoogleTimeZoneSearchBox.Text.Length;
            },
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HandleGoogleTimeZonePopupClosed(object sender, EventArgs e)
    {
        if (DataContext is ProgramSettingsOverlayViewModel { Workspace: not null } viewModel)
        {
            viewModel.Workspace.GoogleTimeZoneSearchText = string.Empty;
        }
    }

    private void HandleGoogleTimeZoneResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { IsKeyboardFocusWithin: false, IsMouseOver: false }
            || ProgramGoogleTimeZonePopup is not Popup { IsOpen: true })
        {
            return;
        }

        ProgramGoogleTimeZonePopup.IsOpen = false;
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
        AnimateIconState(ThemeSunIcon, isDark ? 0d : 1d, isDark ? 0.76 : 1d, isDark ? 8d : 0d, isDark ? 19d : 0d, animate);
        AnimateIconState(ThemeMoonIcon, isDark ? 1d : 0d, isDark ? 1d : 0.8, isDark ? 0d : -8d, isDark ? 0d : 18d, animate);
        AnimateToggleGlow(animate);
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

        var duration = TimeSpan.FromMilliseconds(430);
        var easing = new QuarticEase { EasingMode = EasingMode.EaseOut };
        icon.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = easing });
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateX, duration) { EasingFunction = easing });
        translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateY, duration) { EasingFunction = easing });
    }

    private void AnimateToggleGlow(bool animate)
    {
        if (ThemeToggleGlow.RenderTransform is not TransformGroup group
            || group.Children.Count < 2
            || group.Children[0] is not ScaleTransform scaleTransform
            || group.Children[1] is not TranslateTransform translateTransform)
        {
            return;
        }

        ThemeToggleGlow.BeginAnimation(OpacityProperty, null);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        translateTransform.BeginAnimation(TranslateTransform.YProperty, null);

        if (!animate)
        {
            ThemeToggleGlow.Opacity = 0;
            scaleTransform.ScaleX = 0.68;
            scaleTransform.ScaleY = 0.56;
            translateTransform.Y = 5;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.34, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))) { EasingFunction = easing });
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(440))) { EasingFunction = easing });

        ThemeToggleGlow.BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.68, 1.15, TimeSpan.FromMilliseconds(440)) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.56, 0.78, TimeSpan.FromMilliseconds(440)) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
        translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, -2, TimeSpan.FromMilliseconds(440)) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
    }
}
