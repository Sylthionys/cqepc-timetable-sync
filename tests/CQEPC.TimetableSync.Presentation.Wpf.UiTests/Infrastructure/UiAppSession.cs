using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System.Globalization;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit.Sdk;
using FlaUIApplication = FlaUI.Core.Application;

namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;

internal sealed class UiAppSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly UiTestWorkspace? workspace;

    private UiAppSession(string testName, UiTestWorkspace? workspace, FlaUIApplication application, UIA3Automation automation, Window mainWindow)
    {
        TestName = testName;
        this.workspace = workspace;
        Application = application;
        Automation = automation;
        MainWindow = mainWindow;
    }

    public string TestName { get; }

    public FlaUIApplication Application { get; }

    public UIA3Automation Automation { get; }

    public Window MainWindow { get; private set; }

    public static async Task<UiAppSession> LaunchAsync(string testName, UiFixtureScenario scenario = UiFixtureScenario.Default)
    {
        var workspace = await UiTestWorkspace.CreateAsync(testName, scenario);
        return LaunchCore(testName, workspace.RootDirectory, workspace);
    }

    public static Task<UiAppSession> LaunchAsync(string testName, string storageRoot) =>
        Task.FromResult(LaunchCore(testName, storageRoot, workspace: null));

    public static Task<UiAppSession> LaunchAsync(string testName, string storageRoot, int width, int height) =>
        Task.FromResult(LaunchCore(testName, storageRoot, workspace: null, width, height));

    public static async Task<UiAppSession> LaunchAsync(string testName, int width, int height, UiFixtureScenario scenario = UiFixtureScenario.Default)
    {
        var workspace = await UiTestWorkspace.CreateAsync(testName, scenario);
        return LaunchCore(testName, workspace.RootDirectory, workspace, width, height);
    }

    private static UiAppSession LaunchCore(string testName, string storageRoot, UiTestWorkspace? workspace, int width = 1380, int height = 900)
    {
        var executablePath = UiTestPaths.AppExecutablePath;
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"The WPF executable was not found at '{executablePath}'. Build the app project before running the FlaUI tests.",
                executablePath);
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
            Arguments = $"--ui-automation --window-mode background --width {width} --height {height}",
        };
        startInfo.EnvironmentVariables["CQEPC_TIMETABLESYNC_STORAGE_ROOT"] = storageRoot;

        var application = FlaUIApplication.Launch(startInfo);
        var automation = new UIA3Automation();
        var mainWindow = WaitForMainWindow(application, automation);

        return new UiAppSession(testName, workspace, application, automation, mainWindow);
    }

    public AutomationElement WaitForElement(string automationId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var element = GetActiveMainWindow().FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(200);
        }

        throw new XunitException($"Timed out waiting for automation element '{automationId}'.");
    }

    public Button WaitForButton(string automationId, TimeSpan? timeout = null) =>
        WaitForElement(automationId, timeout).AsButton();

    public void NavigateTo(string navigationButtonId, string expectedPageRootId)
    {
        ClickButton(navigationButtonId);
        _ = WaitForElement(expectedPageRootId);
    }

    public void ClickButton(string automationId, TimeSpan? timeout = null) =>
        ClickButton(automationId, expectedPostActionElementId: null, timeout);

    public void ClickButton(string automationId, string? expectedPostActionElementId, TimeSpan? timeout = null)
    {
        var button = WaitForButton(automationId, timeout);
        InvokeElement(button);

        if (string.IsNullOrWhiteSpace(expectedPostActionElementId))
        {
            return;
        }

        if (TryFindElement(expectedPostActionElementId, TimeSpan.FromSeconds(1)) is not null)
        {
            return;
        }

        // Some WPF command buttons do not react to Invoke even though UIA exposes the pattern.
        button.Click(false);

        if (TryFindElement(expectedPostActionElementId, TimeSpan.FromSeconds(5)) is not null)
        {
            return;
        }

        throw new XunitException(
            $"Button '{automationId}' did not produce automation element '{expectedPostActionElementId}' after semantic invoke and WPF click fallback.");
    }

    public void InvokeElement(string automationId, TimeSpan? timeout = null) =>
        InvokeElement(WaitForElement(automationId, timeout));

    public void InvokeFirstElementByAutomationIdPrefix(string automationIdPrefix, TimeSpan? timeout = null)
    {
        var element = WaitForElementByAutomationIdPrefix(automationIdPrefix, timeout);
        InvokeElement(element);
    }

    public void ClickElement(string automationId, TimeSpan? timeout = null)
    {
        var element = WaitForElement(automationId, timeout);
        element.Focus();
        element.Click(false);
    }

    private AutomationElement WaitForElementByAutomationIdPrefix(string automationIdPrefix, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var element = GetActiveMainWindow()
                .FindAllDescendants()
                .FirstOrDefault(item => item.AutomationId.StartsWith(automationIdPrefix, StringComparison.Ordinal));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(200);
        }

        throw new XunitException($"Timed out waiting for automation element prefix '{automationIdPrefix}'.");
    }

    public void ToggleElement(string automationId, ToggleState? desiredState = null, TimeSpan? timeout = null)
    {
        var element = WaitForElement(automationId, timeout);
        if (element.Patterns.Toggle.IsSupported)
        {
            var togglePattern = element.Patterns.Toggle.Pattern;
            if (desiredState is null)
            {
                try
                {
                    togglePattern.Toggle();
                }
                catch
                {
                    element.Focus();
                    element.Click(false);
                }

                return;
            }

            var attemptsRemaining = 3;
            while (togglePattern.ToggleState != desiredState && attemptsRemaining-- > 0)
            {
                try
                {
                    togglePattern.Toggle();
                }
                catch
                {
                    element.Focus();
                    element.Click(false);
                }
            }

            if (togglePattern.ToggleState != desiredState)
            {
                throw new XunitException($"Failed to set toggle '{automationId}' to '{desiredState}'.");
            }

            return;
        }

        InvokeElement(element);
    }

    public void SelectComboBoxItem(string automationId, string itemText, TimeSpan? timeout = null)
    {
        var comboBox = WaitForElement(automationId, timeout).AsComboBox();
        var items = comboBox.Items;
        var index = Array.FindIndex(items, candidate => string.Equals(candidate.Text, itemText, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new XunitException($"Could not find combo-box item '{itemText}' in '{automationId}'.");
        }

        SelectComboBoxItemByIndex(automationId, index, timeout);
    }

    public void SelectComboBoxItemByIndex(string automationId, int index, TimeSpan? timeout = null)
    {
        var comboBox = WaitForElement(automationId, timeout).AsComboBox();
        var items = comboBox.Items;
        if (index < 0 || index >= items.Length)
        {
            throw new XunitException($"Combo-box '{automationId}' does not contain item index {index}.");
        }

        var item = items[index];
        try
        {
            comboBox.Select(index);
        }
        catch
        {
            SelectComboBoxItemByIndexViaBridge(automationId, index).GetAwaiter().GetResult();
        }

        if (!WaitForComboBoxSelection(automationId, item.Text, TimeSpan.FromSeconds(6)))
        {
            try
            {
                comboBox.Select(index);
            }
            catch
            {
                SelectComboBoxItemByIndexViaBridge(automationId, index).GetAwaiter().GetResult();
            }

            if (!WaitForComboBoxSelection(automationId, item.Text, TimeSpan.FromSeconds(6)))
            {
                throw new XunitException($"Combo-box '{automationId}' did not commit selection for item index {index}.");
            }
        }
    }

    public string? GetComboBoxSelectionText(string automationId, TimeSpan? timeout = null)
    {
        var comboBox = WaitForElement(automationId, timeout).AsComboBox();
        return comboBox.SelectedItem?.Text;
    }

    public int GetComboBoxItemCount(string automationId, TimeSpan? timeout = null)
    {
        var comboBox = WaitForElement(automationId, timeout).AsComboBox();
        return comboBox.Items.Length;
    }

    public IReadOnlyList<string> GetComboBoxItemTexts(string automationId, TimeSpan? timeout = null)
    {
        var comboBox = WaitForElement(automationId, timeout).AsComboBox();
        return comboBox.Items.Select(static item => item.Text).ToArray();
    }

    public string? GetElementName(string automationId, TimeSpan? timeout = null) =>
        WaitForElement(automationId, timeout).Name;

    public AutomationElement WaitForText(string text, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var element = GetActiveMainWindow().FindFirstDescendant(cf => cf.ByText(text));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(200);
        }

        throw new XunitException($"Timed out waiting for visible text '{text}'.");
    }

    public ToggleState? GetToggleState(string automationId, TimeSpan? timeout = null)
    {
        var element = WaitForElement(automationId, timeout);
        return element.Patterns.Toggle.IsSupported ? element.Patterns.Toggle.Pattern.ToggleState : null;
    }

    public void SetText(string automationId, string value, TimeSpan? timeout = null)
    {
        var element = WaitForElement(automationId, timeout);
        if (element.Patterns.Value.IsSupported)
        {
            element.Patterns.Value.Pattern.SetValue(value);
            return;
        }

        var textBox = element.AsTextBox();
        textBox.Text = value;
    }

    public void ScrollToVerticalPercent(string automationId, double verticalPercent, TimeSpan? timeout = null)
    {
        var element = WaitForElement(automationId, timeout);
        if (!element.Patterns.Scroll.IsSupported)
        {
            throw new XunitException($"Automation element '{automationId}' does not support ScrollPattern.");
        }

        element.Patterns.Scroll.Pattern.SetScrollPercent(-1, verticalPercent);
    }

    public string? FindVisiblePageRootId()
    {
        var window = GetActiveMainWindow();
        foreach (var automationId in new[] { "AboutOverlay.Root", "ProgramSettingsOverlay.Root", "CourseEditorOverlay.Root", "Home.PageRoot", "Import.PageRoot", "Settings.PageRoot" })
        {
            if (window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null)
            {
                return automationId;
            }
        }

        return null;
    }

    public void WaitForElementToDisappear(string automationId, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (GetActiveMainWindow().FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is null)
            {
                return;
            }

            Thread.Sleep(200);
        }

        throw new XunitException($"Timed out waiting for automation element '{automationId}' to disappear.");
    }

    public async Task RunAsync(Func<UiAppSession, Task> body)
    {
        try
        {
            await body(this);
        }
        catch (Exception exception)
        {
            try
            {
                var screenshotPath = await TryCaptureScreenshotAsync();
                throw new XunitException(
                    $"{exception}{Environment.NewLine}Screenshot: {screenshotPath}");
            }
            catch (Exception screenshotException)
            {
                throw new XunitException(
                    $"{exception}{Environment.NewLine}Screenshot capture failed: {screenshotException.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Application.HasExited)
            {
                try
                {
                    MainWindow.Close();
                }
                catch (FlaUI.Core.Exceptions.MethodNotSupportedException)
                {
                    Application.Kill();
                }

                await Task.Delay(1500);
            }

            if (!Application.HasExited)
            {
                Application.Kill();
            }
        }
        finally
        {
            Automation.Dispose();
            Application.Dispose();
            workspace?.Dispose();
        }
    }

    public async Task<string> CaptureCurrentPageScreenshotAsync()
    {
        var screenshotDirectory = Path.Combine(UiTestPaths.SolutionRoot, "tmp", "ui-test-screenshots");
        Directory.CreateDirectory(screenshotDirectory);
        var pageRootAutomationId = FindVisiblePageRootId();
        if (string.IsNullOrWhiteSpace(pageRootAutomationId))
        {
            throw new XunitException("Could not determine the visible automation root for page-render screenshot capture.");
        }

        var filePath = Path.Combine(
            screenshotDirectory,
            $"{SanitizeFileName(TestName)}-page-render-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.png");
        await RequestAppRenderedScreenshotAsync(pageRootAutomationId, filePath);
        return filePath;
    }

    public string CaptureWindowScreenshotAsyncCompatible() => CaptureWindowScreenshot();

    public Task OpenAboutOverlayAsync() => SendAutomationCommandAsync("open-about");

    public Task CloseAboutOverlayAsync() => SendAutomationCommandAsync("close-about");

    public Task OpenFirstHomeCourseEditorAsync() => SendAutomationCommandAsync("open-first-home-course-editor");

    public Task OpenDatePickerDropdownAsync(string automationId) => SendAutomationCommandAsync("open-date-picker-dropdown", automationId);

    public async Task<string> GetDatePickerCalendarThemeStateAsync(string automationId)
    {
        var response = await SendAutomationRequestAsync("get-date-picker-calendar-theme-state", automationId);
        if (response is null)
        {
            throw new XunitException($"The automation bridge returned no response for date-picker '{automationId}'.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to inspect date-picker '{automationId}': {response.Error}");
        }

        return response.Value ?? "{}";
    }

    public async Task SelectComboBoxItemByIndexViaBridge(string automationId, int index)
    {
        var response = await SendAutomationRequestAsync("select-combo-index", automationId, index: index);
        if (response is null)
        {
            throw new XunitException($"The automation bridge returned no response for combo-box '{automationId}'.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to select combo-box '{automationId}' index {index}: {response.Error}");
        }
    }

    public async Task SetFirstWeekStartOverrideAsync(DateOnly date)
    {
        var response = await SendAutomationRequestAsync(
            "set-first-week-start",
            value: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the first-week-start request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to set the first-week-start date: {response.Error}");
        }
    }

    public async Task<string?> GetFirstWeekStartOverrideAsync()
    {
        var response = await SendAutomationRequestAsync("get-first-week-start");
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the first-week-start read request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to read the first-week-start date: {response.Error}");
        }

        return response.Value;
    }

    public async Task<string?> GetSelectedClassStateAsync()
    {
        var response = await SendAutomationRequestAsync("get-selected-class-state");
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the selected-class read request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to read the selected-class state: {response.Error}");
        }

        return response.Value;
    }

    public async Task<string?> GetPlannedChangeStateAsync()
    {
        var response = await SendAutomationRequestAsync("get-planned-change-state");
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the planned-change read request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to read the planned-change state: {response.Error}");
        }

        return response.Value;
    }

    public async Task<string?> GetPreviewOccurrenceStateAsync()
    {
        var response = await SendAutomationRequestAsync("get-preview-occurrence-state");
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the preview-occurrence read request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to read the preview-occurrence state: {response.Error}");
        }

        return response.Value;
    }

    public async Task<string?> GetHomeSelectedDayStateAsync()
    {
        var response = await SendAutomationRequestAsync("get-home-selected-day-state");
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the home-selected-day read request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to read the home-selected-day state: {response.Error}");
        }

        return response.Value;
    }

    public async Task SelectHomeDateAsync(DateOnly date)
    {
        var response = await SendAutomationRequestAsync("select-home-date", value: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the select-home-date request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to select the Home date: {response.Error}");
        }
    }

    public async Task<string?> GetWorkspaceStatusAsync()
    {
        var response = await SendAutomationRequestAsync("get-workspace-status");
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the workspace-status read request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to read the workspace status: {response.Error}");
        }

        return response.Value;
    }

    public async Task<string?> GetLocalizationStateAsync()
    {
        var response = await SendAutomationRequestAsync("get-localization-state");
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the localization-state read request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to read localization state: {response.Error}");
        }

        return response.Value;
    }

    public async Task<string?> GetTitleBarThemeStateAsync()
    {
        var response = await SendAutomationRequestAsync("get-title-bar-theme-state");
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the title-bar-theme-state read request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to read title-bar-theme state: {response.Error}");
        }

        return response.Value;
    }

    public async Task<string?> ApplySelectedImportChangesViaBridgeAsync(TimeSpan? timeout = null)
    {
        var response = await SendAutomationRequestAsync(
            "apply-selected-import-changes",
            timeout: timeout ?? TimeSpan.FromMinutes(3));
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the apply-selected-import-changes request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to apply selected import changes: {response.Error}");
        }

        return response.Value;
    }

    public async Task SetSelectedImportChangeIdsAsync(params string[] changeIds)
    {
        var response = await SendAutomationRequestAsync(
            "set-selected-import-change-ids",
            value: string.Join('\n', changeIds));
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the set-selected-import-change-ids request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to set selected import change ids: {response.Error}");
        }
    }

    public async Task ImportFilesAsync(params string[] filePaths)
    {
        if (filePaths.Length == 0)
        {
            throw new XunitException("The import-files request requires at least one file path.");
        }

        var response = await SendAutomationRequestAsync("import-files", value: string.Join('\n', filePaths));
        if (response is null)
        {
            throw new XunitException("The automation bridge returned no response for the import-files request.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed to import files: {response.Error}");
        }
    }

    private static Window WaitForMainWindow(FlaUIApplication application, UIA3Automation automation)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (application.HasExited)
            {
                throw new XunitException("The WPF application exited before the main window was available.");
            }

            try
            {
                var process = System.Diagnostics.Process.GetProcessById(application.ProcessId);
                var mainWindowHandle = process.MainWindowHandle;
                if (mainWindowHandle != IntPtr.Zero)
                {
                    var mainWindowByHandle = automation.FromHandle(mainWindowHandle)?.AsWindow();
                    if (mainWindowByHandle is not null)
                    {
                        return mainWindowByHandle;
                    }
                }
            }
            catch
            {
                // Fall back to top-level window enumeration below.
            }

            var mainWindow = application.GetAllTopLevelWindows(automation)
                .FirstOrDefault(
                    window =>
                    {
                        try
                        {
                            return string.Equals(window.AutomationId, "Shell.MainWindow", StringComparison.Ordinal);
                        }
                        catch
                        {
                            return false;
                        }
                    });
            if (mainWindow is not null)
            {
                return mainWindow;
            }

            Thread.Sleep(200);
        }

        throw new XunitException("Timed out waiting for the main shell window.");
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }

    private static void InvokeElement(AutomationElement element)
    {
        if (element.Patterns.ScrollItem.IsSupported)
        {
            element.Patterns.ScrollItem.Pattern.ScrollIntoView();
        }

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        if (element.Patterns.ExpandCollapse.IsSupported)
        {
            element.Patterns.ExpandCollapse.Pattern.Expand();
            return;
        }

        if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        element.Focus();
        element.Click(false);
    }

    private AutomationElement? TryFindElement(string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var element = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element is not null)
            {
                return element;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    private async Task<string> TryCaptureScreenshotAsync()
    {
        var appRenderedScreenshot = await TryCaptureAppRenderedScreenshotAsync();
        if (!string.IsNullOrWhiteSpace(appRenderedScreenshot))
        {
            return appRenderedScreenshot;
        }

        return CaptureWindowScreenshot();
    }

    private async Task<string?> TryCaptureAppRenderedScreenshotAsync()
    {
        var pageRootAutomationId = FindVisiblePageRootId();
        if (string.IsNullOrWhiteSpace(pageRootAutomationId))
        {
            return null;
        }

        try
        {
            return await CaptureCurrentPageScreenshotAsync();
        }
        catch
        {
            return null;
        }
    }

    private string CaptureWindowScreenshot()
    {
        var screenshotDirectory = Path.Combine(UiTestPaths.SolutionRoot, "tmp", "ui-test-screenshots");
        Directory.CreateDirectory(screenshotDirectory);
        var filePath = Path.Combine(
            screenshotDirectory,
            $"{SanitizeFileName(TestName)}-window-capture-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.png");
        using var image = Capture.Element(GetActiveMainWindow());
        image.ToFile(filePath);
        return filePath;
    }

    private Window GetActiveMainWindow()
    {
        if (Application.HasExited)
        {
            throw new XunitException("The WPF application exited before the main window could be queried.");
        }

        return MainWindow;
    }

    private async Task RequestAppRenderedScreenshotAsync(string automationId, string outputPath)
    {
        var pipeName = BuildPipeName(Application.ProcessId);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(connectTimeout.Token);

        var request = new UiAutomationBridgeRequest("capture", automationId, outputPath);
        await WriteMessageAsync(client, request, connectTimeout.Token);
        var response = await ReadMessageAsync<UiAutomationBridgeResponse>(client, connectTimeout.Token);
        if (response is null)
        {
            throw new XunitException("The app-side automation screenshot bridge returned no response.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The app-side automation screenshot bridge failed: {response.Error}");
        }

        if (!File.Exists(outputPath))
        {
            throw new XunitException($"The app-side automation screenshot bridge reported success, but '{outputPath}' was not created.");
        }
    }

    private async Task SendAutomationCommandAsync(string action, string? automationId = null)
    {
        var response = await SendAutomationRequestAsync(action, automationId);
        if (response is null)
        {
            throw new XunitException($"The automation bridge returned no response for action '{action}'.");
        }

        if (!response.Success)
        {
            throw new XunitException($"The automation bridge failed for action '{action}': {response.Error}");
        }
    }

    private async Task<UiAutomationBridgeResponse?> SendAutomationRequestAsync(
        string action,
        string? automationId = null,
        int? index = null,
        string? value = null,
        TimeSpan? timeout = null)
    {
        var pipeName = BuildPipeName(Application.ProcessId);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeoutCts.Token);
        await WriteMessageAsync(client, new UiAutomationBridgeRequest(action, automationId, null, index, value), timeoutCts.Token);

        return await ReadMessageAsync<UiAutomationBridgeResponse>(client, timeoutCts.Token);
    }

    private static string BuildPipeName(int processId) =>
        $"CQEPC.TimetableSync.UiAutomation.{processId}";

    private static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var payload = await reader.ReadLineAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(payload)
            ? default
            : JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static async Task WriteMessageAsync<T>(Stream stream, T payload, CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions).AsMemory(), cancellationToken);
    }

    private bool WaitForComboBoxSelection(string automationId, string itemText, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (string.Equals(GetComboBoxSelectionText(automationId, TimeSpan.FromSeconds(1)), itemText, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return string.Equals(GetComboBoxSelectionText(automationId, TimeSpan.FromSeconds(1)), itemText, StringComparison.Ordinal);
    }

    private sealed record UiAutomationBridgeRequest(string Action, string? AutomationId, string? OutputPath, int? Index = null, string? Value = null);

    private sealed record UiAutomationBridgeResponse(bool Success, string? Error, string? Value = null);
}
