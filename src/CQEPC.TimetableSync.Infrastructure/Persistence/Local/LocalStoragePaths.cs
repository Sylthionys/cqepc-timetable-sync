namespace CQEPC.TimetableSync.Infrastructure.Persistence.Local;

public sealed class LocalStoragePaths
{
    public const string StorageRootEnvironmentVariable = "CQEPC_TIMETABLESYNC_STORAGE_ROOT";

    public LocalStoragePaths(string? rootDirectory = null)
    {
        var effectiveRootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Environment.GetEnvironmentVariable(StorageRootEnvironmentVariable)
            : rootDirectory;

        RootDirectory = string.IsNullOrWhiteSpace(effectiveRootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CQEPC Timetable Sync")
            : effectiveRootDirectory.Trim();

        SettingsFilePath = Path.Combine(RootDirectory, "user-settings.json");
        WorkspacePreferencesFilePath = Path.Combine(RootDirectory, "workspace-preferences.json");
        LatestSnapshotFilePath = Path.Combine(RootDirectory, "latest-snapshot.json");
        GoogleSyncMappingsFilePath = Path.Combine(RootDirectory, "google-sync-mappings.json");
        MicrosoftSyncMappingsFilePath = Path.Combine(RootDirectory, "microsoft-sync-mappings.json");
        ProviderTokensDirectory = Path.Combine(RootDirectory, "tokens");
        SourcesDirectory = Path.Combine(RootDirectory, "sources");
    }

    public string RootDirectory { get; }

    public string SettingsFilePath { get; }

    public string WorkspacePreferencesFilePath { get; }

    public string LatestSnapshotFilePath { get; }

    public string GoogleSyncMappingsFilePath { get; }

    public string MicrosoftSyncMappingsFilePath { get; }

    public string ProviderTokensDirectory { get; }

    public string SourcesDirectory { get; }
}
