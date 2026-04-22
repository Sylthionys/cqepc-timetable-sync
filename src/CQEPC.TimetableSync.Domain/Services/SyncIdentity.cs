using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CQEPC.TimetableSync.Domain.Model;

public static class SyncIdentity
{
    public static string CreateOccurrenceId(ResolvedOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return CreateHash(
            "occ",
            occurrence.TargetKind,
            occurrence.ClassName,
            occurrence.SourceFingerprint.SourceKind,
            occurrence.SourceFingerprint.Hash,
            occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurrence.Start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            occurrence.End.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            occurrence.Metadata.CourseTitle,
            occurrence.Metadata.Location ?? string.Empty,
            occurrence.Metadata.Teacher ?? string.Empty,
            occurrence.TimeProfileId);
    }

    public static string CreateExportGroupId(ExportGroup exportGroup)
    {
        ArgumentNullException.ThrowIfNull(exportGroup);

        return CreateHash(
            "grp",
            exportGroup.GroupKind,
            exportGroup.RecurrenceIntervalDays ?? 0,
            string.Join("|", exportGroup.Occurrences.Select(CreateOccurrenceId)));
    }

    public static string CreateTaskRuleFingerprint(string ruleId, ResolvedOccurrence sourceOccurrence)
    {
        ArgumentNullException.ThrowIfNull(sourceOccurrence);

        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new ArgumentException("Rule id cannot be empty.", nameof(ruleId));
        }

        return CreateHash(
            "task",
            ruleId.Trim(),
            CreateOccurrenceId(sourceOccurrence));
    }

    private static string CreateHash(params object[] parts)
    {
        var input = string.Join("|", parts.Select(static part => Convert.ToString(part, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
