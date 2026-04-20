using System.Text.Json;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;

internal static class ManualGoogleWorkflowChineseText
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<ManualGoogleWorkflowChineseTextPayload> Payload = new(Load);

    public static string SelectedClassName => Payload.Value.SelectedClassName;

    public static string SportsCourseTitle => Payload.Value.SportsCourseTitle;

    public static string MentalHealthCourseTitle => Payload.Value.MentalHealthCourseTitle;

    public static string ElectromechanicalCourseTitle => Payload.Value.ElectromechanicalCourseTitle;

    public static string CalculusCourseTitle => Payload.Value.CalculusCourseTitle;

    public static string UnresolvedSectionTitle => Payload.Value.UnresolvedSectionTitle;

    private static ManualGoogleWorkflowChineseTextPayload Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "manual-google-workflow.zh-Hans.json");
        var json = File.ReadAllText(path);
        var payload = JsonSerializer.Deserialize<ManualGoogleWorkflowChineseTextPayload>(
            json,
            JsonOptions);

        if (payload is null)
        {
            throw new InvalidOperationException($"Could not load Chinese text fixture from '{path}'.");
        }

        return payload;
    }

    private sealed record ManualGoogleWorkflowChineseTextPayload(
        string SelectedClassName,
        string SportsCourseTitle,
        string MentalHealthCourseTitle,
        string ElectromechanicalCourseTitle,
        string CalculusCourseTitle,
        string UnresolvedSectionTitle);
}
