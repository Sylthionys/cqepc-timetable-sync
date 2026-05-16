using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Infrastructure.Normalization;
using CQEPC.TimetableSync.Infrastructure.Networking;
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
    private Task? deferredInitializationTask;
    private readonly CancellationTokenSource shutdownCancellation = new();
    private bool shellWindowCloseAllowed;
    private bool shutdownPreparationStarted;
    private bool shutdownPreparationCompleted;

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
            var activeNetworkProxySettings = startupPreferences.ProgramBehavior.NetworkProxy;
            var networkProxySecretStore = new DpapiNetworkProxySecretStore(storagePaths);
            var activeNetworkProxyPassword = await networkProxySecretStore
                .GetPasswordAsync(activeNetworkProxySettings, CancellationToken.None)
                .ConfigureAwait(true);
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
            NetworkProxySettings GetActiveNetworkProxySettings() => activeNetworkProxySettings;
            string? GetActiveNetworkProxyPassword() => activeNetworkProxyPassword;
            var googleProviderAdapter = new GoogleSyncProviderAdapter(
                storagePaths,
                networkProxySettingsProvider: GetActiveNetworkProxySettings,
                networkProxyPasswordProvider: GetActiveNetworkProxyPassword);
            var microsoftProviderAdapter = new MicrosoftSyncProviderAdapter(
                storagePaths,
                networkProxySettingsProvider: GetActiveNetworkProxySettings,
                networkProxyPasswordProvider: GetActiveNetworkProxyPassword);
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
            var homeScheduleRenderCacheStore = new HomeScheduleRenderCacheStore(storagePaths);

            var workspace = new WorkspaceSessionViewModel(
                onboardingService,
                filePickerService,
                preferencesRepository,
                previewService,
                googleProviderAdapter,
                microsoftProviderAdapter,
                localizationService,
                themeService,
                homeScheduleRenderCacheStore: homeScheduleRenderCacheStore,
                networkProxySecretStore: networkProxySecretStore,
                networkProxyConnectionTester: new NetworkProxyConnectionTester(),
                networkProxySettingsChanged: (settings, password) =>
                {
                    activeNetworkProxySettings = settings;
                    activeNetworkProxyPassword = password;
                });
            var settingsViewModel = new SettingsPageViewModel(workspace);
            shellViewModel = new ShellViewModel(workspace, settingsViewModel, effectiveTimeProvider);
            var shellWindow = new ShellWindow(shellViewModel);
            shellWindow.Closing += HandleShellWindowClosing;
            shellWindow.UpdateTitleBarTheme(themeService.ActiveTheme);
            themeService.ThemeChanging += (_, args) => shellWindow.PrepareThemeTransition(args.NewTheme);
            themeService.ThemeChanged += (_, _) =>
            {
                shellWindow.UpdateTitleBarTheme(themeService.ActiveTheme);
                shellWindow.PlayThemeTransition(themeService.ActiveTheme);
            };
            if (launchOptions.IsUiTestMode)
            {
                shellWindow.ApplyUiWindowMode(launchOptions.Width, launchOptions.Height, launchOptions.WindowMode);
            }

            MainWindow = shellWindow;
            shellViewModel.CurrentPage = launchOptions.RequestedPage;

            if (launchOptions.UseDeferredInteractiveInitialization)
            {
                shellWindow.Show();
                deferredInitializationTask = CompleteDeferredInitializationAsync(shellViewModel, shutdownCancellation.Token);
                return;
            }

            var showShellBeforeInitialization = !launchOptions.IsScreenshotMode;
            if (showShellBeforeInitialization)
            {
                shellWindow.Show();
            }

            await shellViewModel.InitializeAsync(shutdownCancellation.Token);

            if (!showShellBeforeInitialization)
            {
                shellWindow.Show();
                ForceRequestedPageMaterialization(shellViewModel, launchOptions.RequestedPage);
                await WaitForInitialShellLayoutAsync(shellWindow);
            }

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
                ShutdownApplication(0);
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
            ShutdownApplication(StartupFailureExitCode);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!shutdownPreparationCompleted)
        {
            PrepareForExitSynchronously(flush: e.ApplicationExitCode == 0);
        }

        shellViewModel?.Dispose();

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
        shutdownCancellation.Dispose();
        base.OnExit(e);
    }

    private async void HandleShellWindowClosing(object? sender, CancelEventArgs e)
    {
        if (shellWindowCloseAllowed || launchOptions?.IsScreenshotMode == true)
        {
            return;
        }

        e.Cancel = true;

        if (shutdownPreparationStarted)
        {
            return;
        }

        shutdownPreparationStarted = true;
        if (sender is Window window)
        {
            window.IsEnabled = false;
        }

        try
        {
            await PrepareForExitAsync(flush: true).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            startupDiagnostics?.ReportUnexpectedFailure("Application shutdown", exception, showDialog: false);
        }
        finally
        {
            shutdownPreparationCompleted = true;
            shellWindowCloseAllowed = true;
            ShutdownApplication(0);
        }
    }

    private void ShutdownApplication(int exitCode)
    {
        shellWindowCloseAllowed = true;
        Shutdown(exitCode);
    }

    private async Task PrepareForExitAsync(bool flush)
    {
        shutdownCancellation.Cancel();
        var canFlush = await CompleteDeferredInitializationForExitAsync().ConfigureAwait(true);

        if (flush && canFlush && shellViewModel is not null)
        {
            await shellViewModel.FlushAsync().ConfigureAwait(true);
        }
    }

    private void PrepareForExitSynchronously(bool flush)
    {
        shutdownCancellation.Cancel();
        var canFlush = CompleteDeferredInitializationForExit();

        if (flush && canFlush && shellViewModel is not null)
        {
            var flushTask = shellViewModel.FlushAsync();
            try
            {
                if (flushTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    flushTask.GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                startupDiagnostics?.ReportUnexpectedFailure("Application shutdown", exception, showDialog: false);
            }
        }

        shutdownPreparationCompleted = true;
    }

    private async Task<bool> CompleteDeferredInitializationForExitAsync()
    {
        if (deferredInitializationTask is null)
        {
            return true;
        }

        var initializationTask = deferredInitializationTask;
        if (!initializationTask.IsCompleted)
        {
            var completedTask = await Task.WhenAny(
                    initializationTask,
                    Task.Delay(TimeSpan.FromSeconds(2)))
                .ConfigureAwait(true);
            if (!ReferenceEquals(completedTask, initializationTask))
            {
                return false;
            }
        }

        var completed = ObserveDeferredInitializationCompletion(initializationTask);
        if (completed)
        {
            deferredInitializationTask = null;
        }

        return completed;
    }

    private bool CompleteDeferredInitializationForExit()
    {
        if (deferredInitializationTask is null)
        {
            return true;
        }

        if (!deferredInitializationTask.IsCompleted)
        {
            try
            {
                deferredInitializationTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException) when (shutdownCancellation.IsCancellationRequested)
            {
            }
        }

        var completed = deferredInitializationTask.IsCompleted;
        if (completed)
        {
            completed = ObserveDeferredInitializationCompletion(deferredInitializationTask);
        }

        if (completed)
        {
            deferredInitializationTask = null;
        }

        return completed;
    }

    private bool ObserveDeferredInitializationCompletion(Task initializationTask)
    {
        try
        {
            initializationTask.GetAwaiter().GetResult();
            return true;
        }
        catch (OperationCanceledException) when (shutdownCancellation.IsCancellationRequested)
        {
            return true;
        }
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

    private static async Task WaitForInitialShellLayoutAsync(Window window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
        await window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void ForceRequestedPageMaterialization(ShellViewModel shellViewModel, ShellPage requestedPage)
    {
        shellViewModel.CurrentPage = requestedPage == ShellPage.Home
            ? ShellPage.Import
            : ShellPage.Home;
        shellViewModel.CurrentPage = requestedPage;
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

    private async Task CompleteDeferredInitializationAsync(ShellViewModel shell, CancellationToken cancellationToken)
    {
        try
        {
            await shell.InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var logPath = startupDiagnostics?.ReportStartupFailure("Deferred application startup", exception);
            Console.Error.WriteLine(exception);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                Console.Error.WriteLine($"Diagnostic log: {logPath}");
            }

            ShutdownApplication(StartupFailureExitCode);
        }
    }

}
