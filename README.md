<p align="center">
  <img src="src/CQEPC.TimetableSync.Presentation.Wpf/Assets/Brand/app-logo.svg" width="104" alt="CQEPC Timetable Sync logo" />
</p>

<h1 align="center">CQEPC Timetable Sync</h1>

<p align="center">
  Local-first CQEPC timetable import, preview, and Google Calendar sync.
</p>

<p align="center">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="WPF" src="https://img.shields.io/badge/UI-WPF-0B5CAD" />
  <img alt="Windows" src="https://img.shields.io/badge/platform-Windows-0078D4?logo=windows&logoColor=white" />
  <img alt="Status: Pre-Alpha" src="https://img.shields.io/badge/status-Pre--Alpha-8A6D3B" />
  <a href="https://github.com/Sylthionys/cqepc-timetable-sync/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/Sylthionys/cqepc-timetable-sync?style=flat&logo=github" /></a>
  <a href="https://github.com/Sylthionys/cqepc-timetable-sync/forks"><img alt="GitHub forks" src="https://img.shields.io/github/forks/Sylthionys/cqepc-timetable-sync?style=flat&logo=github" /></a>
  <a href="LICENSE"><img alt="License: Apache-2.0" src="https://img.shields.io/badge/license-Apache--2.0-3D7A2F" /></a>
</p>

CQEPC Timetable Sync is a local-first Windows desktop app for turning CQEPC timetable source files into a reviewable calendar-sync workflow.

The app imports local CQEPC timetable documents, normalizes them into dated course occurrences, shows the user what will change, and applies only selected app-managed changes to Google Calendar. Microsoft provider support is represented in architecture and infrastructure scaffolding, but the supported desktop sync target today is Google Calendar.

Current release stage: **Pre-Alpha**.

## Supported capabilities

- Import three user-local source files: timetable PDF, teaching-progress XLS, and class-time DOCX.
- Parse CQEPC regular timetable blocks, semester week-date mappings, and period-time profiles.
- Keep practical summary/footer material out of automatic event export and represent ambiguous course-like source text as unresolved review items.
- Resolve courses into concrete dated occurrences before creating any recurring export groups.
- Preserve parser warnings, diagnostics, unresolved items, source fingerprints, and source context for review.
- Let the user choose a class when a PDF contains more than one class section.
- Support manual first-week overrides, automatic or explicit time-profile selection, per-course time-profile overrides, and course/occurrence schedule overrides.
- Show a Home calendar preview and an Import review surface before applying changes.
- Keep Import as a local review/adoption flow and keep provider writes on the Home apply path.
- Connect to Google Calendar with a user-provided installed-app OAuth client JSON, system-browser loopback auth, and local DPAPI-protected token storage.
- Discover writable Google calendars, read selected-calendar preview events, and create/update/delete app-managed timed events.
- Preserve Google Calendar time-zone and color metadata in preview, diffing, and apply reconciliation.
- Support optional rule-generated Google Tasks separately from timed course events; task rules are disabled by default.
- Persist user preferences, local source references, snapshots, provider mappings, and provider defaults locally.

## Non-goals

The project does not currently try to provide:

- school portal login, scraping, or browser automation;
- OCR from screenshots or scanned/image-only timetables;
- automatic guessing of ambiguous practical-course time slots;
- a generic multi-school parser before CQEPC source shapes are stable;
- a background sync daemon;
- Microsoft Calendar or Microsoft To Do as a supported desktop apply target.

## Tech stack

- .NET 8
- WPF
- MVVM
- xUnit tests
- Google Calendar / Google Tasks client libraries in Infrastructure
- Microsoft Graph adapter scaffolding reserved for planned provider work

## Solution layout

```text
src/
  CQEPC.TimetableSync.Domain/           Core timetable, occurrence, diff, and mapping models
  CQEPC.TimetableSync.Application/      Use-case contracts, preview orchestration, preferences
  CQEPC.TimetableSync.Infrastructure/   Parsers, normalization, persistence, provider adapters
  CQEPC.TimetableSync.Presentation.Wpf/ WPF shell, views, view models, localization, UI testing hooks
tests/
  CQEPC.TimetableSync.Domain.Tests/
  CQEPC.TimetableSync.Application.Tests/
  CQEPC.TimetableSync.Infrastructure.Tests/
  CQEPC.TimetableSync.Presentation.Wpf.Tests/
  CQEPC.TimetableSync.Presentation.Wpf.UiTests/
docs/
  architecture.md
  parsers/
    timetable-pdf.md
    timetable-pdf-source-tokens.md
    teaching-progress-xls.md
    class-time-docx.md
  workflows/
    import-diff.md
  providers/
    google-calendar.md
    microsoft.md
SPEC.md
README.md
```

`src/CQEPC.TimetableSync.Presentation.Wpf/` is the only desktop entry point in the current solution.

## Build and test basics

From the repository root:

```powershell
dotnet restore CQEPC.TimetableSync.sln
dotnet build CQEPC.TimetableSync.sln
dotnet test CQEPC.TimetableSync.sln
```

The WPF UI smoke tests and screenshot harness require a Windows desktop-capable environment. Long-form workflow and troubleshooting notes belong in the [GitHub Wiki](https://github.com/Sylthionys/cqepc-timetable-sync/wiki).

`Directory.Solution.props` intentionally disables solution-level parallel restore/build to avoid NuGet restore graph failures seen in some SDK/MSBuild environments. If a WPF app instance is still running from `src/CQEPC.TimetableSync.Presentation.Wpf/bin/<Configuration>/net8.0-windows/`, Windows can lock build output files; close or terminate the old process before rebuilding.

## Documentation

Repository contracts that should be reviewed with code changes:

- Product and behavior contract: [SPEC.md](SPEC.md)
- Architecture boundaries: [docs/architecture.md](docs/architecture.md)
- PDF parser contract: [docs/parsers/timetable-pdf.md](docs/parsers/timetable-pdf.md)
- PDF source-token companion: [docs/parsers/timetable-pdf-source-tokens.md](docs/parsers/timetable-pdf-source-tokens.md)
- XLS parser contract: [docs/parsers/teaching-progress-xls.md](docs/parsers/teaching-progress-xls.md)
- DOCX parser contract: [docs/parsers/class-time-docx.md](docs/parsers/class-time-docx.md)
- Import diff contract: [docs/workflows/import-diff.md](docs/workflows/import-diff.md)
- Google Calendar provider contract: [docs/providers/google-calendar.md](docs/providers/google-calendar.md)
- Microsoft provider contract: [docs/providers/microsoft.md](docs/providers/microsoft.md)

Long-form guides belong in the GitHub Wiki:

- [Wiki Home](https://github.com/Sylthionys/cqepc-timetable-sync/wiki)

Keep page-level navigation in the Wiki itself instead of duplicating each Wiki page here.

## Acknowledgements

CQEPC Timetable Sync builds on these external modules and tools:

Runtime and app modules:

- [ClosedXML](https://github.com/ClosedXML/ClosedXML) and [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) for spreadsheet-facing test and import infrastructure.
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM primitives.
- [Google APIs Client Library for .NET](https://github.com/googleapis/google-api-dotnet-client): `Google.Apis.Auth`, `Google.Apis.Calendar.v3`, and `Google.Apis.Tasks.v1`.
- [Microsoft Authentication Library](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet): `Microsoft.Identity.Client` and `Microsoft.Identity.Client.Broker`.
- [Noda Time](https://github.com/nodatime/nodatime) for IANA time-zone and offset handling.
- [PdfPig](https://github.com/UglyToad/PdfPig) for text-based timetable PDF parsing.
- [System.Security.Cryptography.ProtectedData](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData) for DPAPI-backed local protection.
- [System.Text.Encoding.CodePages](https://www.nuget.org/packages/System.Text.Encoding.CodePages) for legacy code-page support where source files require it.

Test and validation modules:

- [FlaUI](https://github.com/FlaUI/FlaUI): `FlaUI.Core` and `FlaUI.UIA3` for UI automation smoke coverage.
- [FluentAssertions](https://github.com/fluentassertions/fluentassertions) for readable test assertions.
- [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest), [xUnit.net](https://github.com/xunit/xunit), [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit), and [Xunit.StaFact](https://github.com/AArnott/Xunit.StaFact) for automated tests.
- [coverlet.collector](https://github.com/coverlet-coverage/coverlet) for test coverage collection support.

Dependency versions are centrally pinned in [Directory.Packages.props](Directory.Packages.props).

## Security and source-file hygiene

- Original school-exported files are local input materials, not repository assets.
- Do not commit private timetable PDFs, teaching-progress workbooks, class-time documents, OAuth client secrets, refresh tokens, tenant IDs, personal calendar IDs, or local provider mapping stores.
- Store personal samples only in ignored local folders such as `local-samples/`, `tests/Fixtures/Local/`, or `tests/Fixtures/SourceSamples/`.
- Add parser regression fixtures only when they are intentionally sanitized and documented as sanitized assets.
- Google tokens, render caches, and proxy secrets are protected locally with user-scoped DPAPI where applicable.
- Provider ownership must be determined from provider-safe private metadata or local mappings, never from ordinary event description text.
