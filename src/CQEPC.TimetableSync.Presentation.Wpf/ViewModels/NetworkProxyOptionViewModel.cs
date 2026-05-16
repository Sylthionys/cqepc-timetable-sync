using CQEPC.TimetableSync.Application.UseCases.Workspace;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class NetworkProxyOptionViewModel
{
    public NetworkProxyOptionViewModel(NetworkProxyMode mode, string displayName)
    {
        Mode = mode;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? mode.ToString() : displayName.Trim();
    }

    public NetworkProxyMode Mode { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
