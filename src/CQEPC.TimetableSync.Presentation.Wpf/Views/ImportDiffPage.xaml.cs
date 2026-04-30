namespace CQEPC.TimetableSync.Presentation.Wpf.Views;

public partial class ImportDiffPage : System.Windows.Controls.UserControl
{
    private const double MouseWheelScrollStep = 56d;

    public ImportDiffPage()
    {
        InitializeComponent();
    }

    private void HandlePanelPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ScrollViewer scrollViewer
            || scrollViewer.ScrollableHeight <= 0)
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
}
