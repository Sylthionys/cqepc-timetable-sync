using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Controls;

public partial class TimeZonePicker : UserControl, INotifyPropertyChanged
{
    private const double CompactPopupWidth = 360d;
    private const double MinimumUsablePopupWidth = 320d;
    private const double WideCategoryColumnWidth = 150d;
    private const double CompactCategoryColumnWidth = 118d;
    private const double CompactCategoryThreshold = 430d;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(TimeZonePicker),
            new PropertyMetadata(null, HandleItemsSourceChanged));

    public static readonly DependencyProperty RecentTimeZoneIdsProperty =
        DependencyProperty.Register(
            nameof(RecentTimeZoneIds),
            typeof(IEnumerable),
            typeof(TimeZonePicker),
            new PropertyMetadata(null, HandleRecentTimeZoneIdsChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(GoogleTimeZoneOptionViewModel),
            typeof(TimeZonePicker),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                HandleSelectedItemChanged));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText),
            typeof(string),
            typeof(TimeZonePicker),
            new PropertyMetadata(string.Empty, HandleSearchTextChanged));

    public static readonly DependencyProperty PopupMinWidthProperty =
        DependencyProperty.Register(
            nameof(PopupMinWidth),
            typeof(double),
            typeof(TimeZonePicker),
            new PropertyMetadata(520d));

    public static readonly DependencyProperty PopupMaxWidthProperty =
        DependencyProperty.Register(
            nameof(PopupMaxWidth),
            typeof(double),
            typeof(TimeZonePicker),
            new PropertyMetadata(660d));

    public static readonly DependencyProperty PopupMaxHeightProperty =
        DependencyProperty.Register(
            nameof(PopupMaxHeight),
            typeof(double),
            typeof(TimeZonePicker),
            new PropertyMetadata(360d));

    public static readonly DependencyProperty SearchAutomationIdProperty =
        DependencyProperty.Register(
            nameof(SearchAutomationId),
            typeof(string),
            typeof(TimeZonePicker),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CategoryAutomationIdProperty =
        DependencyProperty.Register(
            nameof(CategoryAutomationId),
            typeof(string),
            typeof(TimeZonePicker),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ResultAutomationIdProperty =
        DependencyProperty.Register(
            nameof(ResultAutomationId),
            typeof(string),
            typeof(TimeZonePicker),
            new PropertyMetadata(string.Empty));

    private static readonly WorkspaceTimeZoneRegion[] RegionOrder =
    [
        WorkspaceTimeZoneRegion.Common,
        WorkspaceTimeZoneRegion.Asia,
        WorkspaceTimeZoneRegion.Europe,
        WorkspaceTimeZoneRegion.NorthAmerica,
        WorkspaceTimeZoneRegion.SouthAmerica,
        WorkspaceTimeZoneRegion.Africa,
        WorkspaceTimeZoneRegion.Oceania,
        WorkspaceTimeZoneRegion.Utc,
    ];

    private readonly ObservableCollection<GoogleTimeZoneOptionViewModel> sourceItems = [];
    private readonly List<string> localRecentTimeZoneIds = [];
    private bool suppressCloseOnSelectionChanged;
    private INotifyCollectionChanged? observedItemsSource;
    private INotifyCollectionChanged? observedRecentTimeZoneIds;

    public TimeZonePicker()
    {
        CategoryOptions = new ObservableCollection<TimeZoneCategoryItem>(
            RegionOrder.Select(static region => new TimeZoneCategoryItem(region)));
        FilteredItems = new ObservableCollection<GoogleTimeZoneOptionViewModel>();
        SelectedCategory = CategoryOptions[0];
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IEnumerable? RecentTimeZoneIds
    {
        get => (IEnumerable?)GetValue(RecentTimeZoneIdsProperty);
        set => SetValue(RecentTimeZoneIdsProperty, value);
    }

    public GoogleTimeZoneOptionViewModel? SelectedItem
    {
        get => (GoogleTimeZoneOptionViewModel?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public double PopupMinWidth
    {
        get => (double)GetValue(PopupMinWidthProperty);
        set => SetValue(PopupMinWidthProperty, value);
    }

    public double PopupMaxWidth
    {
        get => (double)GetValue(PopupMaxWidthProperty);
        set => SetValue(PopupMaxWidthProperty, value);
    }

    public double PopupMaxHeight
    {
        get => (double)GetValue(PopupMaxHeightProperty);
        set => SetValue(PopupMaxHeightProperty, value);
    }

    public string SearchAutomationId
    {
        get => (string)GetValue(SearchAutomationIdProperty);
        set => SetValue(SearchAutomationIdProperty, value);
    }

    public string CategoryAutomationId
    {
        get => (string)GetValue(CategoryAutomationIdProperty);
        set => SetValue(CategoryAutomationIdProperty, value);
    }

    public string ResultAutomationId
    {
        get => (string)GetValue(ResultAutomationIdProperty);
        set => SetValue(ResultAutomationIdProperty, value);
    }

    public ObservableCollection<TimeZoneCategoryItem> CategoryOptions { get; }

    public ObservableCollection<GoogleTimeZoneOptionViewModel> FilteredItems { get; }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    public double EffectivePopupWidth { get; private set; } = 520d;

    public double EffectiveCategoryColumnWidth { get; private set; } = WideCategoryColumnWidth;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TimeZoneCategoryItem? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (Equals(selectedCategory, value))
            {
                return;
            }

            selectedCategory = value;
            OnPropertyChanged(nameof(SelectedCategory));
            RebuildFilteredItems();
        }
    }

    private TimeZoneCategoryItem? selectedCategory;

    private static void HandleItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((TimeZonePicker)dependencyObject).LoadItems(e.NewValue as IEnumerable);
    }

    private static void HandleSelectedItemChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var picker = (TimeZonePicker)dependencyObject;
        if (e.NewValue is GoogleTimeZoneOptionViewModel selected)
        {
            picker.PromoteLocalRecentTimeZoneId(selected.TimeZoneId);
        }

        picker.EnsureSelectedItemVisible();
        picker.SyncResultSelection();
    }

    private static void HandleRecentTimeZoneIdsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((TimeZonePicker)dependencyObject).ObserveRecentTimeZoneIds(e.NewValue as IEnumerable);
    }

    private static void HandleSearchTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var picker = (TimeZonePicker)dependencyObject;
        picker.OnPropertyChanged(nameof(HasSearchText));
        picker.RebuildFilteredItems();
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void LoadItems(IEnumerable? items)
    {
        if (observedItemsSource is not null)
        {
            observedItemsSource.CollectionChanged -= HandleItemsSourceCollectionChanged;
            observedItemsSource = null;
        }

        if (items is INotifyCollectionChanged notifyCollectionChanged)
        {
            observedItemsSource = notifyCollectionChanged;
            observedItemsSource.CollectionChanged += HandleItemsSourceCollectionChanged;
        }

        sourceItems.Clear();
        if (items is not null)
        {
            foreach (var item in items.OfType<GoogleTimeZoneOptionViewModel>())
            {
                sourceItems.Add(item);
            }
        }

        RebuildFilteredItems();
        EnsureSelectedItemVisible();
    }

    private void ObserveRecentTimeZoneIds(IEnumerable? items)
    {
        if (observedRecentTimeZoneIds is not null)
        {
            observedRecentTimeZoneIds.CollectionChanged -= HandleRecentTimeZoneIdsCollectionChanged;
            observedRecentTimeZoneIds = null;
        }

        if (items is INotifyCollectionChanged notifyCollectionChanged)
        {
            observedRecentTimeZoneIds = notifyCollectionChanged;
            observedRecentTimeZoneIds.CollectionChanged += HandleRecentTimeZoneIdsCollectionChanged;
        }

        RebuildFilteredItems();
    }

    private void HandleItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        LoadItems(ItemsSource);
    }

    private void HandleRecentTimeZoneIdsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildFilteredItems();
    }

    private void EnsureSelectedItemVisible()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var matchingCategory = SelectedItem.Region == WorkspaceTimeZoneRegion.Common
            ? WorkspaceTimeZoneRegion.Common
            : SelectedItem.Region;
        SelectedCategory = CategoryOptions.FirstOrDefault(category => category.Region == matchingCategory)
            ?? CategoryOptions[0];
    }

    private void RebuildFilteredItems()
    {
        var selectedRegion = SelectedCategory?.Region ?? WorkspaceTimeZoneRegion.Common;
        var query = Normalize(SearchText);
        var filtered = BuildFilteredItems(selectedRegion, query);

        suppressCloseOnSelectionChanged = true;
        try
        {
            FilteredItems.Clear();
            foreach (var option in filtered)
            {
                FilteredItems.Add(option);
            }

            SyncResultSelection();
        }
        finally
        {
            suppressCloseOnSelectionChanged = false;
        }
    }

    private GoogleTimeZoneOptionViewModel[] BuildFilteredItems(WorkspaceTimeZoneRegion selectedRegion, string query)
    {
        if (!string.IsNullOrWhiteSpace(query))
        {
            return sourceItems
                .Where(option => MatchesQuery(option, query))
                .GroupBy(static option => option.TimeZoneId, StringComparer.Ordinal)
                .Select(group => group.FirstOrDefault(option => option.Region == selectedRegion)
                    ?? group.FirstOrDefault(static option => option.Region != WorkspaceTimeZoneRegion.Common)
                    ?? group.First())
                .ToArray();
        }

        if (selectedRegion == WorkspaceTimeZoneRegion.Common)
        {
            return BuildCommonItems();
        }

        return sourceItems
            .Where(option => option.Region == selectedRegion)
            .GroupBy(static option => option.TimeZoneId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
    }

    private GoogleTimeZoneOptionViewModel[] BuildCommonItems()
    {
        var sourceById = sourceItems
            .GroupBy(static option => option.TimeZoneId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.FirstOrDefault(static option => option.Region == WorkspaceTimeZoneRegion.Common)
                    ?? group.FirstOrDefault(static option => option.Region != WorkspaceTimeZoneRegion.Common)
                    ?? group.First(),
                StringComparer.Ordinal);
        var explicitCommonIds = sourceItems
            .Where(static option => option.Region == WorkspaceTimeZoneRegion.Common)
            .Select(static option => option.TimeZoneId);
        var orderedIds = GetRecentTimeZoneIds()
            .Concat(explicitCommonIds)
            .Concat(WorkspaceTimeZoneCatalog.PopularTimeZoneIds)
            .Distinct(StringComparer.Ordinal);

        return orderedIds
            .Select(id => sourceById.TryGetValue(id, out var option) ? option : null)
            .Where(static option => option is not null)
            .Select(static option => option!)
            .ToArray();
    }

    private IEnumerable<string> GetRecentTimeZoneIds() =>
        localRecentTimeZoneIds
            .Concat(EnumerateRecentTimeZoneIds(RecentTimeZoneIds))
            .Select(WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId)
            .Where(static id => id is not null)
            .Select(static id => id!)
            .Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateRecentTimeZoneIds(IEnumerable? items)
    {
        if (items is null)
        {
            yield break;
        }

        foreach (var item in items)
        {
            if (item is string timeZoneId && !string.IsNullOrWhiteSpace(timeZoneId))
            {
                yield return timeZoneId;
            }
        }
    }

    private void PromoteLocalRecentTimeZoneId(string? timeZoneId)
    {
        var normalized = WorkspaceTimeZoneCatalog.ResolveKnownTimeZoneId(timeZoneId);
        if (normalized is null)
        {
            return;
        }

        localRecentTimeZoneIds.RemoveAll(id => string.Equals(id, normalized, StringComparison.Ordinal));
        localRecentTimeZoneIds.Insert(0, normalized);
        if (localRecentTimeZoneIds.Count > 6)
        {
            localRecentTimeZoneIds.RemoveRange(6, localRecentTimeZoneIds.Count - 6);
        }

        if (SelectedCategory?.Region == WorkspaceTimeZoneRegion.Common && string.IsNullOrWhiteSpace(SearchText))
        {
            RebuildFilteredItems();
        }
    }

    private void SyncResultSelection()
    {
        if (ResultList is null)
        {
            return;
        }

        ResultList.SelectedItem = SelectedItem is null
            ? null
            : FilteredItems.FirstOrDefault(option => string.Equals(option.TimeZoneId, SelectedItem.TimeZoneId, StringComparison.Ordinal));
    }

    private static bool MatchesQuery(GoogleTimeZoneOptionViewModel option, string query) =>
        option.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)
        || option.TimeZoneId.Contains(query, StringComparison.OrdinalIgnoreCase)
        || option.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || option.LocalizedDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private void HandlePopupOpened(object sender, EventArgs e)
    {
        UpdatePopupMetrics();
        SearchBox.Dispatcher.BeginInvoke(
            () =>
            {
                SearchBox.UpdateLayout();
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                SearchBox.CaretIndex = SearchBox.Text.Length;
            },
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HandlePopupClosed(object sender, EventArgs e)
    {
        SearchText = string.Empty;
    }

    public void OpenDropDown()
    {
        UpdatePopupMetrics();
        DropDownToggle.IsChecked = true;
        PickerPopup.IsOpen = true;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        UpdatePopupMetrics();
    }

    private void HandleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePopupMetrics();
    }

    private void UpdatePopupMetrics()
    {
        var targetWidth = ActualWidth > 0 ? ActualWidth : DropDownToggle.ActualWidth;
        var desiredWidth = targetWidth > 0 && targetWidth < CompactCategoryThreshold
            ? Math.Max(targetWidth, CompactPopupWidth)
            : Math.Max(targetWidth, PopupMinWidth);

        var availableWidth = GetAvailablePopupWidth();
        var maxWidth = Math.Min(PopupMaxWidth, availableWidth);
        var minWidth = Math.Min(Math.Max(targetWidth, MinimumUsablePopupWidth), maxWidth);
        var width = Math.Min(Math.Max(desiredWidth, minWidth), maxWidth);

        if (Math.Abs(EffectivePopupWidth - width) > 0.5)
        {
            EffectivePopupWidth = width;
            OnPropertyChanged(nameof(EffectivePopupWidth));
        }

        var categoryWidth = width < CompactCategoryThreshold
            ? CompactCategoryColumnWidth
            : WideCategoryColumnWidth;

        if (Math.Abs(EffectiveCategoryColumnWidth - categoryWidth) > 0.5)
        {
            EffectiveCategoryColumnWidth = categoryWidth;
            OnPropertyChanged(nameof(EffectiveCategoryColumnWidth));
        }
    }

    private double GetAvailablePopupWidth()
    {
        var screenWidth = SystemParameters.WorkArea.Width - 32d;
        var window = Window.GetWindow(this);
        if (window is null || window.ActualWidth <= 0)
        {
            return Math.Max(MinimumUsablePopupWidth, screenWidth);
        }

        return Math.Max(MinimumUsablePopupWidth, Math.Min(screenWidth, window.ActualWidth - 32d));
    }

    private void HandleResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressCloseOnSelectionChanged
            || PickerPopup is not Popup { IsOpen: true }
            || sender is not ListBox { IsMouseOver: true }
            || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.AddedItems.OfType<GoogleTimeZoneOptionViewModel>().FirstOrDefault() is { } selected)
        {
            SelectedItem = selected;
        }

        PickerPopup.IsOpen = false;
    }

    private void HandleResultKeyDown(object sender, KeyEventArgs e)
    {
        if (PickerPopup is not Popup { IsOpen: true })
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            PickerPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Enter or Key.Space)
            || sender is not ListBox { SelectedItem: GoogleTimeZoneOptionViewModel selected })
        {
            return;
        }

        SelectedItem = selected;
        PickerPopup.IsOpen = false;
        e.Handled = true;
    }
}

public sealed record TimeZoneCategoryItem(WorkspaceTimeZoneRegion Region);
