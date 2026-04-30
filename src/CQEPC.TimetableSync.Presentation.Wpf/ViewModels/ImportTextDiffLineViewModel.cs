using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class ImportTextDiffLineViewModel : ObservableObject
{
    private string editableText;
    private bool canInlineEdit;
    private Action<ImportTextDiffLineViewModel>? edited;

    public ImportTextDiffLineViewModel(string? beforeText, string? afterText, bool isBeforeChanged, bool isAfterChanged)
    {
        BeforeText = beforeText ?? string.Empty;
        AfterText = afterText ?? string.Empty;
        IsBeforeChanged = isBeforeChanged;
        IsAfterChanged = isAfterChanged;
        editableText = ResolveEditableText();
    }

    public string BeforeText { get; }

    public string AfterText { get; }

    public bool IsBeforeChanged { get; }

    public bool IsAfterChanged { get; }

    public bool IsDeletedLine => IsBeforeChanged && !IsAfterChanged;

    public bool IsAddedLine => IsAfterChanged && !IsBeforeChanged;

    public bool IsChangedLine => IsBeforeChanged || IsAfterChanged;

    public bool IsManagedMetadataLine =>
        IsManagedMetadataText(BeforeText) || IsManagedMetadataText(AfterText);

    public bool CanInlineEdit
    {
        get => canInlineEdit;
        private set => SetProperty(ref canInlineEdit, value);
    }

    public string EditableText
    {
        get => editableText;
        set
        {
            if (SetProperty(ref editableText, value ?? string.Empty))
            {
                edited?.Invoke(this);
            }
        }
    }

    public string BeforeDisplayText =>
        string.IsNullOrEmpty(BeforeText)
            ? string.Empty
            : IsBeforeChanged ? $"- {BeforeText}" : $"  {BeforeText}";

    public string AfterDisplayText =>
        string.IsNullOrEmpty(AfterText)
            ? string.Empty
            : IsAfterChanged ? $"+ {AfterText}" : $"  {AfterText}";

    public string DisplayText => IsAddedLine
        ? $"+ {AfterText}"
        : IsDeletedLine
            ? $"- {BeforeText}"
            : $"  {(string.IsNullOrEmpty(AfterText) ? BeforeText : AfterText)}";

    public void ConfigureInlineEditing(bool canEdit, Action<ImportTextDiffLineViewModel>? editedCallback)
    {
        edited = null;
        EditableText = ResolveEditableText();
        CanInlineEdit = canEdit;
        edited = canEdit ? editedCallback : null;
    }

    public string ResolveCommittedText() =>
        CanInlineEdit ? EditableText : ResolveEditableText();

    private string ResolveEditableText() =>
        string.IsNullOrEmpty(AfterText) ? BeforeText : AfterText;

    private static bool IsManagedMetadataText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();
        return trimmed.StartsWith("managedBy:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("localSyncId:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("sourceFingerprint:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("sourceKind:", StringComparison.OrdinalIgnoreCase);
    }
}
