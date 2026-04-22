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

    public ShellWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContextChanged += HandleDataContextChanged;
        DataContext = viewModel;
        Loaded += (_, _) => UpdateSidebarWidth(animate: false);
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

    internal readonly record struct NativeTitleBarThemeState(
        string ThemeMode,
        string CaptionColorHex,
        string TextColorHex,
        string BorderColorHex);
}
