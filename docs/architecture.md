# Architecture

This document is the repository contract for layer boundaries and runtime ownership. Long-form workflow notes belong in the GitHub Wiki.

## 1. Solution shape

```text
src/
  CQEPC.TimetableSync.Domain/
  CQEPC.TimetableSync.Application/
  CQEPC.TimetableSync.Infrastructure/
  CQEPC.TimetableSync.Presentation.Wpf/
tests/
  CQEPC.TimetableSync.Domain.Tests/
  CQEPC.TimetableSync.Application.Tests/
  CQEPC.TimetableSync.Infrastructure.Tests/
  CQEPC.TimetableSync.Presentation.Wpf.Tests/
  CQEPC.TimetableSync.Presentation.Wpf.UiTests/
```

Allowed dependency direction:

```text
Presentation.Wpf -> Application -> Domain
Presentation.Wpf -> Infrastructure -> Application -> Domain
Infrastructure -> Application -> Domain
```

`Domain` must not reference WPF, parser libraries, persistence, OAuth, HTTP, or provider SDKs.

## 2. Layer responsibilities

### Domain

`CQEPC.TimetableSync.Domain` owns stable business concepts and invariant-enforcing value objects:

- parsed timetable concepts such as `ClassSchedule`, `CourseBlock`, `CourseMetadata`, `WeekExpression`, and `PeriodRange`;
- calendar concepts such as `SchoolWeek`, `TimeProfile`, and `ResolvedOccurrence`;
- unresolved source data through `UnresolvedItem`;
- export and sync concepts such as `ExportGroup`, `SyncPlan`, `PlannedSyncChange`, and `SyncMapping`;
- provider-independent enums and value objects that are part of the product contract.

Domain should stay small and deterministic. It should not decide how to read PDFs, write JSON, authenticate providers, or render UI.

### Application

`CQEPC.TimetableSync.Application` owns use-case boundaries and ports:

- parser interfaces: `ITimetableParser`, `IAcademicCalendarParser`, and `IPeriodTimeProfileParser`;
- normalization interface and result contracts;
- persistence interfaces for source catalogs, preferences, snapshots, and mappings;
- provider adapter abstractions such as `ISyncProviderAdapter`;
- workspace preview orchestration models;
- import command/query contracts;
- user preference models, timetable-resolution settings, and course override models.

Application defines what the app needs and how workflows compose. It should not know parser-library APIs, provider SDKs, file formats, WPF controls, or local storage implementation details.

### Infrastructure

`CQEPC.TimetableSync.Infrastructure` fulfills Application ports and owns external details:

- PDF/XLS/DOCX parser implementations and parser lexicons;
- timetable normalization implementation;
- local snapshot diff classification;
- JSON repositories for local source catalog, preferences, snapshots, and sync mappings;
- DPAPI-backed protected stores;
- Google Calendar / Google Tasks adapter implementation;
- Microsoft provider scaffolding and planned adapter implementation;
- provider payload builders, metadata mapping, recurrence conversion, and read-back repair helpers;
- provider HTTP client/proxy integration.

Infrastructure may reference Application and Domain. It should not reference WPF presentation types.

### Presentation.Wpf

`CQEPC.TimetableSync.Presentation.Wpf` owns the desktop app:

- WPF startup and shell composition;
- Home, Import, Settings, overlays, controls, converters, and UI-only helpers;
- view models and presentation formatting of structured Application data;
- resource dictionaries, localization, runtime language switching, and theme resources;
- UI test hooks, screenshot rendering, and automation IDs;
- composition of Application contracts with Infrastructure implementations.

Presentation may compose Infrastructure through dependency registration, but business rules must stay in Application/Infrastructure/Domain services rather than XAML code-behind.

## 3. Runtime ownership

### Startup

Presentation owns shell startup, theme/language initialization, and task-status display. It may show the shell before preview work completes. It must adopt completed local/provider preview results through `WorkspaceSessionViewModel` instead of duplicating parser or provider logic.

Optional Home render-cache restore is display-only. A real local/provider preview still refreshes and replaces stale cached content.

### Source onboarding

Application models the source catalog and attention state. Infrastructure validates local files and parses them. Presentation owns drag/drop, file-picking UI, status wording, and localized detail text.

Local source references are user-local. Missing files are represented as attention-needed state, not startup failures.

### Workspace preview

Workspace preview is an Application workflow backed by Infrastructure parser, normalization, diff, snapshot, and provider implementations. It produces structured results for Home and Import.

The preview workflow owns:

- source parsing;
- class selection readiness;
- effective first-week and time-profile resolution;
- saved override application;
- normalized occurrences and export groups;
- parser diagnostics/unresolved items;
- local snapshot diff state;
- optional provider preview state.

Presentation owns how these results are grouped, filtered, localized, animated, and selected.

### Import review

Import is a local review/adoption surface. Its course/repeat/occurrence grouping is a presentation projection over raw change identities. Import may save local overrides and accept the selected local baseline, but it must not write remote provider events.

Pinned fallback confirmations, schedule conflicts, and unresolved items are part of the preview/review contract. They must remain visible before ordinary change groups.

### Home provider apply

Home owns the provider-write action surface. Provider writes go through `ISyncProviderAdapter.ApplyAcceptedChangesAsync` with selected effective changes, current occurrences, export groups, existing mappings, provider defaults, and selected destination context.

Home apply must not trust Import grouping state as write identity; it must use raw accepted change IDs and provider-scoped mappings.

## 4. Parser placement

Parser interfaces live in Application. Parser implementations and CQEPC source-token lexicons live in Infrastructure:

- `Infrastructure/Parsing/Pdf/` for timetable PDFs;
- `Infrastructure/Parsing/Spreadsheet/` for teaching-progress XLS files;
- `Infrastructure/Parsing/Word/` for class-time DOCX files.

Parser docs live in `docs/parsers/`. Chinese source tokens may appear there only as exact CQEPC examples; parser behavior should otherwise be documented in English.

## 5. Persistence placement

Application owns persistence ports. Infrastructure owns repository implementations and storage format details.

Persistence responsibilities:

- user preferences and program settings;
- local source catalog state;
- accepted local schedule snapshots;
- provider sync mappings scoped by provider and destination;
- protected token/cache/password stores where applicable.

Presentation must not read or write repository files directly. It should request state changes through Application workflows or view-model services.

## 6. Provider adapter placement

Application defines provider adapter contracts. Infrastructure implements provider-specific adapters.

Provider adapter implementations own:

- authentication and token access;
- destination discovery;
- provider read-back and preview event mapping;
- create/update/delete payload construction;
- provider-safe private metadata;
- mapping repair and apply result mapping;
- provider-specific recurrence, time-zone, color, and task behavior.

Google Calendar is the supported timed-event provider. Microsoft provider types stay separate and must not borrow Google-only metadata names or mapping stores.

## 7. Code-behind boundary

WPF code-behind may contain only UI glue such as view initialization, attached event forwarding, focus/scroll coordination, and automation bridge plumbing.

Code-behind must not own:

- parser decisions;
- normalization rules;
- diff classification;
- provider ownership checks;
- provider payload construction;
- persistence file formats;
- localization fallback policy beyond resource lookup wiring.

## 8. Test ownership

- Domain tests cover value-object invariants and provider-independent model behavior.
- Application tests cover contracts, preferences, preview orchestration, and use-case behavior.
- Infrastructure tests cover parsers, normalization, diffing, persistence, provider payloads, and provider adapter behavior.
- Presentation tests cover view models, formatting, localization/theme services, screenshot/export hooks, and UI smoke automation.

Parser and provider regression tests should use sanitized fixtures or generated fixtures. Private school exports and personal provider data must not be required for normal test runs.
