using CQEPC.TimetableSync.Application.UseCases.Workspace;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Application.Tests;

public sealed class WorkspacePreferencesModelsTests
{
    public static TheoryData<string, string> CleanChineseAliases =>
        new()
        {
            { CourseTypeLexicon.Theory, CourseTypeKeys.Theory },
            { CourseTypeLexicon.Lab, CourseTypeKeys.Lab },
            { CourseTypeLexicon.PracticalTraining, CourseTypeKeys.PracticalTraining },
            { CourseTypeLexicon.Practice, CourseTypeKeys.PracticalTraining },
            { CourseTypeLexicon.Computer, CourseTypeKeys.Computer },
            { CourseTypeLexicon.Extracurricular, CourseTypeKeys.Extracurricular },
        };

    public static TheoryData<string> MojibakeAliases =>
        new(CourseTypeLexicon.KnownMojibakeAliases.ToArray());

    [Theory]
    [MemberData(nameof(CleanChineseAliases))]
    public void CourseTypeKeysResolveMapsCleanChineseAliases(string alias, string expectedKey)
    {
        CourseTypeKeys.Resolve(alias).Should().Be(expectedKey);
    }

    [Theory]
    [MemberData(nameof(MojibakeAliases))]
    public void CourseTypeKeysResolveDoesNotTreatMojibakeAliasesAsSupportedValues(string alias)
    {
        CourseTypeKeys.Resolve(alias).Should().Be(CourseTypeKeys.Other);
    }
}
