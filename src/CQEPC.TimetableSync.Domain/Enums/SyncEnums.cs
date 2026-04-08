namespace CQEPC.TimetableSync.Domain.Enums;

public enum ProviderKind
{
    Google,
    Microsoft,
}

public enum SyncTargetKind
{
    CalendarEvent,
    TaskItem,
}

public enum SyncChangeKind
{
    Added,
    Updated,
    Deleted,
    Unresolved,
}

public enum SyncChangeSource
{
    LocalSnapshot,
    RemoteManaged,
    RemoteTitleConflict,
    RemoteExactMatch,
    RemoteCalendarOnly,
}

public enum HomeScheduleEntryStatus
{
    Unchanged,
    Added,
    Deleted,
    UpdatedBefore,
    UpdatedAfter,
}

public enum HomeScheduleEntryOrigin
{
    LocalSchedule,
    RemoteExactMatch,
    RemotePendingDeletion,
    RemoteCalendarOnly,
}

public enum SyncMappingKind
{
    SingleEvent,
    RecurringMember,
    Task,
}

public enum ExportGroupKind
{
    SingleOccurrence,
    Recurring,
}

public enum WeekStartPreference
{
    Monday,
    Sunday,
}

public enum SourceItemKind
{
    RegularCourseBlock,
    PracticalSummary,
    AmbiguousItem,
}
