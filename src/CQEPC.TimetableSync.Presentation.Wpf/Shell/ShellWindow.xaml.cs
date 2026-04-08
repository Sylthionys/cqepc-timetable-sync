using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Shell;

public partial class ShellWindow : System.Windows.Window
{
    private const double ExpandedSidebarWidth = 148d;
    private const double CollapsedSidebarWidth = 72d;
    private const double ExpandedSidebarGap = 10d;
    private const double CollapsedSidebarGap = 8d;
    private ShellViewModel? subscribedViewModel;
    private Testing.UiWindowMode appliedWindowMode;
    private bool backgroundWindowConfigured;

    public ShellWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContextChanged += HandleDataContextChanged;
        DataContext = viewModel;
        Loaded += (_, _) => UpdateSidebarWidth(animate: false);
        Closed += (_, _) => DetachFromViewModel();
    }

    internal void ApplyUiWindowMode(int width, int height, Testing.UiWindowMode windowMode)
    {
        appliedWindowMode = windowMode;
        Testing.UiWindowModeConfigurator.ApplyPresentation(this, width, height, windowMode);

        switch (windowMode)
        {
            case Testing.UiWindowMode.Background:
                if (PresentationSource.FromVisual(this) is not null)
                {
                    ApplyBackgroundWindowHandleStyles();
                }
                else
                {
                    SourceInitialized -= HandleBackgroundUiSourceInitialized;
                    SourceInitialized += HandleBackgroundUiSourceInitialized;
                }
                break;
            case Testing.UiWindowMode.RenderOnly:
                break;
            case Testing.UiWindowMode.Normal:
            default:
                break;
        }
    }

    private void HandleDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromViewModel();
        subscribedViewModel = e.NewValue as ShellViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged += HandleViewModelPropertyChanged;
        }

        UpdateSidebarWidth(animate: false);
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ShellViewModel.IsSidebarExpanded), StringComparison.Ordinal))
        {
            return;
        }

        Dispatcher.Invoke(() => UpdateSidebarWidth(animate: true));
    }

    private void UpdateSidebarWidth(bool animate)
    {
        var isExpanded = (DataContext as ShellViewModel)?.IsSidebarExpanded != false;
        var targetWidth = isExpanded ? ExpandedSidebarWidth : CollapsedSidebarWidth;
        var targetGap = isExpanded ? ExpandedSidebarGap : CollapsedSidebarGap;

        SidebarColumn.Width = new GridLength(targetWidth);
        SidebarGapColumn.Width = new GridLength(targetGap);

        if (!animate)
        {
            SidebarHost.Width = targetWidth;
            return;
        }

        var animation = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        SidebarHost.BeginAnimation(WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void DetachFromViewModel()
    {
        if (subscribedViewModel is null)
        {
            return;
        }

        subscribedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        subscribedViewModel = null;
    }

    private void HandleBackgroundUiSourceInitialized(object? sender, EventArgs e)
    {
        ApplyBackgroundWindowHandleStyles();
    }

    internal Testing.UiWindowMode AppliedUiWindowMode => appliedWindowMode;

    internal bool IsBackgroundWindowConfigured => backgroundWindowConfigured;

    private void ApplyBackgroundWindowHandleStyles()
    {
        Testing.UiWindowModeConfigurator.ApplyBackgroundHandleStyles(this);
        backgroundWindowConfigured = true;
    }

    public void PlayThemeTransition(Point screenPoint)
    {
        if (!IsLoaded)
        {
            return;
        }

        var center = PointFromScreen(screenPoint);
        ThemeTransitionOrb.Fill = TryFindResource("WorkspaceBackgroundBrush") as Brush
            ?? TryFindResource("AccentSoftBrush") as Brush
            ?? Brushes.White;

        System.Windows.Controls.Canvas.SetLeft(ThemeTransitionOrb, center.X - (ThemeTransitionOrb.Width / 2d));
        System.Windows.Controls.Canvas.SetTop(ThemeTransitionOrb, center.Y - (ThemeTransitionOrb.Height / 2d));

        ThemeTransitionScale.ScaleX = 0.14;
        ThemeTransitionScale.ScaleY = 0.14;
        ThemeTransitionOrb.Opacity = 0.34;

        var radius = Math.Sqrt((ActualWidth * ActualWidth) + (ActualHeight * ActualHeight));
        var targetScale = Math.Max(8d, radius / (ThemeTransitionOrb.Width / 2d));

        var scaleAnimation = new DoubleAnimation
        {
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(560),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        var opacityAnimation = new DoubleAnimationUsingKeyFrames();
        opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0.34, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
        opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0.22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        opacityAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560))));

        ThemeTransitionScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
        ThemeTransitionScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
        ThemeTransitionOrb.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
    }
}
