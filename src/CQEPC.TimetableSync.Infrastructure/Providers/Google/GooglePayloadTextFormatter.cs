using System.Globalization;
using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Google;

internal static class GooglePayloadTextFormatter
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
            $"Time: {occurrence.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}-{occurrence.End.ToString("HH:mm", CultureInfo.InvariantCulture)}",
            $"Week: {occurrence.SchoolWeekNumber.ToString(CultureInfo.InvariantCulture)}",
        };

        AddLine(lines, "Campus", occurrence.Metadata.Campus);
        AddLine(lines, "Location", occurrence.Metadata.Location);
        AddLine(lines, "Teacher", occurrence.Metadata.Teacher);
        AddLine(lines, "Teaching Class", occurrence.Metadata.TeachingClassComposition);
        AddLine(lines, "Course Type", occurrence.CourseType);
        AddLine(lines, "Notes", occurrence.Metadata.Notes);
        lines.Add(string.Empty);
        lines.Add($"{GoogleSyncConstants.ManagedByKey}: {GoogleSyncConstants.ManagedByValue}");
        lines.Add($"{GoogleSyncConstants.LocalSyncIdKey}: {localSyncId}");
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
            $"Due date: {occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            $"Reference class time: {occurrence.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}-{occurrence.End.ToString("HH:mm", CultureInfo.InvariantCulture)}",
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
