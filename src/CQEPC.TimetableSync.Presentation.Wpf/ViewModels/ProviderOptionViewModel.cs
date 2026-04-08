using CQEPC.TimetableSync.Domain.Enums;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ProviderOptionViewModel
{
    public ProviderOptionViewModel(ProviderKind provider, string displayName)
    {
        Provider = provider;
        DisplayName = displayName;
    }

    public ProviderKind Provider { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
