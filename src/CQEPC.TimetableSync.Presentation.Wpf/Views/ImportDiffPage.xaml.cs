using System.Windows;
using System.Windows.Media;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Views;

public partial class ImportDiffPage : System.Windows.Controls.UserControl
{
    private const double MouseWheelScrollStep = 56d;
    private const int ParsedOccurrenceScrollRetryCount = 2;
    private bool isReviewScrollLayoutRefreshQueued;

    public ImportDiffPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ImportDiffPageViewModel oldVm)
        {
            oldVm.ParsedOccurrenceScrollRequested -= OnParsedOccurrenceScrollRequested;
        }

        if (e.NewValue is ImportDiffPageViewModel newVm)
        {
            newVm.ParsedOccurrenceScrollRequested += OnParsedOccurrenceScrollRequested;
        }
    }

    private void OnParsedOccurrenceScrollRequested(string? stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            () => ScrollToParsedOccurrence(stableId, ParsedOccurrenceScrollRetryCount));
    }

    private void ScrollToParsedOccurrence(string stableId, int remainingRetries)
    {
        if (ParsedCourseGroupsControl is null)
        {
            return;
        }

        RefreshReviewScrollLayout();

        var element = FindVisualChildByStableId(ParsedCourseGroupsControl, stableId);
        if (element is null)
        {
            RetryParsedOccurrenceScroll(stableId, remainingRetries);
            return;
        }

        element.UpdateLayout();

        var position = element.TransformToAncestor(ReviewScrollViewer).Transform(new Point(0d, 0d));
        var desiredOffset = position.Y < 0d
            ? ReviewScrollViewer.VerticalOffset + position.Y - 12d
            : position.Y + element.ActualHeight > ReviewScrollViewer.ViewportHeight
                ? ReviewScrollViewer.VerticalOffset + position.Y + element.ActualHeight - ReviewScrollViewer.ViewportHeight + 12d
                : ReviewScrollViewer.VerticalOffset;
        var targetOffset = Math.Clamp(
            desiredOffset,
            0d,
            ReviewScrollViewer.ScrollableHeight);
        ReviewScrollViewer.ScrollToVerticalOffset(targetOffset);
        ReviewScrollViewer.UpdateLayout();
    }

    private void RefreshReviewScrollLayout()
    {
        ParsedCourseGroupsControl.InvalidateMeasure();
        ParsedCourseGroupsControl.InvalidateArrange();
        ReviewScrollViewer.InvalidateMeasure();
        ReviewScrollViewer.InvalidateArrange();
        ReviewScrollViewer.InvalidateScrollInfo();
        ParsedCourseGroupsControl.UpdateLayout();
        ReviewScrollViewer.UpdateLayout();
    }

    private void HandleReviewScrollContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (isReviewScrollLayoutRefreshQueued)
        {
            return;
        }

        isReviewScrollLayoutRefreshQueued = true;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            () =>
            {
                isReviewScrollLayoutRefreshQueued = false;
                ReviewScrollViewer.InvalidateScrollInfo();
                ReviewScrollViewer.UpdateLayout();
            });
    }

    private void RetryParsedOccurrenceScroll(string stableId, int remainingRetries)
    {
        if (remainingRetries <= 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            () => ScrollToParsedOccurrence(stableId, remainingRetries - 1));
    }

    private static System.Windows.FrameworkElement? FindVisualChildByStableId(
        System.Windows.DependencyObject parent,
        string stableId)
    {
        var childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.FrameworkElement fe
                && fe.DataContext is EditableCourseTimeItemViewModel timeItem
                && !string.IsNullOrWhiteSpace(timeItem.StableId)
                && string.Equals(timeItem.StableId, stableId, System.StringComparison.Ordinal))
            {
                return fe;
            }

            var descendant = FindVisualChildByStableId(child, stableId);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void HandlePanelPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var scrollViewer = sender as System.Windows.Controls.ScrollViewer
            ?? FindScrollableAncestor(e.OriginalSource as System.Windows.DependencyObject, e.Delta);
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var nextOffset = scrollViewer.VerticalOffset - (e.Delta / 120d * MouseWheelScrollStep);
        nextOffset = Math.Clamp(nextOffset, 0d, scrollViewer.ScrollableHeight);
        if (Math.Abs(nextOffset - scrollViewer.VerticalOffset) < 0.1d)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(nextOffset);
        e.Handled = true;
    }

    private static System.Windows.Controls.ScrollViewer? FindScrollableAncestor(
        System.Windows.DependencyObject? source,
        int delta)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not System.Windows.Controls.ScrollViewer scrollViewer)
            {
                continue;
            }

            if (delta < 0 && scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight)
            {
                return scrollViewer;
            }

            if (delta > 0 && scrollViewer.VerticalOffset > 0)
            {
                return scrollViewer;
            }
        }

        return null;
    }
}
