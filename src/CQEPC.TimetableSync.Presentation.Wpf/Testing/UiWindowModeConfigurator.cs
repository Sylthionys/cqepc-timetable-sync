using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CQEPC.TimetableSync.Presentation.Wpf.Testing;

internal static class UiWindowModeConfigurator
{
    internal const int BackgroundWindowOffset = -10000;
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndBottom = new(1);

    public static void ApplyPresentation(Window window, int width, int height, UiWindowMode windowMode)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Width = width;
        window.Height = height;
        window.MinWidth = width;
        window.MaxWidth = width;
        window.MinHeight = height;
        window.MaxHeight = height;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;

        if (windowMode is UiWindowMode.Background or UiWindowMode.RenderOnly)
        {
            window.Left = BackgroundWindowOffset;
            window.Top = BackgroundWindowOffset;
        }
    }

    public static void ApplyBackgroundHandleStyles(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        ApplyPresentation(window, (int)window.Width, (int)window.Height, UiWindowMode.Background);

        var handle = new WindowInteropHelper(window).Handle;
        var currentStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var updatedStyle = new IntPtr(currentStyle | WsExNoActivate | WsExToolWindow);
        _ = SetWindowLongPtr(handle, GwlExStyle, updatedStyle);
        _ = SetWindowPos(
            handle,
            HwndBottom,
            BackgroundWindowOffset,
            BackgroundWindowOffset,
            (int)window.Width,
            (int)window.Height,
            SwpNoActivate);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
