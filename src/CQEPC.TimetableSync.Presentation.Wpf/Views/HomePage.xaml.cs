namespace CQEPC.TimetableSync.Presentation.Wpf.Views;

public partial class HomePage : System.Windows.Controls.UserControl
{
    private System.ComponentModel.INotifyPropertyChanged? observedViewModel;

    public HomePage()
    {
        InitializeComponent();
        DataContextChanged += HandleDataContextChanged;
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
    }

    private void HandleUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (observedViewModel is null)
        {
            return;
        }

        observedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        observedViewModel = null;
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
        }
    }

    private static void AnimateFade(System.Windows.UIElement element)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.78,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };

        element.BeginAnimation(OpacityProperty, animation, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }
}
