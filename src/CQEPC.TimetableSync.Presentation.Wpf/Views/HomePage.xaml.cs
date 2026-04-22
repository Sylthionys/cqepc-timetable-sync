namespace CQEPC.TimetableSync.Presentation.Wpf.Views;

public partial class HomePage : System.Windows.Controls.UserControl
{
    private System.ComponentModel.INotifyPropertyChanged? observedViewModel;
    private readonly System.Windows.Threading.DispatcherTimer responsiveLayoutTimer;
    private double lastCalendarWidth = -1d;
    private double lastCalendarHeight = -1d;
    private double lastAgendaWidth = -1d;

    public HomePage()
    {
        InitializeComponent();
        responsiveLayoutTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(90),
            System.Windows.Threading.DispatcherPriority.Background,
            HandleResponsiveLayoutTimerTick,
            Dispatcher);
        responsiveLayoutTimer.Stop();
        DataContextChanged += HandleDataContextChanged;
        Loaded += HandleLoaded;
        SizeChanged += HandleSizeChanged;
        LayoutUpdated += HandleLayoutUpdated;
        Unloaded += HandleUnloaded;
    }

    private void HandleDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        }

        observedViewModel = DataContext as System.ComponentModel.INotifyPropertyChanged;
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += HandleViewModelPropertyChanged;
        }

        ScheduleResponsiveLayout(immediate: true);
    }

    private void HandleUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (observedViewModel is null)
        {
            return;
        }

        responsiveLayoutTimer.Stop();
        observedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        observedViewModel = null;
    }

    private void HandleLoaded(object sender, System.Windows.RoutedEventArgs e) => ScheduleResponsiveLayout(immediate: true);

    private void HandleSizeChanged(object sender, System.Windows.SizeChangedEventArgs e) => ScheduleResponsiveLayout();

    private void HandleLayoutUpdated(object? sender, EventArgs e)
    {
        if (!HasMeaningfulLayoutChange())
        {
            return;
        }

        ScheduleResponsiveLayout();
    }

    private void HandleViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.HomePageViewModel.CurrentMonthTitle))
        {
            AnimateFade(CalendarRegion);
            return;
        }

        if (e.PropertyName == nameof(ViewModels.HomePageViewModel.SelectedDayTitle))
        {
            AnimateFade(AgendaRegion);
            return;
        }

    }

    private static void AnimateFade(System.Windows.UIElement element)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.94,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(110),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };

        element.BeginAnimation(OpacityProperty, animation, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    private void HandlePanelPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ScrollViewer scrollViewer)
        {
            return;
        }

        if (e.Delta < 0)
        {
            scrollViewer.LineDown();
        }
        else
        {
            scrollViewer.LineUp();
        }

        e.Handled = true;
    }

    private void HandleResponsiveLayoutTimerTick(object? sender, EventArgs e)
    {
        responsiveLayoutTimer.Stop();
        ApplyResponsiveLayout();
    }

    private bool HasMeaningfulLayoutChange()
    {
        var calendarWidth = CalendarRegion.ActualWidth;
        var calendarHeight = CalendarScrollViewer.ActualHeight;
        var agendaWidth = AgendaRegion.ActualWidth;

        if (Math.Abs(calendarWidth - lastCalendarWidth) < 0.5d
            && Math.Abs(calendarHeight - lastCalendarHeight) < 0.5d
            && Math.Abs(agendaWidth - lastAgendaWidth) < 0.5d)
        {
            return false;
        }

        lastCalendarWidth = calendarWidth;
        lastCalendarHeight = calendarHeight;
        lastAgendaWidth = agendaWidth;
        return true;
    }

    private void ScheduleResponsiveLayout(bool immediate = false)
    {
        if (immediate)
        {
            responsiveLayoutTimer.Stop();
            ApplyResponsiveLayout();
            return;
        }

        responsiveLayoutTimer.Stop();
        responsiveLayoutTimer.Start();
    }

    private void ApplyResponsiveLayout()
    {
        if (DataContext is not ViewModels.HomePageViewModel viewModel)
        {
            return;
        }

        viewModel.UpdateResponsiveLayout(
            CalendarRegion.ActualWidth,
            CalendarScrollViewer.ActualHeight,
            AgendaRegion.ActualWidth);
    }
}
