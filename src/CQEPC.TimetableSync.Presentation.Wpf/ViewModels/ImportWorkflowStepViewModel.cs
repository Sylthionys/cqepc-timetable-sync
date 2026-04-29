using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportWorkflowStepViewModel : ObservableObject
{
    private bool isActive;
    private bool isCompleted;

    public ImportWorkflowStepViewModel(int index, string title, string summary, bool showsConnector)
    {
        Index = index;
        Title = title;
        Summary = summary;
        ShowsConnector = showsConnector;
    }

    public int Index { get; }

    public string Title { get; }

    public string Summary { get; }

    public string DisplayTitle => string.Create(CultureInfo.InvariantCulture, $"{Index} {Title}");

    public bool ShowsConnector { get; }

    public bool IsActive
    {
        get => isActive;
        set
        {
            if (SetProperty(ref isActive, value))
            {
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(ConnectorState));
                OnPropertyChanged(nameof(Glyph));
            }
        }
    }

    public bool IsCompleted
    {
        get => isCompleted;
        set
        {
            if (SetProperty(ref isCompleted, value))
            {
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(ConnectorState));
                OnPropertyChanged(nameof(Glyph));
            }
        }
    }

    public string Glyph => IsCompleted ? "\u2713" : Index.ToString(CultureInfo.InvariantCulture);

    public string State =>
        IsCompleted ? "Completed" :
        IsActive ? "Active" :
        "Pending";

    public string ConnectorState =>
        IsCompleted ? "Completed" :
        IsActive ? "Active" :
        "Pending";
}
