using CQEPC.TimetableSync.Domain.Model;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class UnresolvedItemCardViewModel
{
    public UnresolvedItemCardViewModel(UnresolvedItem item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public UnresolvedItem Item { get; }

    public string Summary => UiFormatter.FormatUnresolvedSummary(Item);

    public string Reason => UiFormatter.FormatUnresolvedReason(Item);

    public string RawSourceText => Item.RawSourceText;

    public string ClassName => string.IsNullOrWhiteSpace(Item.ClassName) ? UiText.SharedUnknownClass : Item.ClassName;
}
