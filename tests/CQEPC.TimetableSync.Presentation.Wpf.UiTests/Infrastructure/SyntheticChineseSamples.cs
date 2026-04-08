namespace CQEPC.TimetableSync.Presentation.Wpf.UiTests.Infrastructure;

internal static class SyntheticChineseSamples
{
    public const string SemesterHeader = "2025-2026\u5b66\u5e74\u7b2c2\u5b66\u671f";
    public const string MajorPowerAutomation = "\u4e13\u4e1a\uff1a\u6f14\u793a\u81ea\u52a8\u5316\u6280\u672f";
    public const string TimeSegmentLabel = "\u65f6\u95f4\u6bb5";
    public const string PeriodLabel = "\u8282\u6b21";
    public const string MondayLabel = "\u661f\u671f\u4e00";
    public const string TuesdayLabel = "\u661f\u671f\u4e8c";
    public const string WednesdayLabel = "\u661f\u671f\u4e09";
    public const string ThursdayLabel = "\u661f\u671f\u56db";
    public const string FridayLabel = "\u661f\u671f\u4e94";
    public const string SaturdayLabel = "\u661f\u671f\u516d";
    public const string SundayLabel = "\u661f\u671f\u65e5";
    public const string PreviousMonthButtonText = "\u4e0a\u4e2a\u6708";

    public const string ElectronicsTitle = "\u7535\u5b50\u6280\u672f\u2605";
    public const string Calculus2Title = "\u9ad8\u7b49\u6570\u5b662\u2605";
    public const string MotorTechnologyTitle = "\u7535\u673a\u6280\u672f\u2605";
    public const string TeacherLiuHuaqiao = "\u6f14\u793a\u6559\u5e08\u7532";
    public const string TeacherYuanTao = "\u6f14\u793a\u6559\u5e08\u4e59";
    public const string TeacherLiJie = "\u6f14\u793a\u6559\u5e08\u4e19";
    public const string CampusTongnan = "\u6f14\u793a\u6821\u533a";
    public const string PowerClass25101 = "\u6f14\u793a\u73edA01";
    public const string PowerClass25102 = "\u6f14\u793a\u73edA02";

    public const string WorkbookHeader = "***\u6f14\u793a\u804c\u4e1a\u5b66\u96622025/2026\u5b66\u5e74\u7b2c2\u5b66\u671f\u6559\u5b66\u8fdb\u7a0b\u8868***";
    public const string MonthLabel = "\u6708";
    public const string DayLabel = "\u65e5";
    public const string WeekLabel = "\u5468";
    public const string ClassLabel = "\u73ed\u7ea7";
    public const string WorkbookFooter = "\u6267\u884c\u65f6\u95f4\uff1a2026\u5e743\u670813\u65e5";

    public const string ClassTimeTitle = "\u6f14\u793a\u804c\u4e1a\u5b66\u96622025-2026\u5b66\u5e74\u7b2c2\u5b66\u671f\u4e0a\u8bfe\u65f6\u95f4\u8868";
    public const string ClassTimeNote = "\u6ce8\uff1a\u7b2c5-6\u8282\u4e3a\u4e2d\u5348\u65f6\u6bb5\uff0c\u539f\u5219\u4e0a\u4e0d\u5b89\u6392\u8bfe\u7a0b\u3002";

    public static string TimetableTitleFor(string className) => $"{className}\u8bfe\u8868";

    public static string Metadata(string periodRange, string weekExpression, string location, string teacher, string className) =>
        $"({periodRange}\u8282){weekExpression}/\u6821\u533a:{CampusTongnan}/\u573a\u5730:{location}/\u6559\u5e08:{teacher}/\u6559\u5b66\u73ed\u7ec4\u6210:{className}/\u6559\u5b66\u73ed\u4eba\u6570:64/\u8003\u6838\u65b9\u5f0f:\u8003\u8bd5/\u8bfe\u7a0b\u5b66\u65f6\u7ec4\u6210:\u7406\u8bba:32/\u5b66\u5206:2.0";

    public static string ElectronicsMetadata =>
        Metadata("1-2", "3-8\u5468", "31203", TeacherLiuHuaqiao, PowerClass25101);

    public static string Calculus2Metadata =>
        Metadata("3-4", "3-8\u5468", "31301", TeacherYuanTao, PowerClass25101);

    public static string MotorTechnologyMetadata =>
        Metadata("1-2", "5-12\u5468", "31308", TeacherLiJie, PowerClass25102);

    public static IReadOnlyList<string> DocxParagraphs =>
    [
        ClassTimeTitle,
        ClassTimeNote,
    ];

    public static IReadOnlyList<IReadOnlyList<string>> DocxTableRows =>
    [
        ["\u6559\u5b66\u5730\u70b9", "\u7b2c1-2\u8282", "\u7b2c3-4\u8282", "\u7b2c5-6\u8282", "\u7b2c7-8\u8282", "\u7b2c9-10\u8282", "\u7b2c11-12\u8282"],
        [$"{CampusTongnan}(\u7406\u8bba\u8bfe)", "8:30-9:50(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "10:20-11:40(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "12:40-14:00(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "14:30-15:50(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "16:20-17:40(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "19:00-20:20(\u8bfe\u95f4\u4e0d\u4f11\u606f)"],
        [$"{CampusTongnan}(\u5b9e\u8bad\u8bfe)", "8:10-9:30(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "10:00-11:20(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "12:40-14:00(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "14:30-15:50(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "16:10-17:30(\u8bfe\u95f4\u4e0d\u4f11\u606f)", "19:00-20:20(\u8bfe\u95f4\u4e0d\u4f11\u606f)"],
    ];
}
