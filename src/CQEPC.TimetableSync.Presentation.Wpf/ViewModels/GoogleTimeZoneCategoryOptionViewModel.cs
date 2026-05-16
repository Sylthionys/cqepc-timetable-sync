using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class GoogleTimeZoneCategoryOptionViewModel
{
    public GoogleTimeZoneCategoryOptionViewModel(WorkspaceTimeZoneRegion region, string displayName)
    {
        Region = region;
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? region.ToString()
            : displayName.Trim();
    }

    public WorkspaceTimeZoneRegion Region { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
