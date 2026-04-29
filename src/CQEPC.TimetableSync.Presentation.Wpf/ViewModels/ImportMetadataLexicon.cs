namespace CQEPC.TimetableSync.Presentation.Wpf.ViewModels;

internal static class ImportMetadataLexicon
{
    public const string Course = "Course";
    public const string Class = "Class";
    public const string Campus = "Campus";
    public const string Location = "Location";
    public const string Teacher = "Teacher";
    public const string TeachingClass = "Teaching Class";
    public const string CourseType = "Course Type";
    public const string Notes = "Notes";
    public const string Time = "Time";
    public const string Date = "Date";
    public const string Week = "Week";
    public const string ManagedBy = "managedBy";
    public const string LocalSyncId = "localSyncId";
    public const string SourceFingerprint = "sourceFingerprint";
    public const string SourceKind = "sourceKind";

    public const string CourseZh = "\u8bfe\u7a0b";
    public const string ClassZh = "\u73ed\u7ea7";
    public const string CampusZh = "\u6821\u533a";
    public const string LocationZh = "\u5730\u70b9";
    public const string TeacherZh = "\u6559\u5e08";
    public const string TeachingClassZh = "\u6559\u5b66\u73ed";
    public const string CourseTypeZh = "\u8bfe\u7a0b\u7c7b\u578b";
    public const string NotesZh = "\u5907\u6ce8";
    public const string TimeZh = "\u65f6\u95f4";

    public const string AssessmentModeZh = "\u8003\u6838\u65b9\u5f0f";
    public const string AssessmentShortZh = "\u8003\u6838";
    public const string MethodZh = "\u65b9\u5f0f";
    public const string CourseHourCompositionZh = "\u8bfe\u7a0b\u5b66\u65f6\u7ec4\u6210";
    public const string CompositionZh = "\u7ec4\u6210";
    public const string CreditsZh = "\u5b66\u5206";
    public const string ExamZh = "\u8003\u8bd5";
    public const string CheckZh = "\u8003\u67e5";
    public const string TheoryZh = "\u7406\u8bba";
    public const string PracticeZh = "\u5b9e\u8df5";
    public const string TrainingZh = "\u5b9e\u8bad";
    public const string ExperimentZh = "\u5b9e\u9a8c";
    public const string ComputerZh = "\u4e0a\u673a";

    public static readonly string[] StructuredMetadataKeys =
    [
        Course,
        Class,
        Campus,
        Location,
        Teacher,
        TeachingClass,
        CourseType,
        Notes,
        Time,
        Date,
        Week,
        CourseZh,
        ClassZh,
        CampusZh,
        LocationZh,
        TeacherZh,
        TeachingClassZh,
        CourseTypeZh,
        NotesZh,
        TimeZh,
    ];

    public static readonly string[] SourceMetadataKeys =
    [
        Class,
        Campus,
        Location,
        Teacher,
        TeachingClass,
        CourseType,
        Time,
        Date,
        Week,
        ManagedBy,
        LocalSyncId,
        SourceFingerprint,
        SourceKind,
        ClassZh,
        CampusZh,
        LocationZh,
        TeacherZh,
        TeachingClassZh,
        CourseTypeZh,
        TimeZh,
    ];
}
