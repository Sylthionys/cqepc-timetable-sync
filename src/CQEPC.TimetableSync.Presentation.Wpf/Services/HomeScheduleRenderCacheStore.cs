using System.IO;
using System.Text.Json;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

public interface IHomeScheduleRenderCacheStore
{
    Task<HomeScheduleRenderCache?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(HomeScheduleRenderCache cache, CancellationToken cancellationToken);
}

public sealed class HomeScheduleRenderCacheStore : IHomeScheduleRenderCacheStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string cacheFilePath;

    public HomeScheduleRenderCacheStore(LocalStoragePaths storagePaths)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);
        cacheFilePath = Path.Combine(storagePaths.RootDirectory, "home-schedule-render-cache.json");
    }

    public async Task<HomeScheduleRenderCache?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(cacheFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(cacheFilePath);
        return await JsonSerializer.DeserializeAsync<HomeScheduleRenderCache>(stream, SerializerOptions, cancellationToken);
    }

    public async Task SaveAsync(HomeScheduleRenderCache cache, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cache);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath) ?? ".");
        await using var stream = File.Create(cacheFilePath);
        await JsonSerializer.SerializeAsync(stream, cache, SerializerOptions, cancellationToken);
    }
}

public sealed record HomeScheduleRenderCache(
    DateTimeOffset SavedAt,
    string? ClassName,
    ProviderKind Provider,
    int UnresolvedItemCount,
    IReadOnlyList<HomeScheduleRenderCacheItem> Items)
{
    public static HomeScheduleRenderCache Create(
        DateTimeOffset savedAt,
        string? className,
        ProviderKind provider,
        int unresolvedItemCount,
        IReadOnlyList<AgendaOccurrenceViewModel> items) =>
        new(
            savedAt,
            className,
            provider,
            unresolvedItemCount,
            items.Select(HomeScheduleRenderCacheItem.Create).ToArray());

    public AgendaOccurrenceViewModel[] ToAgendaItems() =>
        Items
            .Select(static item => new AgendaOccurrenceViewModel(
                item.OccurrenceDate,
                item.SchoolWeekNumber,
                item.Title,
                item.TimeRange,
                item.Location,
                item.Teacher,
                item.ColorDotHex,
                item.Details,
                item.Status,
                item.Source,
                item.Origin,
                item.VisualStyle,
                canOpenRemoteEditor: false))
            .ToArray();
}

public sealed record HomeScheduleRenderCacheItem(
    DateOnly OccurrenceDate,
    int? SchoolWeekNumber,
    string Title,
    string TimeRange,
    string Location,
    string Teacher,
    string ColorDotHex,
    string Details,
    HomeScheduleEntryStatus Status,
    SyncChangeSource Source,
    HomeScheduleEntryOrigin Origin,
    HomeCalendarVisualStyle VisualStyle)
{
    public static HomeScheduleRenderCacheItem Create(AgendaOccurrenceViewModel item) =>
        new(
            item.OccurrenceDate,
            item.SchoolWeekNumber,
            item.Title,
            item.TimeRange,
            item.Location,
            item.Teacher,
            item.ColorDotHex,
            item.Details,
            item.Status,
            item.Source,
            item.Origin,
            item.VisualStyle);
}
