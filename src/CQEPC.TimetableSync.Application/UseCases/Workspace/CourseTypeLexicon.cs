namespace CQEPC.TimetableSync.Application.UseCases.Workspace;

public static class CourseTypeLexicon
{
    public const string Theory = "\u7406\u8BBA";
    public const string Lab = "\u5B9E\u9A8C";
    public const string PracticalTraining = "\u5B9E\u8BAD";
    public const string Practice = "\u5B9E\u8DF5";
    public const string Computer = "\u4E0A\u673A";
    public const string Extracurricular = "\u8BFE\u5916";

    public static IReadOnlyList<string> CleanChineseAliases { get; } =
    [
        Theory,
        Lab,
        PracticalTraining,
        Practice,
        Computer,
        Extracurricular,
    ];

    public static IReadOnlyList<string> KnownMojibakeAliases { get; } =
    [
        "\u9583\u70B2\u68DC\u9854?",
        "\u9410\u570D\u5074\u9424\u941B?",
        "\u9410\u570D\u5075\u9854?",
        "\u9410\u570D\u5075\u6769?",
        "\u5A11\u6483\uFE65\u5A67\u20AC",
        "\u9420\u56E8\u5133\u9866?",
    ];
}
