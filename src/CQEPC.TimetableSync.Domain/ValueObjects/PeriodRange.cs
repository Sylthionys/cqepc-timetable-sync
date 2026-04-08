namespace CQEPC.TimetableSync.Domain.ValueObjects;

public sealed record PeriodRange
{
    public PeriodRange(int startPeriod, int endPeriod)
    {
        if (startPeriod <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startPeriod), "Start period must be positive.");
        }

        if (endPeriod <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endPeriod), "End period must be positive.");
        }

        if (endPeriod < startPeriod)
        {
            throw new ArgumentException("End period must be greater than or equal to start period.", nameof(endPeriod));
        }

        StartPeriod = startPeriod;
        EndPeriod = endPeriod;
    }

    public int StartPeriod { get; }

    public int EndPeriod { get; }
}
