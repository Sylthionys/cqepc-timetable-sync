using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQEPC.TimetableSync.Application.UseCases.Onboarding;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class SourceFileCardViewModel : ObservableObject
{
    private readonly Func<LocalSourceFileKind, Task> browseAsync;
    private readonly Func<LocalSourceFileKind, Task> replaceAsync;
    private readonly Func<LocalSourceFileKind, Task> removeAsync;
    private string selectedFileName = UiText.SourceFileNotSelected;
    private string? fullPath;
    private string importStatusText = UiFormatter.GetImportStatusText(SourceImportStatus.Missing);
    private string importDetail = UiText.SourceFileNotSelectedDetail;
    private string parseStatusText = UiFormatter.GetParseStatusText(SourceParseStatus.WaitingForFile);
    private string parseDetail = UiText.SourceFileEnableParsingLater;
    private string lastSelectedSummary = UiText.SourceFileNotImportedYet;
    private bool hasSelection;

    public SourceFileCardViewModel(
        LocalSourceFileKind kind,
        Func<LocalSourceFileKind, Task> browseAsync,
        Func<LocalSourceFileKind, Task> replaceAsync,
        Func<LocalSourceFileKind, Task> removeAsync)
    {
        Kind = kind;
        this.browseAsync = browseAsync ?? throw new ArgumentNullException(nameof(browseAsync));
        this.replaceAsync = replaceAsync ?? throw new ArgumentNullException(nameof(replaceAsync));
        this.removeAsync = removeAsync ?? throw new ArgumentNullException(nameof(removeAsync));

        BrowseCommand = new AsyncRelayCommand(() => this.browseAsync(Kind));
        ReplaceCommand = new AsyncRelayCommand(() => this.replaceAsync(Kind), () => HasSelection);
        RemoveCommand = new AsyncRelayCommand(() => this.removeAsync(Kind), () => HasSelection);
    }

    public LocalSourceFileKind Kind { get; }

    public string AutomationId =>
        Kind switch
        {
            LocalSourceFileKind.TimetablePdf => "Settings.SourceFileCard.TimetablePdf",
            LocalSourceFileKind.TeachingProgressXls => "Settings.SourceFileCard.TeachingProgressXls",
            LocalSourceFileKind.ClassTimeDocx => "Settings.SourceFileCard.ClassTimeDocx",
            _ => "Settings.SourceFileCard.Unknown",
        };

    public string BrowseAutomationId => $"{AutomationId}.Browse";

    public string ReplaceAutomationId => $"{AutomationId}.Replace";

    public string RemoveAutomationId => $"{AutomationId}.Remove";

    public string Title => UiText.GetSourceFileDisplayName(Kind);

    public string Description => UiText.GetSourceFileShortDescription(Kind);

    public string ExpectedExtension => LocalSourceCatalogMetadata.GetExpectedExtension(Kind);

    public string SelectedFileName
    {
        get => selectedFileName;
        private set => SetProperty(ref selectedFileName, value);
    }

    public string? FullPath
    {
        get => fullPath;
        private set => SetProperty(ref fullPath, value);
    }

    public string ImportStatusText
    {
        get => importStatusText;
        private set => SetProperty(ref importStatusText, value);
    }

    public string ImportDetail
    {
        get => importDetail;
        private set => SetProperty(ref importDetail, value);
    }

    public string ParseStatusText
    {
        get => parseStatusText;
        private set => SetProperty(ref parseStatusText, value);
    }

    public string ParseDetail
    {
        get => parseDetail;
        private set => SetProperty(ref parseDetail, value);
    }

    public string LastSelectedSummary
    {
        get => lastSelectedSummary;
        private set => SetProperty(ref lastSelectedSummary, value);
    }

    public bool HasSelection
    {
        get => hasSelection;
        private set
        {
            if (SetProperty(ref hasSelection, value))
            {
                ReplaceCommand.NotifyCanExecuteChanged();
                RemoveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand BrowseCommand { get; }

    public IAsyncRelayCommand ReplaceCommand { get; }

    public IAsyncRelayCommand RemoveCommand { get; }

    public void Apply(LocalSourceFileState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        SelectedFileName = string.IsNullOrWhiteSpace(state.DisplayName)
            ? UiText.SourceFileNotSelected
            : state.DisplayName;
        FullPath = state.FullPath;
        ImportStatusText = UiFormatter.GetImportStatusText(state.ImportStatus);
        ImportDetail = UiFormatter.FormatSourceImportDetail(state);
        ParseStatusText = UiFormatter.GetParseStatusText(state.ParseStatus);
        ParseDetail = UiFormatter.FormatSourceParseDetail(state);
        LastSelectedSummary = state.LastSelectedUtc.HasValue
            ? UiText.FormatSourceFileLastSelected(state.LastSelectedUtc.Value.LocalDateTime)
            : UiText.SourceFileNotImportedYet;
        HasSelection = state.HasSelection;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ExpectedExtension));
    }
}
