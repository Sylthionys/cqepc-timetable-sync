using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class TimeProfileOptionViewModel
{
    public TimeProfileOptionViewModel(string? profileId, string name, string summary)
    {
        ProfileId = string.IsNullOrWhiteSpace(profileId) ? null : profileId.Trim();
        Name = string.IsNullOrWhiteSpace(name) ? UiText.UnnamedProfile : name.Trim();
        Summary = string.IsNullOrWhiteSpace(summary) ? UiText.NoProfileSummary : summary.Trim();
    }

    public string? ProfileId { get; }

    public string Name { get; }

    public string DisplayName => Name;

    public string Summary { get; }

    public override string ToString() => DisplayName;
}
