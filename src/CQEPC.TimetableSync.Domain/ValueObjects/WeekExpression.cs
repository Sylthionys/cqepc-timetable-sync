namespace CQEPC.TimetableSync.Domain.ValueObjects;

public sealed record WeekExpression
{
    public WeekExpression(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new ArgumentException("Week expression cannot be empty.", nameof(rawText));
        }

        RawText = rawText.Trim();
    }

    public string RawText { get; }

    public override string ToString() => RawText;
}
