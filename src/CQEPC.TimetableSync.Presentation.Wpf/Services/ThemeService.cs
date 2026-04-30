using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

internal sealed class ThemeService : IThemeService
{
    private static readonly string[] SolidBrushKeys =
    [
        "WindowBackgroundBrush",
        "ShellBackgroundBrush",
        "WorkspaceBackgroundBrush",
        "SurfaceBrush",
        "SurfaceAltBrush",
        "MutedSurfaceBrush",
        "PanelBrush",
        "HighlightSurfaceBrush",
        "SidebarBackgroundBrush",
        "SidebarOverlayBrush",
        "AccentBrush",
        "AccentMutedBrush",
        "AccentSoftBrush",
        "AccentStrongBrush",
        "TextBrush",
        "SubtleTextBrush",
        "SuccessBrush",
        "SuccessSurfaceBrush",
        "WarningBrush",
        "DangerBrush",
        "DangerSurfaceBrush",
        "BorderBrush",
        "StrongBorderBrush",
        "DividerBrush",
        "CalendarGridBrush",
        "OverlayBackdropBrush",
        "SidebarCardBrush",
        "SidebarCardBorderBrush",
        "SidebarTextBrush",
        "SidebarMutedTextBrush",
        "SidebarCaptionBrush",
        "SidebarBadgeBrush",
        "InputChromeBrush",
        "PopupSurfaceBrush",
        "PopupAltSurfaceBrush",
        "PopupHighlightBrush",
        "PopupSelectedBrush",
        "WarningSurfaceBrush",
        "ImportPageBackgroundBrush",
        "ImportPageSurfaceBrush",
        "ImportPageSurfaceAltBrush",
        "ImportPageSurfaceMutedBrush",
        "ImportPageBorderBrush",
        "ImportPageTextBrush",
        "ImportPageSubtleTextBrush",
        "ImportPageTrackBrush",
        "ImportPageSelectionBrush",
        "ImportAddBrush",
        "ImportAddSurfaceBrush",
        "ImportUpdateBrush",
        "ImportUpdateSurfaceBrush",
        "ImportDeleteBrush",
        "ImportDeleteSurfaceBrush",
        "ImportConflictBrush",
        "ImportConflictSurfaceBrush",
        "ImportNeutralBrush",
        "ImportNeutralSurfaceBrush",
    ];

    private readonly ResourceDictionary resources;

    public ThemeService(ResourceDictionary resources)
    {
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
        EnsureMutableThemeResources();
    }

    public event EventHandler? ThemeChanged;

    public ThemeMode ActiveTheme { get; private set; } = ThemeMode.Light;

    public void ApplyTheme(ThemeMode themeMode)
    {
        var themeChanged = ActiveTheme != themeMode;
        var palette = themeMode == ThemeMode.Dark ? ThemePalette.Dark : ThemePalette.Light;

        SetBrushColor("WindowBackgroundBrush", palette.WindowBackground);
        SetGradient("ChromeBackgroundBrush", palette.ChromeStart, palette.ChromeEnd);
        SetBrushColor("ShellBackgroundBrush", palette.ShellBackground);
        SetBrushColor("WorkspaceBackgroundBrush", palette.WorkspaceBackground);
        SetBrushColor("SurfaceBrush", palette.Surface);
        SetBrushColor("SurfaceAltBrush", palette.SurfaceAlt);
        SetBrushColor("MutedSurfaceBrush", palette.MutedSurface);
        SetBrushColor("PanelBrush", palette.Panel);
        SetBrushColor("HighlightSurfaceBrush", palette.HighlightSurface);
        SetBrushColor("SidebarBackgroundBrush", palette.SidebarBackground);
        SetBrushColor("SidebarOverlayBrush", palette.SidebarOverlay);
        SetBrushColor("AccentBrush", palette.Accent);
        SetBrushColor("AccentMutedBrush", palette.AccentMuted);
        SetBrushColor("AccentSoftBrush", palette.AccentSoft);
        SetBrushColor("AccentStrongBrush", palette.AccentStrong);
        SetBrushColor("TextBrush", palette.Text);
        SetBrushColor("SubtleTextBrush", palette.SubtleText);
        SetBrushColor("SuccessBrush", palette.Success);
        SetBrushColor("SuccessSurfaceBrush", palette.SuccessSurface);
        SetBrushColor("WarningBrush", palette.Warning);
        SetBrushColor("DangerBrush", palette.Danger);
        SetBrushColor("DangerSurfaceBrush", palette.DangerSurface);
        SetBrushColor("BorderBrush", palette.Border);
        SetBrushColor("StrongBorderBrush", palette.StrongBorder);
        SetBrushColor("DividerBrush", palette.Divider);
        SetBrushColor("CalendarGridBrush", palette.CalendarGrid);
        SetBrushColor("OverlayBackdropBrush", palette.OverlayBackdrop);
        SetBrushColor("SidebarCardBrush", palette.SidebarCard);
        SetBrushColor("SidebarCardBorderBrush", palette.SidebarCardBorder);
        SetBrushColor("SidebarTextBrush", palette.SidebarText);
        SetBrushColor("SidebarMutedTextBrush", palette.SidebarMutedText);
        SetBrushColor("SidebarCaptionBrush", palette.SidebarCaption);
        SetBrushColor("SidebarBadgeBrush", palette.SidebarBadge);
        SetBrushColor("InputChromeBrush", palette.InputChrome);
        SetBrushColor("PopupSurfaceBrush", palette.PopupSurface);
        SetBrushColor("PopupAltSurfaceBrush", palette.PopupAltSurface);
        SetBrushColor("PopupHighlightBrush", palette.PopupHighlight);
        SetBrushColor("PopupSelectedBrush", palette.PopupSelected);
        SetBrushColor("WarningSurfaceBrush", palette.WarningSurface);
        SetBrushColor("ImportPageBackgroundBrush", palette.ImportPageBackground);
        SetBrushColor("ImportPageSurfaceBrush", palette.ImportPageSurface);
        SetBrushColor("ImportPageSurfaceAltBrush", palette.ImportPageSurfaceAlt);
        SetBrushColor("ImportPageSurfaceMutedBrush", palette.ImportPageSurfaceMuted);
        SetBrushColor("ImportPageBorderBrush", palette.ImportPageBorder);
        SetBrushColor("ImportPageTextBrush", palette.ImportPageText);
        SetBrushColor("ImportPageSubtleTextBrush", palette.ImportPageSubtleText);
        SetBrushColor("ImportPageTrackBrush", palette.ImportPageTrack);
        SetBrushColor("ImportPageSelectionBrush", palette.ImportPageSelection);
        SetBrushColor("ImportAddBrush", palette.ImportAdd);
        SetBrushColor("ImportAddSurfaceBrush", palette.ImportAddSurface);
        SetBrushColor("ImportUpdateBrush", palette.ImportUpdate);
        SetBrushColor("ImportUpdateSurfaceBrush", palette.ImportUpdateSurface);
        SetBrushColor("ImportDeleteBrush", palette.ImportDelete);
        SetBrushColor("ImportDeleteSurfaceBrush", palette.ImportDeleteSurface);
        SetBrushColor("ImportConflictBrush", palette.ImportConflict);
        SetBrushColor("ImportConflictSurfaceBrush", palette.ImportConflictSurface);
        SetBrushColor("ImportNeutralBrush", palette.ImportNeutral);
        SetBrushColor("ImportNeutralSurfaceBrush", palette.ImportNeutralSurface);

        ActiveTheme = themeMode;
        RefreshOpenWindows();
        if (themeChanged)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetBrushColor(string key, Color color)
    {
        if (resources[key] is not SolidColorBrush brush)
        {
            return;
        }

        if (brush.IsFrozen)
        {
            var replacement = brush.CloneCurrentValue();
            replacement.Color = color;
            resources[key] = replacement;
            return;
        }

        brush.Color = color;
    }

    private void SetGradient(string key, Color start, Color end)
    {
        if (resources[key] is not LinearGradientBrush brush)
        {
            return;
        }

        if (brush.GradientStops.Count < 2)
        {
            return;
        }

        if (brush.IsFrozen)
        {
            var replacement = brush.CloneCurrentValue();
            replacement.GradientStops[0].Color = start;
            replacement.GradientStops[1].Color = end;
            resources[key] = replacement;
            return;
        }

        brush.GradientStops[0].Color = start;
        brush.GradientStops[1].Color = end;
    }

    private void EnsureMutableThemeResources()
    {
        foreach (var key in SolidBrushKeys)
        {
            if (resources[key] is SolidColorBrush solidBrush && solidBrush.IsFrozen)
            {
                resources[key] = solidBrush.CloneCurrentValue();
            }
        }

        if (resources["ChromeBackgroundBrush"] is LinearGradientBrush gradientBrush && gradientBrush.IsFrozen)
        {
            resources["ChromeBackgroundBrush"] = gradientBrush.CloneCurrentValue();
        }
    }

    private sealed record ThemePalette(
        Color WindowBackground,
        Color ChromeStart,
        Color ChromeEnd,
        Color ShellBackground,
        Color WorkspaceBackground,
        Color Surface,
        Color SurfaceAlt,
        Color MutedSurface,
        Color Panel,
        Color HighlightSurface,
        Color SidebarBackground,
        Color SidebarOverlay,
        Color Accent,
        Color AccentMuted,
        Color AccentSoft,
        Color AccentStrong,
        Color Text,
        Color SubtleText,
        Color Success,
        Color SuccessSurface,
        Color Warning,
        Color Danger,
        Color DangerSurface,
        Color Border,
        Color StrongBorder,
        Color Divider,
        Color CalendarGrid,
        Color OverlayBackdrop,
        Color SidebarCard,
        Color SidebarCardBorder,
        Color SidebarText,
        Color SidebarMutedText,
        Color SidebarCaption,
        Color SidebarBadge,
        Color InputChrome,
        Color PopupSurface,
        Color PopupAltSurface,
        Color PopupHighlight,
        Color PopupSelected,
        Color WarningSurface,
        Color ImportPageBackground,
        Color ImportPageSurface,
        Color ImportPageSurfaceAlt,
        Color ImportPageSurfaceMuted,
        Color ImportPageBorder,
        Color ImportPageText,
        Color ImportPageSubtleText,
        Color ImportPageTrack,
        Color ImportPageSelection,
        Color ImportAdd,
        Color ImportAddSurface,
        Color ImportUpdate,
        Color ImportUpdateSurface,
        Color ImportDelete,
        Color ImportDeleteSurface,
        Color ImportConflict,
        Color ImportConflictSurface,
        Color ImportNeutral,
        Color ImportNeutralSurface)
    {
        public static ThemePalette Light { get; } = new(
            Color.FromRgb(0xF3, 0xF6, 0xFA),
            Color.FromRgb(0xF8, 0xFB, 0xFF),
            Color.FromRgb(0xE5, 0xED, 0xF8),
            Color.FromRgb(0xED, 0xF2, 0xF8),
            Color.FromRgb(0xF8, 0xFA, 0xFD),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xF4, 0xF7, 0xFB),
            Color.FromRgb(0xE8, 0xEE, 0xF7),
            Color.FromRgb(0xEF, 0xF4, 0xFA),
            Color.FromRgb(0xEA, 0xF2, 0xFF),
            Color.FromRgb(0xF0, 0xF4, 0xFA),
            Color.FromArgb(0xB0, 0xD8, 0xE3, 0xF4),
            Color.FromRgb(0x16, 0x68, 0xC7),
            Color.FromRgb(0xE0, 0xEC, 0xFF),
            Color.FromRgb(0xD4, 0xE2, 0xF6),
            Color.FromRgb(0x0F, 0x58, 0xA8),
            Color.FromRgb(0x16, 0x1C, 0x24),
            Color.FromRgb(0x59, 0x65, 0x79),
            Color.FromRgb(0x4E, 0x87, 0x65),
            Color.FromRgb(0xE9, 0xF5, 0xEC),
            Color.FromRgb(0xA0, 0x6C, 0x18),
            Color.FromRgb(0xB1, 0x47, 0x55),
            Color.FromRgb(0xFB, 0xEC, 0xEE),
            Color.FromRgb(0xD6, 0xDF, 0xEB),
            Color.FromRgb(0xB2, 0xC0, 0xD3),
            Color.FromRgb(0xE5, 0xEB, 0xF3),
            Color.FromRgb(0xD8, 0xE2, 0xEE),
            Color.FromArgb(0x66, 0x0B, 0x13, 0x20),
            Color.FromRgb(0xF8, 0xFB, 0xFF),
            Color.FromRgb(0xD7, 0xE0, 0xEB),
            Color.FromRgb(0x1B, 0x1F, 0x23),
            Color.FromRgb(0x5D, 0x68, 0x78),
            Color.FromRgb(0x7B, 0x87, 0x99),
            Color.FromRgb(0x16, 0x68, 0xC7),
            Color.FromRgb(0xF4, 0xF7, 0xFB),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xF7, 0xF9, 0xFC),
            Color.FromRgb(0xEA, 0xF2, 0xFF),
            Color.FromRgb(0xD9, 0xE8, 0xFF),
            Color.FromRgb(0xFE, 0xF4, 0xDD),
            Color.FromRgb(0xF4, 0xF7, 0xFB),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xF2, 0xF6, 0xFB),
            Color.FromRgb(0xE9, 0xF0, 0xF8),
            Color.FromRgb(0xD6, 0xE1, 0xED),
            Color.FromRgb(0x17, 0x21, 0x31),
            Color.FromRgb(0x61, 0x71, 0x85),
            Color.FromRgb(0xED, 0xF3, 0xFA),
            Color.FromRgb(0x16, 0x68, 0xC7),
            Color.FromRgb(0x2F, 0x8A, 0x56),
            Color.FromRgb(0xE8, 0xF4, 0xEC),
            Color.FromRgb(0xB3, 0x6A, 0x09),
            Color.FromRgb(0xFF, 0xF3, 0xE1),
            Color.FromRgb(0xC1, 0x4A, 0x57),
            Color.FromRgb(0xFC, 0xEB, 0xED),
            Color.FromRgb(0x7A, 0x55, 0xD3),
            Color.FromRgb(0xF2, 0xEC, 0xFF),
            Color.FromRgb(0x5D, 0x73, 0x89),
            Color.FromRgb(0xEA, 0xF0, 0xF6));

        public static ThemePalette Dark { get; } = new(
            Color.FromRgb(0x09, 0x10, 0x16),
            Color.FromRgb(0x12, 0x1B, 0x26),
            Color.FromRgb(0x1B, 0x29, 0x38),
            Color.FromRgb(0x0F, 0x17, 0x22),
            Color.FromRgb(0x11, 0x1B, 0x28),
            Color.FromRgb(0x17, 0x23, 0x32),
            Color.FromRgb(0x1D, 0x2C, 0x3E),
            Color.FromRgb(0x26, 0x38, 0x4C),
            Color.FromRgb(0x1A, 0x2D, 0x42),
            Color.FromRgb(0x22, 0x34, 0x48),
            Color.FromRgb(0x0D, 0x15, 0x1F),
            Color.FromArgb(0x96, 0x05, 0x0A, 0x10),
            Color.FromRgb(0x71, 0xB4, 0xFF),
            Color.FromRgb(0x24, 0x43, 0x63),
            Color.FromRgb(0x18, 0x30, 0x49),
            Color.FromRgb(0xD2, 0xEA, 0xFF),
            Color.FromRgb(0xF4, 0xF8, 0xFC),
            Color.FromRgb(0xC3, 0xCF, 0xDB),
            Color.FromRgb(0x88, 0xD4, 0xA0),
            Color.FromRgb(0x18, 0x2D, 0x23),
            Color.FromRgb(0xF0, 0xC3, 0x78),
            Color.FromRgb(0xF4, 0xA7, 0xAF),
            Color.FromRgb(0x3E, 0x20, 0x28),
            Color.FromRgb(0x41, 0x56, 0x6B),
            Color.FromRgb(0x66, 0x7D, 0x96),
            Color.FromRgb(0x2A, 0x3A, 0x4D),
            Color.FromRgb(0x35, 0x48, 0x5E),
            Color.FromArgb(0x9C, 0x03, 0x08, 0x0E),
            Color.FromRgb(0x13, 0x1F, 0x2D),
            Color.FromRgb(0x38, 0x4B, 0x60),
            Color.FromRgb(0xF4, 0xF8, 0xFC),
            Color.FromRgb(0xC3, 0xCF, 0xDB),
            Color.FromRgb(0x90, 0xA5, 0xBD),
            Color.FromRgb(0x71, 0xB4, 0xFF),
            Color.FromRgb(0x1A, 0x2A, 0x3B),
            Color.FromRgb(0x18, 0x28, 0x39),
            Color.FromRgb(0x20, 0x34, 0x48),
            Color.FromRgb(0x38, 0x5B, 0x7C),
            Color.FromRgb(0x4B, 0x82, 0xB6),
            Color.FromRgb(0x52, 0x3D, 0x18),
            Color.FromRgb(0x0D, 0x17, 0x25),
            Color.FromRgb(0x11, 0x1F, 0x31),
            Color.FromRgb(0x16, 0x28, 0x3D),
            Color.FromRgb(0x1A, 0x2E, 0x45),
            Color.FromRgb(0x27, 0x3A, 0x54),
            Color.FromRgb(0xF3, 0xF7, 0xFD),
            Color.FromRgb(0x8F, 0xA6, 0xC1),
            Color.FromRgb(0x0E, 0x1A, 0x29),
            Color.FromRgb(0x21, 0x5F, 0xD7),
            Color.FromRgb(0x67, 0xD3, 0x7E),
            Color.FromRgb(0x1A, 0x35, 0x28),
            Color.FromRgb(0xFF, 0xAA, 0x3C),
            Color.FromRgb(0x37, 0x2B, 0x1D),
            Color.FromRgb(0xFF, 0x6D, 0x6D),
            Color.FromRgb(0x3A, 0x20, 0x28),
            Color.FromRgb(0xB1, 0x83, 0xFF),
            Color.FromRgb(0x2F, 0x25, 0x44),
            Color.FromRgb(0xA5, 0xB9, 0xD4),
            Color.FromRgb(0x24, 0x34, 0x46));
    }

    private static void RefreshOpenWindows()
    {
        if (System.Windows.Application.Current is null)
        {
            return;
        }

        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            window.Dispatcher.Invoke(
                () =>
                {
                    RefreshVisual(window);
                    window.UpdateLayout();
                },
                DispatcherPriority.Render);
        }
    }

    private static void RefreshVisual(DependencyObject dependencyObject)
    {
        if (dependencyObject is UIElement element)
        {
            element.InvalidateVisual();
        }

        if (dependencyObject is FrameworkElement frameworkElement)
        {
            frameworkElement.InvalidateArrange();
            frameworkElement.InvalidateMeasure();
            frameworkElement.UpdateLayout();
        }

        var childCount = VisualTreeHelper.GetChildrenCount(dependencyObject);
        for (var index = 0; index < childCount; index++)
        {
            RefreshVisual(VisualTreeHelper.GetChild(dependencyObject, index));
        }
    }
}
