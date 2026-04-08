namespace CQEPC.TimetableSync.Infrastructure.Parsing.Spreadsheet;

internal static class TeachingProgressXlsLexicon
{
    public const string Month = "\u6708";
    public const string Day = "\u65E5";
    public const string Week = "\u5468";
    public const string Class = "\u73ED\u7EA7";

    public const string One = "\u4E00";
    public const string Two = "\u4E8C";
    public const string Three = "\u4E09";
    public const string Four = "\u56DB";

    public const string AcademicYearPattern =
        @"(?<startYear>\d{4})\s*/\s*(?<endYear>\d{4})\s*\u5B66\u5E74\s*\u7B2C(?<semester>[\u4E00\u4E8C\u4E09\u56DB1-4])\u5B66\u671F";

    public const string ExecutionDatePattern =
        @"\u6267\u884C\u65F6\u95F4[:\uFF1A]\s*(?<year>\d{4})\u5E74(?<month>\d{1,2})\u6708(?<day>\d{1,2})\u65E5";
}
