namespace CQEPC.TimetableSync.Presentation.Wpf.Testing;

internal sealed class DeterministicTimeProvider : TimeProvider
{
    private readonly DateTimeOffset utcNow;
    private readonly TimeZoneInfo localTimeZone;

    public DeterministicTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone)
    {
        this.utcNow = utcNow;
        this.localTimeZone = localTimeZone;
    }

    public override DateTimeOffset GetUtcNow() => utcNow;

    public override TimeZoneInfo LocalTimeZone => localTimeZone;
}
