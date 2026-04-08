using CQEPC.TimetableSync.Application.Abstractions.Normalization;
using CQEPC.TimetableSync.Presentation.Wpf.Resources;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class TimeProfileFallbackConfirmationCardViewModel
{
    public TimeProfileFallbackConfirmationCardViewModel(TimeProfileFallbackConfirmation confirmation)
    {
        Confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
    }

    public TimeProfileFallbackConfirmation Confirmation { get; }

    public string Title => Confirmation.Metadata.CourseTitle;

    public string ClassName => string.IsNullOrWhiteSpace(Confirmation.ClassName) ? UiText.SharedUnknownClass : Confirmation.ClassName;

    public string Summary => UiFormatter.FormatTimeProfileFallbackSummary(Confirmation);

    public string Reason => UiFormatter.FormatTimeProfileFallbackReason(Confirmation);

    public string AppliedProfile => UiFormatter.FormatTimeProfileFallbackAppliedProfile(Confirmation);

    public string? PreferredProfile => UiFormatter.FormatTimeProfileFallbackPreferredProfile(Confirmation);

    public bool HasPreferredProfile => !string.IsNullOrWhiteSpace(PreferredProfile);

    public string RawSourceText => Confirmation.RawSourceText;
}
