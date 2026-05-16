using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Shell;

public partial class ShellWindow : System.Windows.Window
{
    private const double ExpandedSidebarWidth = 148d;
    private const double CollapsedSidebarWidth = 72d;
    private const double ExpandedSidebarGap = 10d;
    private const double CollapsedSidebarGap = 8d;
    private const double SettingsSubNavWidth = 126d;
    private const double CompactSettingsSubNavWidth = 112d;
    private const double SettingsSubNavGap = 8d;
    private const double CompactShellWidth = 1180d;
    private const double PreferredWindowAspectRatio = 1380d / 900d;
    private const int WmSizing = 0x0214;
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszTopRight = 5;
    private const int WmszBottom = 6;
    private const int WmszBottomLeft = 7;
    private const int WmszBottomRight = 8;
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaUseImmersiveDarkModeLegacy = 19;
    private const int DwmaBorderColor = 34;
    private const int DwmaCaptionColor = 35;
    private const int DwmaTextColor = 36;

    private ShellViewModel? subscribedViewModel;
    private Testing.UiWindowMode appliedWindowMode;
    private bool backgroundWindowConfigured;
    private HwndSource? windowSource;
    private ThemeMode titleBarTheme = ThemeMode.Light;
    private bool hasPreparedThemeTransition;
    private bool shellColumnUpdateQueued;

    public ShellWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContextChanged += HandleDataContextChanged;
        DataContext = viewModel;
        Loaded += (_, _) => UpdateSidebarWidth(animate: false);
        SizeChanged += (_, _) => UpdateShellColumns(animate: false);
        SourceInitialized += HandleWindowSourceInitialized;
        Closed += (_, _) => DetachFromViewModel();
    }

    internal void ApplyUiWindowMode(int width, int height, Testing.UiWindowMode windowMode)
    {
        appliedWindowMode = windowMode;
        Testing.UiWindowModeConfigurator.ApplyPresentation(this, width, height, windowMode);

        if (windowMode == Testing.UiWindowMode.Background && PresentationSource.FromVisual(this) is not null)
        {
            ApplyBackgroundWindowHandleStyles();
        }

        ApplyNativeTitleBarTheme();
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
        if (string.Equals(e.PropertyName, nameof(ShellViewModel.IsSidebarExpanded), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ShellViewModel.IsSettingsSelected), StringComparison.Ordinal))
        {
            QueueShellColumnUpdate();
            return;
        }

        if (string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentPageViewModel), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ShellViewModel.CurrentPage), StringComparison.Ordinal))
        {
            Dispatcher.Invoke(PlayPageTransition);
            return;
        }
    }

    private void QueueShellColumnUpdate()
    {
        if (shellColumnUpdateQueued)
        {
            return;
        }

        shellColumnUpdateQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                shellColumnUpdateQueued = false;
                UpdateShellColumns(animate: true);
            },
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private void UpdateSidebarWidth(bool animate)
    {
        UpdateShellColumns(animate);
    }

    private void UpdateShellColumns(bool animate)
    {
        var isExpanded = (DataContext as ShellViewModel)?.IsSidebarExpanded != false;
        var isSettingsSelected = (DataContext as ShellViewModel)?.IsSettingsSelected == true;
        var targetWidth = isExpanded ? ExpandedSidebarWidth : CollapsedSidebarWidth;
        var targetGap = isExpanded ? ExpandedSidebarGap : CollapsedSidebarGap;
        var targetSettingsWidth = isSettingsSelected ? GetSettingsSubNavWidth() : 0d;
        var targetSettingsGap = isSettingsSelected ? SettingsSubNavGap : 0d;

        SidebarColumn.Width = GridLength.Auto;
        SidebarGapColumn.Width = new GridLength(targetGap);
        SettingsSubNavColumn.Width = GridLength.Auto;
        SettingsSubNavGapColumn.Width = new GridLength(targetSettingsGap);

        if (!animate)
        {
            SidebarHost.BeginAnimation(WidthProperty, null);
            SettingsSubNavHost.BeginAnimation(WidthProperty, null);
            SidebarHost.Width = targetWidth;
            SettingsSubNavHost.Width = targetSettingsWidth;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimation(targetWidth, duration)
        {
            EasingFunction = easing,
        };
        var settingsAnimation = new DoubleAnimation(targetSettingsWidth, duration)
        {
            EasingFunction = easing,
        };

        SidebarHost.BeginAnimation(WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
        SettingsSubNavHost.BeginAnimation(WidthProperty, settingsAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private void PlayPageTransition()
    {
        if (!IsLoaded)
        {
            return;
        }

        PageHost.BeginAnimation(OpacityProperty, null);
        PageHostScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PageHostScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PageHostTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        PageHost.Opacity = 0;
        PageHostScale.ScaleX = 0.985;
        PageHostScale.ScaleY = 0.985;
        PageHostTranslate.Y = 18;

        var duration = TimeSpan.FromMilliseconds(280);
        PageHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } },
            HandoffBehavior.SnapshotAndReplace);
        PageHostScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } },
            HandoffBehavior.SnapshotAndReplace);
        PageHostScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } },
            HandoffBehavior.SnapshotAndReplace);
        PageHostTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void HandleNavigationButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject root)
        {
            return;
        }

        var icon = FindAnimatedNavigationIcon(root);
        if (icon is null)
        {
            return;
        }

        PlayNavigationIconAnimation(icon);
    }

    private static FrameworkElement? FindAnimatedNavigationIcon(DependencyObject root)
    {
        if (root is FrameworkElement element
            && element.Tag is string tag
            && tag.StartsWith("AnimatedNavIcon.", StringComparison.Ordinal))
        {
            return element;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var match = FindAnimatedNavigationIcon(VisualTreeHelper.GetChild(root, index));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static void PlayNavigationIconAnimation(FrameworkElement icon)
    {
        var tag = icon.Tag as string ?? string.Empty;
        var transforms = EnsureNavigationIconTransforms(icon);
        icon.RenderTransformOrigin = new Point(0.5, 0.5);

        transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transforms.Rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        transforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        transforms.Translate.BeginAnimation(TranslateTransform.YProperty, null);
        icon.BeginAnimation(OpacityProperty, null);

        transforms.Scale.ScaleX = 1;
        transforms.Scale.ScaleY = 1;
        transforms.Rotate.Angle = 0;
        transforms.Translate.X = 0;
        transforms.Translate.Y = 0;
        icon.Opacity = 1;

        PlayNavigationIconPartAnimation(icon, tag);
    }

    private static void PlayNavigationIconPartAnimation(FrameworkElement icon, string tag)
    {
        if (tag.EndsWith(".Home", StringComparison.Ordinal))
        {
            AnimateTranslateY(FindTaggedElement(icon, "NavHome.Roof"), -3.4, 0, 330, 34, back: true);
            AnimateOpacityPulse(FindTaggedElement(icon, "NavHome.Door"), 1, 0.46, 140);
            AnimateOpacityPulse(FindTaggedElement(icon, "NavHome.Today"), 1, 0.5, 190);
            return;
        }

        if (tag.EndsWith(".Import", StringComparison.Ordinal))
        {
            AnimateTranslateY(FindTaggedElement(icon, "NavImport.Arrow"), -4.8, 0.8, 230, 32, back: true);
            AnimateTranslateY(FindTaggedElement(icon, "NavImport.Tray"), 1.4, 0, 240, 170);
            AnimateOpacity(FindTaggedElement(icon, "NavImport.Tray"), 0.45, 1, 160, 150);
            return;
        }

        if (tag.EndsWith(".Settings", StringComparison.Ordinal))
        {
            AnimateRotatePulse(FindTaggedElement(icon, "NavSettings.Gear"), 72, 20);
            AnimateScalePulse(FindTaggedElement(icon, "NavSettings.Knob"), 1.45, 130);
            return;
        }

        if (tag.EndsWith(".Files", StringComparison.Ordinal))
        {
            AnimateTranslateX(FindTaggedElement(icon, "NavFiles.Page"), -3.6, 0, 280, 35, back: true);
            AnimateTranslateY(FindTaggedElement(icon, "NavFiles.Folder"), 1.1, 0, 230, 120);
            return;
        }

        if (tag.EndsWith(".Timetable", StringComparison.Ordinal))
        {
            AnimateTranslateY(FindTaggedElement(icon, "NavTimetable.Bar1"), -1.5, 0, 230, 35, back: true);
            AnimateTranslateY(FindTaggedElement(icon, "NavTimetable.Bar2"), -1.1, 0, 230, 115, back: true);
            AnimateOpacityPulse(FindTaggedElement(icon, "NavTimetable.Dot"), 1, 0.68, 170);
            return;
        }

        if (tag.EndsWith(".Connections", StringComparison.Ordinal)
            || tag.EndsWith(".ProviderGoogle", StringComparison.Ordinal)
            || tag.EndsWith(".ProviderMicrosoft", StringComparison.Ordinal))
        {
            AnimateRotatePulse(FindTaggedElement(icon, "NavConnections.Google.Orbit"), 56, 20);
            AnimateScalePulse(FindTaggedElement(icon, "NavConnections.Google.Node"), 1.55, 120);
            AnimateTranslateX(FindTaggedElement(icon, "NavConnections.Microsoft.Panel1"), -1.8, 0, 250, 45);
            AnimateTranslateX(FindTaggedElement(icon, "NavConnections.Microsoft.Panel2"), 1.8, 0, 250, 120);
            return;
        }

        if (tag.EndsWith(".Program", StringComparison.Ordinal))
        {
            AnimateTranslateX(FindTaggedElement(icon, "NavProgram.Knob1"), -1.8, 2, 240, 40, back: true);
            AnimateTranslateX(FindTaggedElement(icon, "NavProgram.Knob2"), 2, -1.8, 240, 130, back: true);
        }
    }

    private static FrameworkElement? FindTaggedElement(DependencyObject root, string tag)
    {
        if (root is FrameworkElement element
            && string.Equals(element.Tag as string, tag, StringComparison.Ordinal))
        {
            return element;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var match = FindTaggedElement(VisualTreeHelper.GetChild(root, index), tag);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static void AnimateTranslateY(FrameworkElement? element, double from, double to, int durationMilliseconds, int delayMilliseconds = 0, bool back = false)
    {
        if (element is null)
        {
            return;
        }

        var transforms = EnsureNavigationIconTransforms(element);
        transforms.Translate.BeginAnimation(
            TranslateTransform.YProperty,
            CreateDoubleAnimation(from, to, durationMilliseconds, delayMilliseconds, back),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateTranslateX(FrameworkElement? element, double from, double to, int durationMilliseconds, int delayMilliseconds = 0, bool back = false)
    {
        if (element is null)
        {
            return;
        }

        var transforms = EnsureNavigationIconTransforms(element);
        transforms.Translate.BeginAnimation(
            TranslateTransform.XProperty,
            CreateDoubleAnimation(from, to, durationMilliseconds, delayMilliseconds, back),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateOpacity(FrameworkElement? element, double from, double to, int durationMilliseconds, int delayMilliseconds = 0)
    {
        element?.BeginAnimation(
            OpacityProperty,
            CreateDoubleAnimation(from, to, durationMilliseconds, delayMilliseconds, back: false),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateOpacityPulse(FrameworkElement? element, double peak, double rest, int delayMilliseconds)
    {
        if (element is null)
        {
            return;
        }

        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMilliseconds + 135)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMilliseconds + 360)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        element.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateScalePulse(FrameworkElement? element, double peak, int delayMilliseconds)
    {
        if (element is null)
        {
            return;
        }

        var transforms = EnsureNavigationIconTransforms(element);
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMilliseconds + 150)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMilliseconds + 380)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation.Clone(), HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateRotatePulse(FrameworkElement? element, double peak, int delayMilliseconds)
    {
        if (element is null)
        {
            return;
        }

        var transforms = EnsureNavigationIconTransforms(element);
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMilliseconds + 220)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMilliseconds + 460)), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 }));
        transforms.Rotate.BeginAnimation(RotateTransform.AngleProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateDoubleAnimation(double from, double to, int durationMilliseconds, int delayMilliseconds, bool back)
    {
        return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            EasingFunction = back
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
    }

    private static NavigationIconTransforms EnsureNavigationIconTransforms(FrameworkElement icon)
    {
        icon.RenderTransformOrigin = new Point(0.5, 0.5);

        if (icon.RenderTransform is TransformGroup group && !group.IsFrozen)
        {
            var scale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
            var rotate = group.Children.OfType<RotateTransform>().FirstOrDefault();
            var translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (scale is not null && rotate is not null && translate is not null)
            {
                return new NavigationIconTransforms(scale, rotate, translate);
            }
        }

        var transforms = new NavigationIconTransforms(
            new ScaleTransform(1, 1),
            new RotateTransform(0),
            new TranslateTransform(0, 0));
        icon.RenderTransform = new TransformGroup
        {
            Children =
            {
                transforms.Scale,
                transforms.Rotate,
                transforms.Translate,
            },
        };
        return transforms;
    }

    private double GetSettingsSubNavWidth() =>
        ActualWidth > 0 && ActualWidth <= CompactShellWidth
            ? CompactSettingsSubNavWidth
            : SettingsSubNavWidth;

    private void DetachFromViewModel()
    {
        if (subscribedViewModel is null)
        {
            return;
        }

        subscribedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        subscribedViewModel = null;
    }

    private void HandleWindowSourceInitialized(object? sender, EventArgs e)
    {
        windowSource = PresentationSource.FromVisual(this) as HwndSource;
        windowSource?.AddHook(WindowProc);

        if (appliedWindowMode == Testing.UiWindowMode.Background)
        {
            ApplyBackgroundWindowHandleStyles();
        }

        ApplyNativeTitleBarTheme();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSizing
            && appliedWindowMode == Testing.UiWindowMode.Normal
            && WindowState == WindowState.Normal
            && lParam != IntPtr.Zero)
        {
            var rect = Marshal.PtrToStructure<RectNative>(lParam);
            ConstrainSizingRect(wParam.ToInt32(), ref rect);
            Marshal.StructureToPtr(rect, lParam, fDeleteOld: false);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ConstrainSizingRect(int edge, ref RectNative rect)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var minWidth = Math.Max(1, (int)Math.Round(MinWidth * dpi.DpiScaleX));
        var minHeight = Math.Max(1, (int)Math.Round(MinHeight * dpi.DpiScaleY));

        var width = Math.Max(rect.Width, minWidth);
        var height = Math.Max(rect.Height, minHeight);

        switch (edge)
        {
            case WmszLeft:
            case WmszRight:
                height = Math.Max(minHeight, (int)Math.Round(width / PreferredWindowAspectRatio));
                rect.Bottom = rect.Top + height;
                break;
            case WmszTop:
            case WmszBottom:
                width = Math.Max(minWidth, (int)Math.Round(height * PreferredWindowAspectRatio));
                rect.Right = rect.Left + width;
                break;
            case WmszTopLeft:
            case WmszTopRight:
            case WmszBottomLeft:
            case WmszBottomRight:
                var widthDrivenHeight = (int)Math.Round(width / PreferredWindowAspectRatio);
                var heightDrivenWidth = (int)Math.Round(height * PreferredWindowAspectRatio);

                if (widthDrivenHeight >= height)
                {
                    height = Math.Max(minHeight, widthDrivenHeight);
                }
                else
                {
                    width = Math.Max(minWidth, heightDrivenWidth);
                }

                if (edge is WmszTopLeft or WmszBottomLeft)
                {
                    rect.Left = rect.Right - width;
                }
                else
                {
                    rect.Right = rect.Left + width;
                }

                if (edge is WmszTopLeft or WmszTopRight)
                {
                    rect.Top = rect.Bottom - height;
                }
                else
                {
                    rect.Bottom = rect.Top + height;
                }

                break;
            default:
                break;
        }
    }

    internal Testing.UiWindowMode AppliedUiWindowMode => appliedWindowMode;

    internal bool IsBackgroundWindowConfigured => backgroundWindowConfigured;

    internal void UpdateTitleBarTheme(ThemeMode themeMode)
    {
        titleBarTheme = themeMode;
        ApplyNativeTitleBarTheme();
    }

    internal NativeTitleBarThemeState GetNativeTitleBarThemeState()
    {
        var palette = GetNativeTitleBarPalette(titleBarTheme);
        return new NativeTitleBarThemeState(
            titleBarTheme.ToString(),
            ToHex(palette.CaptionColor),
            ToHex(palette.TextColor),
            ToHex(palette.BorderColor));
    }

    private void ApplyBackgroundWindowHandleStyles()
    {
        Testing.UiWindowModeConfigurator.ApplyBackgroundHandleStyles(this);
        backgroundWindowConfigured = true;
    }

    public void PrepareThemeTransition(ThemeMode newTheme)
    {
        if (!IsLoaded)
        {
            return;
        }

        ResetThemeTransitionAnimations();
        hasPreparedThemeTransition = true;
        ThemeTransitionCanvas.Background = Brushes.Transparent;
        ThemeTransitionCanvas.Width = Math.Max(ActualWidth, 1d);
        ThemeTransitionCanvas.Height = Math.Max(ActualHeight, 1d);
        ThemeTransitionCanvas.Opacity = 0;
    }

    public void PlayThemeTransition(ThemeMode newTheme)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!hasPreparedThemeTransition)
        {
            PrepareThemeTransition(newTheme);
        }

        FinishThemeTransition();
    }

    public void PlayThemeTransition(Point screenPoint)
    {
        if (!IsLoaded)
        {
            return;
        }

        FinishThemeTransition();
    }

    private void ResetThemeTransitionAnimations()
    {
        ThemeTransitionCanvas.BeginAnimation(OpacityProperty, null);
    }

    private void FinishThemeTransition()
    {
        ResetThemeTransitionAnimations();
        ThemeTransitionCanvas.Opacity = 0;
        ThemeTransitionCanvas.Background = Brushes.Transparent;
        hasPreparedThemeTransition = false;
    }

    private void ApplyNativeTitleBarTheme()
    {
        if (windowSource?.Handle is not nint handle || handle == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return;
        }

        var palette = GetNativeTitleBarPalette(titleBarTheme);

        var immersiveDarkMode = palette.UseImmersiveDarkMode ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmaUseImmersiveDarkMode, ref immersiveDarkMode, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(handle, DwmaUseImmersiveDarkModeLegacy, ref immersiveDarkMode, Marshal.SizeOf<int>());

        var captionColor = ToColorRef(palette.CaptionColor);
        var textColor = ToColorRef(palette.TextColor);
        var borderColor = ToColorRef(palette.BorderColor);
        _ = DwmSetWindowAttribute(handle, DwmaCaptionColor, ref captionColor, Marshal.SizeOf<uint>());
        _ = DwmSetWindowAttribute(handle, DwmaTextColor, ref textColor, Marshal.SizeOf<uint>());
        _ = DwmSetWindowAttribute(handle, DwmaBorderColor, ref borderColor, Marshal.SizeOf<uint>());
    }

    private static uint ToColorRef(Color color) =>
        (uint)(color.R | (color.G << 8) | (color.B << 16));

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static NativeTitleBarPalette GetNativeTitleBarPalette(ThemeMode themeMode) =>
        themeMode == ThemeMode.Dark
            ? new NativeTitleBarPalette(
                Color.FromRgb(0x12, 0x1B, 0x26),
                Color.FromRgb(0xF4, 0xF8, 0xFC),
                Color.FromRgb(0x35, 0x48, 0x5E),
                true)
            : new NativeTitleBarPalette(
                Color.FromRgb(0xE7, 0xEF, 0xF9),
                Color.FromRgb(0x16, 0x1C, 0x24),
                Color.FromRgb(0xC9, 0xD7, 0xE8),
                false);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    private readonly record struct NativeTitleBarPalette(
        Color CaptionColor,
        Color TextColor,
        Color BorderColor,
        bool UseImmersiveDarkMode);

    private readonly record struct NavigationIconTransforms(
        ScaleTransform Scale,
        RotateTransform Rotate,
        TranslateTransform Translate);

    internal readonly record struct NativeTitleBarThemeState(
        string ThemeMode,
        string CaptionColorHex,
        string TextColorHex,
        string BorderColorHex);
}
