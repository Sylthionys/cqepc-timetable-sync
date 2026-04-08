using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiAutomationTestCollectionDefinition
{
    public const string Name = "UI automation";
}
