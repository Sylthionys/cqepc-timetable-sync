namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class GoogleTimeZoneOptionViewModel
{
    public GoogleTimeZoneOptionViewModel(string timeZoneId, string displayName)
    {
        TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId)
            ? throw new ArgumentException("Time-zone id cannot be empty.", nameof(timeZoneId))
            : timeZoneId.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? TimeZoneId
            : displayName.Trim();
    }

    public string TimeZoneId { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
