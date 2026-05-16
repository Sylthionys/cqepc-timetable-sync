using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Services;

public interface IHomeScheduleRenderCacheStore
{
    Task<HomeScheduleRenderCache?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(HomeScheduleRenderCache cache, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed class HomeScheduleRenderCacheStore : IHomeScheduleRenderCacheStore
{
    private const string ProtectedCacheFileName = "home-schedule-render-cache.bin";
    private const string LegacyPlaintextCacheFileName = "home-schedule-render-cache.json";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CQEPC.TimetableSync.HomeScheduleRenderCache.v1");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string cacheFilePath;
    private readonly string legacyPlaintextCacheFilePath;

    public HomeScheduleRenderCacheStore(LocalStoragePaths storagePaths)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);
        cacheFilePath = Path.Combine(storagePaths.RootDirectory, ProtectedCacheFileName);
        legacyPlaintextCacheFilePath = Path.Combine(storagePaths.RootDirectory, LegacyPlaintextCacheFileName);
    }

    public async Task<HomeScheduleRenderCache?> LoadAsync(CancellationToken cancellationToken)
    {
        DeleteLegacyPlaintextCache();
        if (!File.Exists(cacheFilePath))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(cacheFilePath, cancellationToken);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<HomeScheduleRenderCache>(bytes, SerializerOptions);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(HomeScheduleRenderCache cache, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cache);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath) ?? ".");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cache, SerializerOptions);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(cacheFilePath, protectedBytes, cancellationToken);
        DeleteLegacyPlaintextCache();
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteIfExists(cacheFilePath);
        DeleteLegacyPlaintextCache();
        return Task.CompletedTask;
    }

    private void DeleteLegacyPlaintextCache() => DeleteIfExists(legacyPlaintextCacheFilePath);

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
