using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CourseTimeProfileOverrideItemViewModel : ObservableObject
{
    private string profileDisplayName;
    private string statusText;
    private bool isMatched;

    public CourseTimeProfileOverrideItemViewModel(
        string className,
        string courseTitle,
        string profileId,
        string profileDisplayName,
        string statusText,
        bool isMatched,
        Action<CourseTimeProfileOverrideItemViewModel> remove)
    {
        ClassName = className;
        CourseTitle = courseTitle;
        ProfileId = profileId;
        this.profileDisplayName = profileDisplayName;
        this.statusText = statusText;
        this.isMatched = isMatched;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string ClassName { get; }

    public string CourseTitle { get; }

    public string ProfileId { get; }

    public string ProfileDisplayName
    {
        get => profileDisplayName;
        private set => SetProperty(ref profileDisplayName, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsMatched
    {
        get => isMatched;
        private set => SetProperty(ref isMatched, value);
    }

    public IRelayCommand RemoveCommand { get; }

    public void Update(string updatedProfileDisplayName, string updatedStatusText, bool updatedIsMatched)
    {
        ProfileDisplayName = updatedProfileDisplayName;
        StatusText = updatedStatusText;
        IsMatched = updatedIsMatched;
    }
}
