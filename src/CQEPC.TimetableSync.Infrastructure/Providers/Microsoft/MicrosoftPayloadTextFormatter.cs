using System.Globalization;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;

internal static class MicrosoftPayloadTextFormatter
{
    public static string BuildEventDescription(ResolvedOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        var localSyncId = SyncIdentity.CreateOccurrenceId(occurrence);
        var lines = new List<string>
        {
            occurrence.Metadata.CourseTitle,
            $"Class: {occurrence.ClassName}",
            $"Date: {occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"Time: {occurrence.Start:HH:mm}-{occurrence.End:HH:mm}",
            $"Week: {occurrence.SchoolWeekNumber.ToString(CultureInfo.InvariantCulture)}",
        };

        AddLine(lines, "Campus", occurrence.Metadata.Campus);
        AddLine(lines, "Location", occurrence.Metadata.Location);
        AddLine(lines, "Teacher", occurrence.Metadata.Teacher);
        AddLine(lines, "Teaching Class", occurrence.Metadata.TeachingClassComposition);
        AddLine(lines, "Course Type", occurrence.CourseType);
        AddLine(lines, "Notes", occurrence.Metadata.Notes);
        lines.Add(string.Empty);
        lines.Add($"{MicrosoftSyncConstants.ManagedByKey}: {MicrosoftSyncConstants.ManagedByValue}");
        lines.Add($"{MicrosoftSyncConstants.LocalSyncIdKey}: {localSyncId}");
        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildTaskNotes(ResolvedOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        var lines = new List<string>
        {
            "Task generated from CQEPC timetable sync",
            $"Class: {occurrence.ClassName}",
            $"Course: {occurrence.Metadata.CourseTitle}",
            $"Scheduled time: {occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {occurrence.Start:HH:mm}-{occurrence.End:HH:mm}",
        };

        AddLine(lines, "Location", occurrence.Metadata.Location);
        AddLine(lines, "Teacher", occurrence.Metadata.Teacher);
        AddLine(lines, "Notes", occurrence.Metadata.Notes);
        lines.Add($"Local sync id: {SyncIdentity.CreateOccurrenceId(occurrence)}");
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddLine(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value.Trim()}");
        }
    }
}
