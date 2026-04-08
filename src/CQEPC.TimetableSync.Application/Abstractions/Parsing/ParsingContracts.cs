using CQEPC.TimetableSync.Domain.Model;

namespace CQEPC.TimetableSync.Application.Abstractions.Parsing;

public enum ParseDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ParseWarning
{
    public ParseWarning(string message, string? code = null, string? sourceAnchor = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Warning message cannot be empty.", nameof(message));
        }

        Message = message.Trim();
        Code = Normalize(code);
        SourceAnchor = Normalize(sourceAnchor);
    }

    public string Message { get; }

    public string? Code { get; }

    public string? SourceAnchor { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ParseDiagnostic
{
    public ParseDiagnostic(ParseDiagnosticSeverity severity, string code, string message, string? sourceAnchor = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Diagnostic code cannot be empty.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Diagnostic message cannot be empty.", nameof(message));
        }

        Severity = severity;
        Code = code.Trim();
        Message = message.Trim();
        SourceAnchor = Normalize(sourceAnchor);
    }

    public ParseDiagnosticSeverity Severity { get; }

    public string Code { get; }

    public string Message { get; }

    public string? SourceAnchor { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ParserResult<T>
{
    public ParserResult(
        T payload,
        IReadOnlyList<ParseWarning>? warnings = null,
        IReadOnlyList<UnresolvedItem>? unresolvedItems = null,
        IReadOnlyList<ParseDiagnostic>? diagnostics = null)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Warnings = warnings?.ToArray() ?? Array.Empty<ParseWarning>();
        UnresolvedItems = unresolvedItems?.ToArray() ?? Array.Empty<UnresolvedItem>();
        Diagnostics = diagnostics?.ToArray() ?? Array.Empty<ParseDiagnostic>();
    }

    public T Payload { get; }

    public IReadOnlyList<ParseWarning> Warnings { get; }

    public IReadOnlyList<UnresolvedItem> UnresolvedItems { get; }

    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; }
}

public interface IAcademicCalendarParser
{
    Task<ParserResult<IReadOnlyList<SchoolWeek>>> ParseAsync(
        string filePath,
        DateOnly? firstWeekStartOverride,
        CancellationToken cancellationToken);
}

public interface IPeriodTimeProfileParser
{
    Task<ParserResult<IReadOnlyList<TimeProfile>>> ParseAsync(
        string filePath,
        CancellationToken cancellationToken);
}

public interface ITimetableParser
{
    Task<ParserResult<IReadOnlyList<ClassSchedule>>> ParseAsync(
        string filePath,
        CancellationToken cancellationToken);
}
