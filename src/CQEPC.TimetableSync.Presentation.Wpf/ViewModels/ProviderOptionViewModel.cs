using CQEPC.TimetableSync.Domain.Enums;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ProviderOptionViewModel
{
    public ProviderOptionViewModel(ProviderKind provider, string displayName, string? connectedAccountSummary = null)
    {
        Provider = provider;
        ProviderDisplayName = displayName;
        ConnectedAccountSummary = connectedAccountSummary;
        DisplayName = string.IsNullOrWhiteSpace(connectedAccountSummary)
            ? displayName
            : $"{displayName}: {connectedAccountSummary.Trim()}";
    }

    public ProviderKind Provider { get; }

    public string ProviderDisplayName { get; }

    public string? ConnectedAccountSummary { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
