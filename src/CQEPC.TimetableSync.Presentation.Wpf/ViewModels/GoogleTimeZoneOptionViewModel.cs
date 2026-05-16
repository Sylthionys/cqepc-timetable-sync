namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class GoogleTimeZoneOptionViewModel
{
    public GoogleTimeZoneOptionViewModel(
        string timeZoneId,
        string displayName,
        string searchText = "",
        CQEPC.TimetableSync.Application.UseCases.Workspace.WorkspaceTimeZoneRegion region =
            CQEPC.TimetableSync.Application.UseCases.Workspace.WorkspaceTimeZoneRegion.Common,
        string? localizedDisplayName = null)
    {
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId)
            ? throw new ArgumentException("Time-zone id cannot be empty.", nameof(timeZoneId))
            : timeZoneId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? TimeZoneId
            : displayName.Trim();
        LocalizedDisplayName = string.IsNullOrWhiteSpace(localizedDisplayName)
            ? DisplayName
            : localizedDisplayName.Trim();
        SearchText = string.IsNullOrWhiteSpace(searchText)
            ? $"{TimeZoneId} {DisplayName} {LocalizedDisplayName}"
            : searchText.Trim();
        Region = region;
    }

    public string TimeZoneId { get; }

    public string DisplayName { get; }

    public string LocalizedDisplayName { get; }

    public string SearchText { get; }

    public CQEPC.TimetableSync.Application.UseCases.Workspace.WorkspaceTimeZoneRegion Region { get; }

    public override string ToString() => DisplayName;
}
