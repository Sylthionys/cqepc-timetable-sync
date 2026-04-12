using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Globalization;
using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

namespace CQEPC.TimetableSync.Presentation.Wpf.Testing;

internal sealed class UiAutomationBridge : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Window window;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task listenerTask;

    public UiAutomationBridge(Window window)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        PipeName = BuildPipeName(Environment.ProcessId);
        listenerTask = Task.Run(() => ListenAsync(shutdown.Token));
    }

    public string PipeName { get; }

    public static string BuildPipeName(int processId) =>
        $"CQEPC.TimetableSync.UiAutomation.{processId}";

    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();

        try
        {
            await listenerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            shutdown.Dispose();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var response = await HandleRequestAsync(server, cancellationToken).ConfigureAwait(false);
            await WriteMessageAsync(server, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<UiAutomationBridgeResponse> HandleRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var request = await ReadMessageAsync<UiAutomationBridgeRequest>(stream, cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return new UiAutomationBridgeResponse(false, "The automation screenshot request was incomplete.");
            }

            switch (request.Action)
            {
                case "capture":
                    if (string.IsNullOrWhiteSpace(request.AutomationId)
                        || string.IsNullOrWhiteSpace(request.OutputPath))
                    {
                        return new UiAutomationBridgeResponse(false, "The automation capture request was incomplete.");
                    }

                    await window.Dispatcher.InvokeAsync(
                        () => UiScreenshotExporter.ExportAutomationElementAsync(
                            window,
                            request.AutomationId,
                            request.OutputPath,
                            cancellationToken)).Task.Unwrap().ConfigureAwait(false);
                    break;
                case "open-about":
                    await ExecuteUiActionAsync(OpenAboutOverlay, cancellationToken).ConfigureAwait(false);
                    break;
                case "close-about":
                    await ExecuteUiActionAsync(CloseAboutOverlay, cancellationToken).ConfigureAwait(false);
                    break;
                case "open-first-home-course-editor":
                    await ExecuteUiActionAsync(OpenFirstHomeCourseEditor, cancellationToken).ConfigureAwait(false);
                    break;
                case "open-date-picker-dropdown":
                    if (string.IsNullOrWhiteSpace(request.AutomationId))
                    {
                        return new UiAutomationBridgeResponse(false, "The date-picker dropdown request was incomplete.");
                    }

                    await ExecuteUiActionAsync(
                        () => OpenDatePickerDropdown(request.AutomationId),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "select-combo-index":
                    if (string.IsNullOrWhiteSpace(request.AutomationId)
                        || !request.Index.HasValue)
                    {
                        return new UiAutomationBridgeResponse(false, "The combo-box selection request was incomplete.");
                    }

                    await ExecuteUiActionAsync(
                        () => SelectComboBoxItemByIndex(request.AutomationId, request.Index.Value),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "set-first-week-start":
                    if (string.IsNullOrWhiteSpace(request.Value))
                    {
                        return new UiAutomationBridgeResponse(false, "The first-week-start request was incomplete.");
                    }

                    await ExecuteUiActionAsync(
                        () => SetFirstWeekStartOverride(request.Value),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "get-first-week-start":
                    return await ExecuteUiFuncAsync(GetFirstWeekStartOverrideState, cancellationToken).ConfigureAwait(false);
                case "get-selected-class-state":
                    return await ExecuteUiFuncAsync(GetSelectedClassState, cancellationToken).ConfigureAwait(false);
                case "get-planned-change-state":
                    return await ExecuteUiFuncAsync(GetPlannedChangeState, cancellationToken).ConfigureAwait(false);
                case "get-preview-occurrence-state":
                    return await ExecuteUiFuncAsync(GetPreviewOccurrenceState, cancellationToken).ConfigureAwait(false);
                case "get-home-selected-day-state":
                    return await ExecuteUiFuncAsync(GetHomeSelectedDayState, cancellationToken).ConfigureAwait(false);
                case "get-workspace-status":
                    return await ExecuteUiFuncAsync(GetWorkspaceStatus, cancellationToken).ConfigureAwait(false);
                case "get-localization-state":
                    return await ExecuteUiFuncAsync(GetLocalizationState, cancellationToken).ConfigureAwait(false);
                case "select-home-date":
                    if (string.IsNullOrWhiteSpace(request.Value))
                    {
                        return new UiAutomationBridgeResponse(false, "The select-home-date request was incomplete.");
                    }

                    await ExecuteUiActionAsync(
                        () => SelectHomeDate(request.Value),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "apply-selected-import-changes":
                    return await ApplySelectedImportChangesAsync(cancellationToken).ConfigureAwait(false);
                case "set-selected-import-change-ids":
                    await ExecuteUiActionAsync(
                        () => SetSelectedImportChangeIds(request.Value),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "import-files":
                    if (string.IsNullOrWhiteSpace(request.Value))
                    {
                        return new UiAutomationBridgeResponse(false, "The import-files request was incomplete.");
                    }

                    var filePaths = request.Value
                        .Split(["\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (filePaths.Length == 0)
                    {
                        return new UiAutomationBridgeResponse(false, "The import-files request did not include any file paths.");
                    }

                    await ImportFilesAsync(filePaths, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return new UiAutomationBridgeResponse(false, $"Unsupported automation action '{request.Action}'.");
            }

            return new UiAutomationBridgeResponse(true, null);
        }
        catch (Exception exception)
        {
            return new UiAutomationBridgeResponse(false, exception.Message);
        }
    }

    private static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var payload = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(payload)
            ? default
            : JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static async Task WriteMessageAsync<T>(Stream stream, T payload, CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions).AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private void OpenAboutOverlay()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        shellViewModel.Settings.About.OpenCommand.Execute(null);
    }

    private void CloseAboutOverlay()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        shellViewModel.Settings.About.CloseCommand.Execute(null);
    }

    private void OpenFirstHomeCourseEditor()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var occurrence = shellViewModel.Home.SelectedDayOccurrences.FirstOrDefault();
        if (occurrence is null)
        {
            throw new InvalidOperationException("The selected day does not expose any course occurrences.");
        }

        occurrence.OpenEditorCommand.Execute(null);
    }

    private void OpenDatePickerDropdown(string automationId)
    {
        if (FindElementByAutomationId(window, automationId) is not DatePicker datePicker)
        {
            throw new InvalidOperationException($"The automation bridge could not find date-picker '{automationId}'.");
        }

        datePicker.IsDropDownOpen = true;
        datePicker.UpdateLayout();
    }

    private void SelectComboBoxItemByIndex(string automationId, int index)
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var workspace = shellViewModel.Settings.Workspace;
        switch (automationId)
        {
            case "Settings.TimeProfileModeCombo":
                if (index < 0 || index >= workspace.TimeProfileDefaultModes.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Combo-box '{automationId}' does not contain item index {index}.");
                }

                workspace.SelectedTimeProfileDefaultModeOption = workspace.TimeProfileDefaultModes[index];
                return;
            case "Settings.ExplicitTimeProfileCombo":
                if (index < 0 || index >= workspace.TimeProfiles.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Combo-box '{automationId}' does not contain item index {index}.");
                }

                workspace.SelectedExplicitTimeProfileOption = workspace.TimeProfiles[index];
                return;
            case "Settings.ParsedClassCombo":
                if (index < 0 || index >= workspace.AvailableClasses.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Combo-box '{automationId}' does not contain item index {index}.");
                }

                workspace.SelectedParsedClassName = workspace.AvailableClasses[index];
                return;
            case "ProgramSettings.LocalizationCombo":
                if (index < 0 || index >= workspace.LocalizationOptions.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Combo-box '{automationId}' does not contain item index {index}.");
                }

                workspace.SelectedLocalizationOption = workspace.LocalizationOptions[index];
                return;
            case "Settings.DefaultCalendarColorCombo":
                if (index < 0 || index >= workspace.GoogleCalendarColorOptions.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), index, $"Combo-box '{automationId}' does not contain item index {index}.");
                }

                workspace.SelectedDefaultCalendarColorId = workspace.GoogleCalendarColorOptions[index].ColorId;
                return;
        }

        if (FindElementByAutomationId(window, automationId) is not ComboBox comboBox)
        {
            throw new InvalidOperationException($"The automation bridge could not find combo-box '{automationId}'.");
        }

        if (index < 0 || index >= comboBox.Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Combo-box '{automationId}' does not contain item index {index}.");
        }

        comboBox.SelectedIndex = index;
        comboBox.IsDropDownOpen = false;
        comboBox.UpdateLayout();
    }

    private void SetFirstWeekStartOverride(string value)
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        if (!DateTime.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"Could not parse '{value}' as a first-week-start date.");
        }

        shellViewModel.Settings.Workspace.FirstWeekStartOverrideDate = parsed;
    }

    private UiAutomationBridgeResponse GetFirstWeekStartOverrideState()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var workspace = shellViewModel.Settings.Workspace;
        var date = workspace.FirstWeekStartOverrideDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new UiAutomationBridgeResponse(true, null, date);
    }

    private UiAutomationBridgeResponse GetSelectedClassState()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var workspace = shellViewModel.Settings.Workspace;
        var payload = JsonSerializer.Serialize(
            new
            {
                workspace.SelectedParsedClassName,
                workspace.EffectiveSelectedClassName,
            },
            JsonOptions);
        return new UiAutomationBridgeResponse(true, null, payload);
    }

    private UiAutomationBridgeResponse GetLocalizationState()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var workspace = shellViewModel.Settings.Workspace;
        var payload = JsonSerializer.Serialize(
            new
            {
                workspace.SelectedPreferredCultureName,
                SelectedLocalizationOptionKey = workspace.SelectedLocalizationOption?.SelectionKey,
                ProgramSettingsTitle = System.Windows.Application.Current.TryFindResource("SettingsProgramSettingsTitle")?.ToString(),
                CloseButton = System.Windows.Application.Current.TryFindResource("AboutCloseButton")?.ToString(),
            },
            JsonOptions);
        return new UiAutomationBridgeResponse(true, null, payload);
    }

    private UiAutomationBridgeResponse GetPlannedChangeState()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var preview = shellViewModel.Settings.Workspace.CurrentPreviewResult;
        var payload = JsonSerializer.Serialize(
            new
            {
                PlannedChanges = preview?.SyncPlan?.PlannedChanges.Select(
                    static change => new
                    {
                        change.LocalStableId,
                        ChangeKind = change.ChangeKind.ToString(),
                        ChangeSource = change.ChangeSource.ToString(),
                        TargetKind = change.TargetKind.ToString(),
                        BeforeLocation = change.Before?.Metadata.Location,
                        AfterLocation = change.After?.Metadata.Location,
                        RemoteLocation = change.RemoteEvent?.Location,
                    }),
            },
            JsonOptions);
        return new UiAutomationBridgeResponse(true, null, payload);
    }

    private UiAutomationBridgeResponse GetPreviewOccurrenceState()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var workspace = shellViewModel.Settings.Workspace;
        var preview = workspace.CurrentPreviewResult;
        var payload = JsonSerializer.Serialize(
            new
            {
                workspace.SelectedParsedClassName,
                workspace.EffectiveSelectedClassName,
                DeletionWindowStart = preview?.SyncPlan?.DeletionWindow?.Start,
                DeletionWindowEnd = preview?.SyncPlan?.DeletionWindow?.End,
                Occurrences = preview?.SyncPlan?.Occurrences.Select(
                    static occurrence => new
                    {
                        LocalStableId = SyncIdentity.CreateOccurrenceId(occurrence),
                        occurrence.ClassName,
                        occurrence.SchoolWeekNumber,
                        OccurrenceDate = occurrence.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Start = occurrence.Start,
                        End = occurrence.End,
                        occurrence.TimeProfileId,
                        Weekday = occurrence.Weekday.ToString(),
                        TargetKind = occurrence.TargetKind.ToString(),
                        occurrence.CourseType,
                        CourseTitle = occurrence.Metadata.CourseTitle,
                        occurrence.Metadata.Notes,
                        occurrence.Metadata.Campus,
                        occurrence.Metadata.Location,
                        occurrence.Metadata.Teacher,
                        occurrence.Metadata.TeachingClassComposition,
                        WeekExpressionRaw = occurrence.Metadata.WeekExpression.RawText,
                        PeriodStart = occurrence.Metadata.PeriodRange.StartPeriod,
                        PeriodEnd = occurrence.Metadata.PeriodRange.EndPeriod,
                        SourceKind = occurrence.SourceFingerprint.SourceKind,
                        SourceHash = occurrence.SourceFingerprint.Hash,
                    }),
                UnresolvedItems = workspace.CurrentUnresolvedItems.Select(
                    static item => new
                    {
                        item.ClassName,
                        item.Code,
                        item.Summary,
                        item.Reason,
                        SourceKind = item.SourceFingerprint.SourceKind,
                        SourceHash = item.SourceFingerprint.Hash,
                    }),
            },
            JsonOptions);
        return new UiAutomationBridgeResponse(true, null, payload);
    }

    private UiAutomationBridgeResponse GetHomeSelectedDayState()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                shellViewModel.Home.SelectedDayTitle,
                shellViewModel.Home.SelectedDaySummary,
                Occurrences = shellViewModel.Home.SelectedDayOccurrences.Select(
                    static occurrence => new
                    {
                        occurrence.Title,
                        occurrence.TimeRange,
                        Status = occurrence.Status.ToString(),
                        Source = occurrence.Source.ToString(),
                        Origin = occurrence.Origin.ToString(),
                        occurrence.ColorDotHex,
                        occurrence.BorderBrushHex,
                        occurrence.Location,
                    }),
            },
            JsonOptions);
        return new UiAutomationBridgeResponse(true, null, payload);
    }

    private void SelectHomeDate(string value)
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidOperationException($"The select-home-date request used an invalid date '{value}'.");
        }

        var targetDay = shellViewModel.Home.CalendarDays.FirstOrDefault(day => day.Date == date);
        if (targetDay is null)
        {
            throw new InvalidOperationException($"The Home calendar did not contain date '{value}'.");
        }

        shellViewModel.Home.SelectDayCommand.Execute(targetDay);
    }

    private UiAutomationBridgeResponse GetWorkspaceStatus()
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        return new UiAutomationBridgeResponse(true, null, shellViewModel.Settings.Workspace.WorkspaceStatus);
    }

    private async Task<UiAutomationBridgeResponse> ApplySelectedImportChangesAsync(CancellationToken cancellationToken)
    {
        var status = await window.Dispatcher.InvokeAsync(
            async () =>
            {
                if (window.DataContext is not ShellViewModel shellViewModel)
                {
                    throw new InvalidOperationException("The automation bridge could not access the shell view model.");
                }

                await shellViewModel.Settings.Workspace.ApplySelectedImportChangesAsync();
                window.UpdateLayout();
                return shellViewModel.Settings.Workspace.WorkspaceStatus;
            },
            DispatcherPriority.Send,
            cancellationToken).Task.Unwrap().ConfigureAwait(false);

        await window.Dispatcher.InvokeAsync(
            () => window.UpdateLayout(),
            DispatcherPriority.ApplicationIdle,
            cancellationToken).Task.ConfigureAwait(false);

        return new UiAutomationBridgeResponse(true, null, status);
    }

    private void SetSelectedImportChangeIds(string? value)
    {
        if (window.DataContext is not ShellViewModel shellViewModel)
        {
            throw new InvalidOperationException("The automation bridge could not access the shell view model.");
        }

        var selectedIds = string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(["\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        shellViewModel.Settings.Workspace.UpdateImportSelection(selectedIds);
    }

    private async Task ImportFilesAsync(string[] filePaths, CancellationToken cancellationToken)
    {
        await window.Dispatcher.InvokeAsync(
            async () =>
            {
                if (window.DataContext is not ShellViewModel shellViewModel)
                {
                    throw new InvalidOperationException("The automation bridge could not access the shell view model.");
                }

                await shellViewModel.Settings.Workspace.HandleDroppedFilesAsync(filePaths);
                window.UpdateLayout();
            },
            DispatcherPriority.Send,
            cancellationToken).Task.Unwrap().ConfigureAwait(false);

        await window.Dispatcher.InvokeAsync(
            () => window.UpdateLayout(),
            DispatcherPriority.ApplicationIdle,
            cancellationToken).Task.ConfigureAwait(false);
    }

    private async Task ExecuteUiActionAsync(Action action, CancellationToken cancellationToken)
    {
        await window.Dispatcher.InvokeAsync(
            () =>
            {
                action();
                window.UpdateLayout();
            },
            DispatcherPriority.Send,
            cancellationToken).Task.ConfigureAwait(false);

        await window.Dispatcher.InvokeAsync(
            () => window.UpdateLayout(),
            DispatcherPriority.ApplicationIdle,
            cancellationToken).Task.ConfigureAwait(false);
    }

    private async Task<UiAutomationBridgeResponse> ExecuteUiFuncAsync(
        Func<UiAutomationBridgeResponse> action,
        CancellationToken cancellationToken)
    {
        var response = await window.Dispatcher.InvokeAsync(
            action,
            DispatcherPriority.Send,
            cancellationToken).Task.ConfigureAwait(false);

        await window.Dispatcher.InvokeAsync(
            () => window.UpdateLayout(),
            DispatcherPriority.ApplicationIdle,
            cancellationToken).Task.ConfigureAwait(false);

        return response;
    }

    private static FrameworkElement? FindElementByAutomationId(DependencyObject root, string automationId)
    {
        if (root is FrameworkElement element
            && string.Equals(AutomationProperties.GetAutomationId(element), automationId, StringComparison.Ordinal))
        {
            return element;
        }

        var childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (FindElementByAutomationId(child, automationId) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private sealed record UiAutomationBridgeRequest(string Action, string? AutomationId, string? OutputPath, int? Index = null, string? Value = null);

    private sealed record UiAutomationBridgeResponse(bool Success, string? Error, string? Value = null);
}
