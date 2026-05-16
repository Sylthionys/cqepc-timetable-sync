using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Views;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private const double CompactLayoutWidth = 760d;
    private const double MediumLayoutWidth = 1040d;
    private const double TimeProfileModeWidth = 152d;
    private SettingsPageViewModel? subscribedViewModel;
    private bool selectedSectionTransitionQueued;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyAdaptiveLayout();
            QueueSelectedSectionTransition();
        };
        Unloaded += (_, _) => DetachFromViewModel();
        DataContextChanged += HandleDataContextChanged;
    }

    private void HandleRootGridSizeChanged(object sender, System.Windows.SizeChangedEventArgs e) =>
        ApplyAdaptiveLayout();

    private void HandleGoogleTimeZonePopupOpened(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                GoogleTimeZoneSearchBox.UpdateLayout();
                GoogleTimeZoneSearchBox.Focus();
                Keyboard.Focus(GoogleTimeZoneSearchBox);
                GoogleTimeZoneSearchBox.CaretIndex = GoogleTimeZoneSearchBox.Text.Length;
            },
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HandleGoogleTimeZonePopupClosed(object sender, EventArgs e)
    {
        if (DataContext is SettingsPageViewModel { Workspace: not null } viewModel)
        {
            viewModel.Workspace.GoogleTimeZoneSearchText = string.Empty;
        }
    }

    private void HandleGoogleTimeZoneResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { IsKeyboardFocusWithin: false, IsMouseOver: false }
            || GoogleTimeZonePopup is not Popup { IsOpen: true })
        {
            return;
        }

        GoogleTimeZonePopup.IsOpen = false;
    }

    private void HandleCustomNetworkProxyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox
            && DataContext is SettingsPageViewModel { Workspace: not null } viewModel)
        {
            viewModel.Workspace.CustomNetworkProxyPassword = passwordBox.Password;
        }
    }

    private void HandleDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromViewModel();
        subscribedViewModel = e.NewValue as SettingsPageViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged += HandleViewModelPropertyChanged;
        }

        QueueSelectedSectionTransition();
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SettingsPageViewModel.SelectedSection), StringComparison.Ordinal))
        {
            QueueSelectedSectionTransition();
        }
    }

    private void QueueSelectedSectionTransition()
    {
        if (!IsLoaded || selectedSectionTransitionQueued)
        {
            return;
        }

        selectedSectionTransitionQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                selectedSectionTransitionQueued = false;
                PlaySelectedSectionTransition();
            },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void PlaySelectedSectionTransition()
    {
        var section = subscribedViewModel?.SelectedSection switch
        {
            SettingsSection.LocalFiles => LocalFilesSection,
            SettingsSection.Timetable => TimetableSection,
            SettingsSection.Connections => ConnectionsSection,
            SettingsSection.Program => ProgramSection,
            _ => null,
        };
        if (section is null)
        {
            return;
        }

        section.ApplyTemplate();
        section.UpdateLayout();
        if (section.Visibility != Visibility.Visible)
        {
            return;
        }

        if (section.RenderTransform is not TranslateTransform translateTransform)
        {
            translateTransform = new TranslateTransform();
            section.RenderTransform = translateTransform;
        }

        section.BeginAnimation(OpacityProperty, null);
        translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        section.Opacity = 0;
        translateTransform.Y = 12;

        var duration = TimeSpan.FromMilliseconds(240);
        var fadeAnimation = new DoubleAnimation(1, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        var slideAnimation = new DoubleAnimation(0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        section.BeginAnimation(OpacityProperty, fadeAnimation, HandoffBehavior.SnapshotAndReplace);
        translateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnimation, HandoffBehavior.SnapshotAndReplace);
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

    private void ApplyAdaptiveLayout()
    {
        var width = RootGrid.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var compact = width < CompactLayoutWidth;
        var medium = width < MediumLayoutWidth;
        SettingsLayoutRoot.Margin = compact
            ? new System.Windows.Thickness(12)
            : new System.Windows.Thickness(18);

        ApplyTwoColumnLayout(
            TimetableHeaderGrid,
            FirstWeekPanel,
            ParsedClassPanel,
            compact,
            firstColumn: new System.Windows.GridLength(1.2, System.Windows.GridUnitType.Star),
            secondColumn: new System.Windows.GridLength(0.8, System.Windows.GridUnitType.Star));
        ApplyTimeProfileDefaultsLayout(compact);
        ApplyCourseOverrideEditorLayout(compact);
        ApplyTwoColumnLayout(
            ProviderDestinationsGrid,
            CalendarDestinationPanel,
            TaskListDestinationPanel,
            compact,
            firstColumn: new System.Windows.GridLength(1, System.Windows.GridUnitType.Star),
            secondColumn: new System.Windows.GridLength(1, System.Windows.GridUnitType.Star));
        ApplyTwoColumnLayout(
            ProgramCardsGrid,
            ProgramPreferencesCard,
            ProgramBehaviorCard,
            medium,
            firstColumn: new System.Windows.GridLength(1, System.Windows.GridUnitType.Star),
            secondColumn: new System.Windows.GridLength(1, System.Windows.GridUnitType.Star));
        ApplyTwoColumnLayout(
            ProgramPreferencesGrid,
            WeekStartPanel,
            LanguagePanel,
            compact,
            firstColumn: new System.Windows.GridLength(1, System.Windows.GridUnitType.Star),
            secondColumn: new System.Windows.GridLength(1, System.Windows.GridUnitType.Star));
    }

    private void ApplyTimeProfileDefaultsLayout(bool compact)
    {
        EnsureRows(TimeProfileDefaultsGrid, compact ? 3 : 1);
        if (compact)
        {
            SetColumns(TimeProfileDefaultsGrid, new System.Windows.GridLength(1, System.Windows.GridUnitType.Star));
            Place(TimeProfileModePanel, row: 0, column: 0);
            Place(ExplicitTimeProfilePanel, row: 2, column: 0);
            ExplicitTimeProfilePanel.Margin = new System.Windows.Thickness(0, 14, 0, 0);
            return;
        }

        SetColumns(
            TimeProfileDefaultsGrid,
            new System.Windows.GridLength(TimeProfileModeWidth),
            new System.Windows.GridLength(14),
            new System.Windows.GridLength(1, System.Windows.GridUnitType.Star));
        Place(TimeProfileModePanel, row: 0, column: 0);
        Place(ExplicitTimeProfilePanel, row: 0, column: 2);
        ExplicitTimeProfilePanel.Margin = default;
    }

    private void ApplyCourseOverrideEditorLayout(bool compact)
    {
        EnsureRows(CourseOverrideEditorGrid, compact ? 5 : 1);
        if (compact)
        {
            SetColumns(CourseOverrideEditorGrid, new System.Windows.GridLength(1, System.Windows.GridUnitType.Star));
            Place(CourseOverrideCourseCombo, row: 0, column: 0);
            Place(CourseOverrideProfileCombo, row: 2, column: 0);
            Place(AddCourseOverrideButton, row: 4, column: 0);
            CourseOverrideProfileCombo.Margin = new System.Windows.Thickness(0, 10, 0, 0);
            AddCourseOverrideButton.Margin = new System.Windows.Thickness(0, 10, 0, 0);
            AddCourseOverrideButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            return;
        }

        SetColumns(
            CourseOverrideEditorGrid,
            new System.Windows.GridLength(1, System.Windows.GridUnitType.Star),
            new System.Windows.GridLength(14),
            new System.Windows.GridLength(1, System.Windows.GridUnitType.Star),
            System.Windows.GridLength.Auto);
        Place(CourseOverrideCourseCombo, row: 0, column: 0);
        Place(CourseOverrideProfileCombo, row: 0, column: 2);
        Place(AddCourseOverrideButton, row: 0, column: 3);
        CourseOverrideProfileCombo.Margin = default;
        AddCourseOverrideButton.Margin = new System.Windows.Thickness(12, 0, 0, 0);
        AddCourseOverrideButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
    }

    private static void ApplyTwoColumnLayout(
        System.Windows.Controls.Grid grid,
        System.Windows.FrameworkElement first,
        System.Windows.FrameworkElement second,
        bool stack,
        System.Windows.GridLength firstColumn,
        System.Windows.GridLength secondColumn)
    {
        EnsureRows(grid, stack ? 3 : 1);
        if (stack)
        {
            SetColumns(grid, new System.Windows.GridLength(1, System.Windows.GridUnitType.Star));
            Place(first, row: 0, column: 0);
            Place(second, row: 2, column: 0);
            second.Margin = new System.Windows.Thickness(0, 14, 0, 0);
            return;
        }

        SetColumns(grid, firstColumn, new System.Windows.GridLength(14), secondColumn);
        Place(first, row: 0, column: 0);
        Place(second, row: 0, column: 2);
        second.Margin = default;
    }

    private static void Place(System.Windows.FrameworkElement element, int row, int column)
    {
        System.Windows.Controls.Grid.SetRow(element, row);
        System.Windows.Controls.Grid.SetColumn(element, column);
        System.Windows.Controls.Grid.SetColumnSpan(element, 1);
    }

    private static void EnsureRows(System.Windows.Controls.Grid grid, int count)
    {
        while (grid.RowDefinitions.Count < count)
        {
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        }

        while (grid.RowDefinitions.Count > count)
        {
            grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        }

        for (var index = 0; index < grid.RowDefinitions.Count; index++)
        {
            grid.RowDefinitions[index].Height = System.Windows.GridLength.Auto;
        }
    }

    private static void SetColumns(System.Windows.Controls.Grid grid, params System.Windows.GridLength[] widths)
    {
        while (grid.ColumnDefinitions.Count < widths.Length)
        {
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
        }

        while (grid.ColumnDefinitions.Count > widths.Length)
        {
            grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);
        }

        for (var index = 0; index < widths.Length; index++)
        {
            grid.ColumnDefinitions[index].Width = widths[index];
        }
    }
}
