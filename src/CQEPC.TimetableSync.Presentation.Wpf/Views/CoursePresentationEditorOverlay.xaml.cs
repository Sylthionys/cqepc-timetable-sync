using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Views;

public partial class CoursePresentationEditorOverlay : UserControl
{
    public CoursePresentationEditorOverlay()
    {
        InitializeComponent();
        Loaded += HandleLoaded;
        KeyDown += HandleKeyDown;
    }

    private void HandleLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () => TimeZoneComboBox.Focus());
    }

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (DataContext is not CoursePresentationEditorViewModel viewModel)
        {
            return;
        }

        if (!viewModel.CancelCommand.CanExecute(null))
        {
            return;
        }

        viewModel.CancelCommand.Execute(null);
        e.Handled = true;
    }
}
