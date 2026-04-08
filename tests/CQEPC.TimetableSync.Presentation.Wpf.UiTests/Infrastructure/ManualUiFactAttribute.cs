using Xunit;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;

public sealed class ManualUiFactAttribute : StaFactAttribute
{
    internal const string EnableVariableName = "CQEPC_RUN_MANUAL_UI_TESTS";

    public ManualUiFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = $"Manual UI test skipped by default. Set {EnableVariableName}=1 to run tests that require real local storage and provider access.";
        }
    }

    private static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnableVariableName);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
