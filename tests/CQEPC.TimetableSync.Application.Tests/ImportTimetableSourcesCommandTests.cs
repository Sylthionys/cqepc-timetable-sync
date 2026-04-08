using CQEPC.TimetableSync.Application.UseCases.Import;
using CQEPC.TimetableSync.Domain.Enums;
using CQEPC.TimetableSync.Domain.Model;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Application.Tests;

public sealed class ImportTimetableSourcesCommandTests
{
    [Fact]
    public void CommandKeepsSelectedClassAndTimeProfile()
    {
        var command = new ImportTimetableSourcesCommand(
            new SourceFileSet("timetable.pdf", "progress.xls", "times.docx", new DateOnly(2026, 2, 23)),
            "Software Engineering 1",
            "campus-a-default",
            ProviderKind.Google,
            IncludeRuleBasedTasks: true);

        command.SelectedClassName.Should().Be("Software Engineering 1");
        command.SelectedTimeProfileId.Should().Be("campus-a-default");
        command.Provider.Should().Be(ProviderKind.Google);
        command.IncludeRuleBasedTasks.Should().BeTrue();
    }
}
