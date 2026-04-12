namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class GoogleCalendarColorOptionViewModel
{
    public GoogleCalendarColorOptionViewModel(string? colorId, string displayName, string colorHex)
    {
        ColorId = string.IsNullOrWhiteSpace(colorId) ? null : colorId.Trim();
        DisplayName = displayName;
        ColorHex = colorHex;
    }

    public string? ColorId { get; }

    public string DisplayName { get; }

    public string ColorHex { get; }
}
