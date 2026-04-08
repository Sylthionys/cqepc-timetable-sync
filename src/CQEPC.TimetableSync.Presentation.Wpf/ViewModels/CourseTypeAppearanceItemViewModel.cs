using CommunityToolkit.Mvvm.ComponentModel;

namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

public sealed class CourseTypeAppearanceItemViewModel : ObservableObject
{
    private readonly Action<string, string, string> onChanged;
    private string categoryName;
    private string colorHex;

    public CourseTypeAppearanceItemViewModel(
        string courseTypeKey,
        string displayName,
        string categoryName,
        string colorHex,
        Action<string, string, string> onChanged)
    {
        CourseTypeKey = courseTypeKey ?? throw new ArgumentNullException(nameof(courseTypeKey));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        this.categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        this.colorHex = colorHex ?? throw new ArgumentNullException(nameof(colorHex));
        this.onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    }

    public string CourseTypeKey { get; }

    public string DisplayName { get; }

    public string CategoryName
    {
        get => categoryName;
        set
        {
            if (SetProperty(ref categoryName, value))
            {
                onChanged(CourseTypeKey, CategoryName, ColorHex);
            }
        }
    }

    public string ColorHex
    {
        get => colorHex;
        set
        {
            if (SetProperty(ref colorHex, NormalizeColor(value)))
            {
                onChanged(CourseTypeKey, CategoryName, ColorHex);
            }
        }
    }

    private static string NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#5A6472";
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.StartsWith('#') ? normalized : $"#{normalized}";
    }
}
