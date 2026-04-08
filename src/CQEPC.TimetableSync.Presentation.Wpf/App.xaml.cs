using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Infrastructure.Normalization;
using CQEPC.TimetableSync.Infrastructure.Parsing.Pdf;
using CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;
using CQEPC.TimetableSync.Infrastructure.Parsing.Word;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using CQEPC.TimetableSync.Infrastructure.Providers.Google;
using CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;
using CQEPC.TimetableSync.Infrastructure.Sync;
using CQEPC.TimetableSync.Presentation.Wpf.Services;
using CQEPC.TimetableSync.Presentation.Wpf.Shell;
using CQEPC.TimetableSync.Presentation.Wpf.Testing;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF Application lifetime is managed by the framework; async disposables are released in OnExit.")]
public partial class App : System.Windows.Application
{
    private const int StartupFailureExitCode = 1;
    private LocalStoragePaths? storagePaths;
    private StartupDiagnostics? startupDiagnostics;
    private SampleWorkspaceSeeder? uiTestWorkspace;
    private UiAutomationBridge? uiAutomationBridge;
    private ShellViewModel? shellViewModel;
    private AppLaunchOptions? launchOptions;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        try
        {
            launchOptions = AppLaunchOptions.Parse(e.Args);
            var externalStorageRoot = Environment.GetEnvironmentVariable(LocalStoragePaths.StorageRootEnvironmentVariable);
            uiTestWorkspace = launchOptions.IsUiTestMode && string.IsNullOrWhiteSpace(externalStorageRoot)
                ? await SampleWorkspaceSeeder.CreateAsync(launchOptions.FixtureName, CancellationToken.None)
                : null;

            storagePaths = new LocalStoragePaths(uiTestWorkspace?.StorageRoot);
            startupDiagnostics = new StartupDiagnostics(storagePaths);
            Resources = CreateWritableResources(Resources);
            Resources["IsUiAutomationMode"] = launchOptions.IsAutomationMode;

            var effectiveTimeProvider = launchOptions.IsUiTestMode
                ? new DeterministicTimeProvider(
                    DateTimeOffset.Parse("2026-03-16T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                    TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"))
                : TimeProvider.System;

            var localSourceRepository = new JsonLocalSourceCatalogRepository(storagePaths);
            var preferencesRepository = new JsonUserPreferencesRepository(storagePaths);
            var startupPreferences = await preferencesRepository.LoadAsync(CancellationToken.None);
            var localizationService = new LocalizationService(Resources);
            var themeService = new ThemeService(Resources);
            themeService.ApplyTheme(startupPreferences.Appearance.ThemeMode);
            localizationService.ApplyPreferredCulture(
                startupPreferences.Localization.PreferredCultureName,
                exception => startupDiagnostics.ReportUnexpectedFailure("Localization startup", exception, showDialog: false));
            var workspaceRepository = new JsonWorkspaceRepository(storagePaths);
            var syncMappingRepository = new JsonSyncMappingRepository(storagePaths);
            var onboardingService = new LocalSourceOnboardingService(localSourceRepository);
            var timetableParser = new TimetablePdfParser();
            var academicCalendarParser = new TeachingProgressXlsParser();
            var periodTimeProfileParser = new ClassTimeDocxParser();
            var normalizer = new TimetableNormalizer();
            var diffService = new LocalSnapshotSyncDiffService(workspaceRepository);
            var taskGenerationService = new RuleBasedTaskGenerationService();
            var exportGroupBuilder = new ExportGroupBuilder();
            var googleProviderAdapter = new GoogleSyncProviderAdapter(storagePaths);
            var microsoftProviderAdapter = new MicrosoftSyncProviderAdapter(storagePaths);
            var previewService = new WorkspacePreviewService(
                timetableParser,
                academicCalendarParser,
                periodTimeProfileParser,
                normalizer,
                diffService,
                workspaceRepository,
                timeProvider: effectiveTimeProvider,
                taskGenerationService: taskGenerationService,
                syncMappingRepository: syncMappingRepository,
                providerAdapters: [googleProviderAdapter, microsoftProviderAdapter],
                exportGroupBuilder: exportGroupBuilder);
            var filePickerService = new LocalFilePickerService();

            var workspace = new WorkspaceSessionViewModel(
                onboardingService,
                filePickerService,
                preferencesRepository,
                previewService,
                googleProviderAdapter,
                microsoftProviderAdapter,
                localizationService,
                themeService);
            var settingsViewModel = new SettingsPageViewModel(workspace);
            shellViewModel = new ShellViewModel(workspace, settingsViewModel, effectiveTimeProvider);
            var shellWindow = new ShellWindow(shellViewModel);
            if (launchOptions.IsUiTestMode)
            {
                shellWindow.ApplyUiWindowMode(launchOptions.Width, launchOptions.Height, launchOptions.WindowMode);
            }

            MainWindow = shellWindow;
            await shellViewModel.InitializeAsync();
            shellViewModel.CurrentPage = launchOptions.RequestedPage;

            if (launchOptions.IsScreenshotMode && launchOptions.WindowMode == UiWindowMode.RenderOnly)
            {
                var screenshotPath = launchOptions.ScreenshotPath
                    ?? throw new InvalidOperationException("UI test mode requires a screenshot output path.");

                try
                {
                    using var renderOnlyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    await UiScreenshotExporter.ExportPageWithoutShowingAsync(
                        shellWindow,
                        UiScreenshotExporter.GetAutomationIdForPage(launchOptions.RequestedPage),
                        screenshotPath,
                        renderOnlyTimeout.Token);
                    Shutdown(0);
                    return;
                }
                catch (Exception) when (!Debugger.IsAttached)
                {
                    shellWindow.ApplyUiWindowMode(launchOptions.Width, launchOptions.Height, UiWindowMode.Background);
                }
            }

            shellWindow.Show();

            if (launchOptions.IsAutomationMode)
            {
                uiAutomationBridge = new UiAutomationBridge(shellWindow);
            }

            if (launchOptions.IsScreenshotMode)
            {
                var screenshotPath = launchOptions.ScreenshotPath
                    ?? throw new InvalidOperationException("UI test mode requires a screenshot output path.");
                await UiScreenshotExporter.ExportPageAsync(
                    shellWindow,
                    UiScreenshotExporter.GetAutomationIdForPage(launchOptions.RequestedPage),
                    screenshotPath,
                    CancellationToken.None);
                Shutdown(0);
            }
        }
        catch (Exception exception)
        {
            var logPath = startupDiagnostics?.ReportStartupFailure("Application startup", exception);
            Console.Error.WriteLine(exception);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                Console.Error.WriteLine($"Diagnostic log: {logPath}");
            }
            Shutdown(StartupFailureExitCode);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        shellViewModel?.FlushAsync().GetAwaiter().GetResult();

        if (uiAutomationBridge is not null)
        {
            uiAutomationBridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
            uiAutomationBridge = null;
        }

        if (e.ApplicationExitCode == 0)
        {
            uiTestWorkspace?.Dispose();
        }

        uiTestWorkspace = null;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (startupDiagnostics is null)
        {
            return;
        }

        startupDiagnostics.ReportUnexpectedFailure(
            "Dispatcher unhandled exception",
            e.Exception,
            showDialog: StartupDiagnostics.ShouldShowDevelopmentDialog());
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (startupDiagnostics is null)
        {
            return;
        }

        startupDiagnostics.ReportUnexpectedFailure(
            "AppDomain unhandled exception",
            StartupDiagnostics.NormalizeException(e.ExceptionObject),
            showDialog: StartupDiagnostics.ShouldShowDevelopmentDialog());
    }

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (startupDiagnostics is null)
        {
            return;
        }

        startupDiagnostics.ReportUnexpectedFailure(
            "TaskScheduler unobserved task exception",
            e.Exception,
            showDialog: false);
    }

    private static ResourceDictionary CreateWritableResources(ResourceDictionary source)
    {
        var writable = new ResourceDictionary();

        foreach (var key in source.Keys)
        {
            writable[key] = CloneResourceValue(source[key]);
        }

        foreach (var dictionary in source.MergedDictionaries)
        {
            writable.MergedDictionaries.Add(CreateWritableResources(dictionary));
        }

        return writable;
    }

    private static object? CloneResourceValue(object? value) =>
        value is Freezable freezable
            ? freezable.Clone()
            : value;

}
