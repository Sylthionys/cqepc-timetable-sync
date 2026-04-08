using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.UseCases.Onboarding;

public enum LocalSourceFileKind
{
    TimetablePdf,
    TeachingProgressXls,
    ClassTimeDocx,
}

public enum SourceImportStatus
{
    Missing,
    Ready,
    NeedsAttention,
}

public enum SourceParseStatus
{
    WaitingForFile,
    Available,
    PendingParserImplementation,
    Blocked,
}

public enum SourceStorageMode
{
    ReferencePath,
    AppLocalCopy,
}

public enum SourceAttentionReason
{
    None,
    ExtensionMismatch,
    MissingFile,
}

public enum CatalogActivityKind
{
    SelectedFile,
    SkippedDuplicateMatches,
    IgnoredUnsupportedFiles,
    RejectedExtensionMismatch,
    RemovedFile,
    ResetUnreadableState,
}

public sealed record CatalogActivityEntry
{
    public CatalogActivityEntry(
        CatalogActivityKind kind,
        LocalSourceFileKind? fileKind = null,
        int? count = null,
        string? expectedExtension = null,
        string? actualExtension = null)
    {
        Kind = kind;
        FileKind = fileKind;
        Count = count;
        ExpectedExtension = Normalize(expectedExtension);
        ActualExtension = Normalize(actualExtension);
    }

    public CatalogActivityKind Kind { get; }

    public LocalSourceFileKind? FileKind { get; }

    public int? Count { get; }

    public string? ExpectedExtension { get; }

    public string? ActualExtension { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record LocalSourceFileState
{
    public LocalSourceFileState(
        LocalSourceFileKind kind,
        string? fullPath,
        string? displayName,
        string? fileExtension,
        long? fileSizeBytes,
        DateTimeOffset? lastWriteTimeUtc,
        DateTimeOffset? lastSelectedUtc,
        SourceImportStatus importStatus,
        SourceParseStatus parseStatus,
        SourceStorageMode storageMode,
        SourceAttentionReason attentionReason = SourceAttentionReason.None)
    {
        Kind = kind;
        FullPath = Normalize(fullPath);
        DisplayName = Normalize(displayName);
        FileExtension = Normalize(fileExtension);
        FileSizeBytes = fileSizeBytes;
        LastWriteTimeUtc = lastWriteTimeUtc;
        LastSelectedUtc = lastSelectedUtc;
        ImportStatus = importStatus;
        ParseStatus = parseStatus;
        StorageMode = storageMode;
        AttentionReason = attentionReason;
    }

    public LocalSourceFileKind Kind { get; init; }

    public string? FullPath { get; init; }

    public string? DisplayName { get; init; }

    public string? FileExtension { get; init; }

    public long? FileSizeBytes { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    public DateTimeOffset? LastSelectedUtc { get; init; }

    public SourceImportStatus ImportStatus { get; init; }

    public SourceParseStatus ParseStatus { get; init; }

    public SourceStorageMode StorageMode { get; init; }

    public SourceAttentionReason AttentionReason { get; init; }

    public bool HasSelection => !string.IsNullOrWhiteSpace(FullPath);

    public bool IsReady => ImportStatus == SourceImportStatus.Ready;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record LocalSourceCatalogState
{
    private static readonly LocalSourceFileKind[] RequiredKinds =
    [
        LocalSourceFileKind.TimetablePdf,
        LocalSourceFileKind.TeachingProgressXls,
        LocalSourceFileKind.ClassTimeDocx,
    ];

    public LocalSourceCatalogState(
        IReadOnlyList<LocalSourceFileState> files,
        string? lastUsedFolder,
        IReadOnlyList<CatalogActivityEntry>? activities = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        var normalizedFiles = RequiredKinds
            .Select(kind => files.SingleOrDefault(file => file.Kind == kind) ?? LocalSourceCatalogDefaults.CreateEmptyFile(kind))
            .ToArray();

        Files = normalizedFiles;
        LastUsedFolder = Normalize(lastUsedFolder);
        Activities = (activities ?? Array.Empty<CatalogActivityEntry>()).ToArray();
        MissingRequiredFiles = normalizedFiles
            .Where(static file => !file.IsReady)
            .Select(static file => file.Kind)
            .ToArray();
        HasAllRequiredFiles = MissingRequiredFiles.Count == 0;
        HasAnySelection = normalizedFiles.Any(static file => file.HasSelection);
    }

    public IReadOnlyList<LocalSourceFileState> Files { get; }

    public string? LastUsedFolder { get; }

    public IReadOnlyList<CatalogActivityEntry> Activities { get; }

    public IReadOnlyList<LocalSourceFileKind> MissingRequiredFiles { get; }

    public bool HasAllRequiredFiles { get; }

    public bool HasAnySelection { get; }

    public LocalSourceFileState GetFile(LocalSourceFileKind kind) =>
        Files.Single(file => file.Kind == kind);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class LocalSourceCatalogDefaults
{
    public static LocalSourceCatalogState CreateEmptyCatalog(
        string? lastUsedFolder = null,
        IReadOnlyList<CatalogActivityEntry>? activities = null) =>
        new(
            LocalSourceCatalogMetadata.RequiredKinds.Select(CreateEmptyFile).ToArray(),
            lastUsedFolder,
            activities);

    public static LocalSourceFileState CreateEmptyFile(LocalSourceFileKind kind) =>
        new(
            kind,
            fullPath: null,
            displayName: null,
            fileExtension: null,
            fileSizeBytes: null,
            lastWriteTimeUtc: null,
            lastSelectedUtc: null,
            importStatus: SourceImportStatus.Missing,
            parseStatus: SourceParseStatus.WaitingForFile,
            storageMode: SourceStorageMode.ReferencePath,
            attentionReason: SourceAttentionReason.None);
}

public static class LocalSourceCatalogMetadata
{
    public static IReadOnlyList<LocalSourceFileKind> RequiredKinds { get; } =
    [
        LocalSourceFileKind.TimetablePdf,
        LocalSourceFileKind.TeachingProgressXls,
        LocalSourceFileKind.ClassTimeDocx,
    ];

    public static string GetDisplayName(LocalSourceFileKind kind) =>
        kind switch
        {
            LocalSourceFileKind.TimetablePdf => "Timetable PDF",
            LocalSourceFileKind.TeachingProgressXls => "Teaching Progress XLS",
            LocalSourceFileKind.ClassTimeDocx => "Class-Time DOCX",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown local source file kind."),
        };

    public static string GetShortDescription(LocalSourceFileKind kind) =>
        kind switch
        {
            LocalSourceFileKind.TimetablePdf => "Regular class blocks",
            LocalSourceFileKind.TeachingProgressXls => "Semester week-date mapping",
            LocalSourceFileKind.ClassTimeDocx => "Period-time profiles",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown local source file kind."),
        };

    public static string GetExpectedExtension(LocalSourceFileKind kind) =>
        kind switch
        {
            LocalSourceFileKind.TimetablePdf => ".pdf",
            LocalSourceFileKind.TeachingProgressXls => ".xls",
            LocalSourceFileKind.ClassTimeDocx => ".docx",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown local source file kind."),
        };

    public static string GetFileDialogFilter(LocalSourceFileKind kind)
    {
        var extension = GetExpectedExtension(kind);
        return $"{GetDisplayName(kind)} (*{extension})|*{extension}";
    }

    public static bool TryResolveKind(string filePath, out LocalSourceFileKind kind)
    {
        var extension = Path.GetExtension(filePath);
        foreach (var candidate in RequiredKinds)
        {
            if (string.Equals(extension, GetExpectedExtension(candidate), StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    public static string GetImportStatusText(SourceImportStatus status) =>
        status switch
        {
            SourceImportStatus.Missing => "Missing",
            SourceImportStatus.Ready => "Ready",
            SourceImportStatus.NeedsAttention => "Needs attention",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown source import status."),
        };

    public static string GetParseStatusText(SourceParseStatus status) =>
        status switch
        {
            SourceParseStatus.WaitingForFile => "Waiting for file",
            SourceParseStatus.Available => "Parser available",
            SourceParseStatus.PendingParserImplementation => "Pending parser implementation",
            SourceParseStatus.Blocked => "Blocked",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown source parse status."),
        };

    public static string GetAllFilesFilter() =>
        "Supported timetable sources (*.pdf;*.xls;*.docx)|*.pdf;*.xls;*.docx|Timetable PDF (*.pdf)|*.pdf|Teaching Progress XLS (*.xls)|*.xls|Class-Time DOCX (*.docx)|*.docx";
}
