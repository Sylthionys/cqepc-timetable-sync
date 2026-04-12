using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed partial class TaskExecutionViewModel : ObservableObject
{
    public TaskExecutionViewModel(int sequence, string title, string detail)
    {
        Sequence = sequence;
        this.title = title;
        this.detail = detail;
    }

    public int Sequence { get; }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string detail;
}
