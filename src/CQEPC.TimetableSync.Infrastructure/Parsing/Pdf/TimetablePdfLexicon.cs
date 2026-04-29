namespace CQEPC.TimetableSync.Infrastructure.Parsing.Pdf;

internal static class TimetablePdfLexicon
{
    public const string Monday = "\u661f\u671f\u4e00";
    public const string Tuesday = "\u661f\u671f\u4e8c";
    public const string Wednesday = "\u661f\u671f\u4e09";
    public const string Thursday = "\u661f\u671f\u56db";
    public const string Friday = "\u661f\u671f\u4e94";
    public const string Saturday = "\u661f\u671f\u516d";
    public const string Sunday = "\u661f\u671f\u65e5";

    public const string Campus = "\u6821\u533a";
    public const string Location = "\u573a\u5730";
    public const string Teacher = "\u6559\u5e08";
    public const string TeachingClassComposition = "\u6559\u5b66\u73ed\u7ec4\u6210";
    public const string TeachingClass = "\u6559\u5b66\u73ed";
    public const string TeachingClassSize = "\u6559\u5b66\u73ed\u4eba\u6570";
    public const string AssessmentMode = "\u8003\u6838\u65b9\u5f0f";
    public const string CourseHourComposition = "\u8bfe\u7a0b\u5b66\u65f6\u7ec4\u6210";
    public const string Credits = "\u5b66\u5206";

    public const string TimetableSuffix = "\u8bfe\u8868";
    public const string PracticalSummaryTitle = "\u5b9e\u8df5\u8bfe\u7a0b\u6c47\u603b";
    public const string PracticalSummaryPrefix = "\u5b9e\u8df5\u8bfe\u7a0b";
    public const string PrintTimePrefix = "\u6253\u5370\u65f6\u95f4:";
    public const string MajorPrefixFullWidth = "\u4e13\u4e1a\uff1a";
    public const string MajorPrefixAscii = "\u4e13\u4e1a:";
    public const string TimeSegment = "\u65f6\u95f4\u6bb5";
    public const string PeriodLabel = "\u8282\u6b21";
    public const string Morning = "\u4e0a\u5348";
    public const string Afternoon = "\u4e0b\u5348";
    public const string Evening = "\u665a\u4e0a";
    public const string Noon = "\u4e2d\u5348";
    public const string FullWidthColon = "\uff1a";

    public const string Theory = "\u7406\u8bba";
    public const string Lab = "\u5b9e\u9a8c";
    public const string Practical = "\u5b9e\u8df5";
    public const string Computer = "\u4e0a\u673a";
    public const string Extracurricular = "\u8bfe\u5916";
    public const string TheoryPrefix = "\u7406\u8bba:";
    public const string LabPrefix = "\u5b9e\u9a8c:";
    public const string PracticalPrefix = "\u5b9e\u8df5:";
    public const string ComputerPrefix = "\u4e0a\u673a:";
    public const string CourseHourCompositionPrefix = "\u8bfe\u7a0b\u5b66\u65f6\u7ec4\u6210:";
    public const string MetadataTailPattern = @"^:(\u7406\u8bba|\u5b9e\u9a8c|\u5b9e\u8df5|\u4e0a\u673a):";

    public const string UnresolvedBlockSummaryPrefix = "\u672a\u89e3\u6790\u8bfe\u7a0b\u5757";

    public const char TheoryMarker = '\u2605';
    public const char LabMarker = '\u2606';
    public const char PracticalMarker = '\u25c6';
    public const char ComputerMarker = '\u25a0';
    public const char ExtracurricularMarker = '\u3007';

    public const string SemesterHeaderPattern = @"^\d{4}-\d{4}\u5b66\u5e74\u7b2c\d\u5b66\u671f$";
    public const string PeriodLeadPattern = @"^\((?<start>\d{1,2})-(?<end>\d{1,2})\u8282\)";
    public const string TaggedMetadataPattern =
        @"/(?<label>\u6821\u533a|\u573a\u5730|\u6559\u5e08|\u6559\u5b66\u73ed\u7ec4\u6210|\u6559\u5b66\u73ed\u4eba\u6570|\u6559\u5b66\u73ed|\u8003\u6838\u65b9\u5f0f|\u8bfe\u7a0b\u5b66\u65f6\u7ec4\u6210|\u5b66\u5206):";
}
