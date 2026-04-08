using System.Windows;
using System.Windows.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.Behaviors;

public static class FileDropBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(FileDropBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject obj) =>
        (ICommand?)obj.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject obj, ICommand? value) =>
        obj.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not UIElement element)
        {
            return;
        }

        element.PreviewDragOver -= OnPreviewDragOver;
        element.PreviewDrop -= OnPreviewDrop;

        if (e.NewValue is null)
        {
            element.AllowDrop = false;
            return;
        }

        element.AllowDrop = true;
        element.PreviewDragOver += OnPreviewDragOver;
        element.PreviewDrop += OnPreviewDrop;
    }

    private static void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject dependencyObject)
        {
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Handled = true;
            return;
        }

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        var command = GetCommand(dependencyObject);
        if (files is not null && command?.CanExecute(files) == true)
        {
            command.Execute(files);
        }

        e.Handled = true;
    }
}
