using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CQEPC.TimetableSync.Presentation.Wpf.Controls;

public static class DatePickerThemeAssist
{
    public static readonly DependencyProperty EnableThemedCalendarProperty =
        DependencyProperty.RegisterAttached(
            "EnableThemedCalendar",
            typeof(bool),
            typeof(DatePickerThemeAssist),
            new PropertyMetadata(false, OnEnableThemedCalendarChanged));

    public static bool GetEnableThemedCalendar(DatePicker datePicker) =>
        (bool)datePicker.GetValue(EnableThemedCalendarProperty);

    public static void SetEnableThemedCalendar(DatePicker datePicker, bool value) =>
        datePicker.SetValue(EnableThemedCalendarProperty, value);

    private static void OnEnableThemedCalendarChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DatePicker datePicker)
        {
            return;
        }

        datePicker.CalendarOpened -= HandleCalendarOpened;
        if (e.NewValue is true)
        {
            datePicker.CalendarOpened += HandleCalendarOpened;
        }
    }

    private static void HandleCalendarOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker datePicker)
        {
            return;
        }

        ApplyThemeSystemBrushes(datePicker.Resources);
        datePicker.Dispatcher.BeginInvoke(
            () =>
            {
                datePicker.ApplyTemplate();
                if (datePicker.Template.FindName("PART_Popup", datePicker) is Popup { Child: DependencyObject popupChild })
                {
                    ApplyDatePickerCalendarTheme(popupChild);
                }
            },
            DispatcherPriority.Loaded);
    }

    private static void ApplyDatePickerCalendarTheme(DependencyObject root)
    {
        if (root is FrameworkElement element)
        {
            ApplyThemeSystemBrushes(element.Resources);
        }

        if (root is Control control)
        {
            control.Foreground = FindBrush("TextBrush");
            control.Background = FindBrush("SurfaceBrush");
            control.BorderBrush = FindBrush("BorderBrush");
        }

        if (root is TextBlock textBlock)
        {
            textBlock.Foreground = FindBrush("TextBrush");
        }

        if (root is ButtonBase button)
        {
            button.Foreground = FindBrush("TextBrush");
        }

        if (root is Shape shape)
        {
            shape.Fill = FindBrush("TextBrush");
            shape.Stroke = FindBrush("TextBrush");
        }

        if (root is CalendarDayButton dayButton)
        {
            dayButton.Foreground = FindBrush("TextBrush");
            dayButton.Background = FindBrush("SurfaceBrush");
        }
        else if (root is CalendarButton calendarButton)
        {
            calendarButton.Foreground = FindBrush("TextBrush");
            calendarButton.Background = FindBrush("SurfaceAltBrush");
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            ApplyDatePickerCalendarTheme(VisualTreeHelper.GetChild(root, index));
        }
    }

    private static void ApplyThemeSystemBrushes(ResourceDictionary resources)
    {
        resources[SystemColors.WindowBrushKey] = FindBrush("SurfaceBrush");
        resources[SystemColors.ControlBrushKey] = FindBrush("SurfaceBrush");
        resources[SystemColors.ControlLightBrushKey] = FindBrush("SurfaceAltBrush");
        resources[SystemColors.ControlDarkBrushKey] = FindBrush("BorderBrush");
        resources[SystemColors.ControlTextBrushKey] = FindBrush("TextBrush");
        resources[SystemColors.GrayTextBrushKey] = FindBrush("SubtleTextBrush");
        resources[SystemColors.HighlightBrushKey] = FindBrush("AccentBrush");
        resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
    }

    private static Brush FindBrush(string resourceKey) =>
        System.Windows.Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
}
