# Architecture

## 1. Overview

The repository is structured around four runtime layers plus tests:

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
```

Dependency direction is one-way:

`Presentation.Wpf -> Infrastructure -> Application -> Domain`

`Presentation.Wpf -> Application -> Domain`

`Domain` must not depend on other project layers.

## 2. Layer Responsibilities

### Domain

`CQEPC.TimetableSync.Domain` owns the core concepts of the system:

- `CourseBlock` and `ClassSchedule` from the timetable PDF
- `UnresolvedItem` values for ambiguous source data
- `SchoolWeek` values from the XLS file
- `TimeProfile` values from the DOCX file
- `ResolvedOccurrence` values produced by normalization
- `ExportGroup` values for lossless single-occurrence or recurring export payloads
- `SyncPlan` and `SyncMapping` state
- shared metadata/value objects such as `CourseMetadata`, `WeekExpression`, and `PeriodRange`

Domain should contain only stable business concepts and small invariant-enforcing value objects. It should not know about WPF, persistence, HTTP, OAuth, or parser libraries.

### Application

`CQEPC.TimetableSync.Application` owns use-case boundaries and ports:

- parser interfaces such as `ITimetableParser`, `IAcademicCalendarParser`, and `IPeriodTimeProfileParser`
- normalization interfaces
- local persistence interfaces
- provider adapter abstractions
- workspace preview orchestration and preview request/result models, including structured preview/apply status data
- user-preference models and defaults, including nested timetable-resolution state plus persisted course-schedule overrides for manual confirmation/editing
- shared parser result contracts such as `ParserResult<T>`, `ParseWarning`, and `ParseDiagnostic`

Application orchestrates workflows later, but it should stay implementation-agnostic. It defines what the app needs to do, not how PDF parsing or remote sync is performed.

### Infrastructure

`CQEPC.TimetableSync.Infrastructure` owns external details:

- PDF parser implementation
- XLS parser implementation
- DOCX parser implementation
- normalization implementation
- local preference persistence
- local snapshot persistence
- local snapshot diff classification
- Google adapter implementation
- Microsoft adapter implementation

Infrastructure references Application and Domain because it fulfills interfaces defined there.

### Presentation

`CQEPC.TimetableSync.Presentation.Wpf` owns the desktop UI:

- WPF app startup
- shell window and future navigation
- page views
- view models
- converters, UI-only helpers, and presentation-owned formatting for dynamic workspace/apply/diff/source-file text
- localization resource dictionaries, culture resolution, and runtime language switching

Presentation can depend on Application contracts and compose Infrastructure implementations, but business logic should not be implemented in XAML code-behind.
The current Presentation layer also owns the lightweight course editor overlay used by both Home and Import, the compact course-presentation overlay for per-course time-zone and Google Calendar color overrides, the hero-free Home month board with direct apply/sync actions, plus the Import-only grouping surfaces that route same-name parsed, deleted, added, and unresolved schedules into course headers while delegating persistence and preview refresh back to `WorkspaceSessionViewModel`.
Presentation must preserve a strict split between Import's local-only apply path and Home's provider-write apply path so preview adoption and remote sync cannot fire twice for the same accepted additions.
Import diff selection is also presentation-owned: the whole change card toggles selection, checkbox chrome is visual-only for those rows, and editable parsed/unresolved rows open directly from card clicks instead of a separate trailing action chip.
Presentation also owns runtime appearance refresh for theme changes: workspace preferences remain the source of truth, `ThemeService` reapplies palette resources in place, shared/page XAML now resolves theme brushes through `DynamicResource` so live WPF visuals repaint without rebuilding the shell window, and the Settings page now routes week-start, language, Google Calendar default time zone, theme, and About access through a dedicated program-settings overlay opened from a compact trailing button instead of an always-expanded calendar-display card. About presentation is shell-owned and replaces the program-settings overlay visually rather than stacking inside it.
For the default Google Calendar color selector, Presentation now binds the committed value by stable `colorId` instead of transient option-object identity so an immediate preference refresh cannot shift the visible selection to an adjacent palette item.
For course-schedule and remote-preview rendering, Presentation must treat `DateTimeOffset.DateTime` as the canonical wall-clock value of a resolved occurrence or preview item. Presentation must not reproject those values through `LocalDateTime` before diff rendering, editor seeding, or rule classification, otherwise per-course time-zone overrides can drift into different dates/times when reopened and saved.
Workspace preview also re-aligns managed recurring Google instances to saved local mappings before diffing. That alignment step is only allowed to rewrite local identity fields such as `LocalSyncId` and source fingerprint binding; provider payload fields like Google Calendar color id must be preserved so downstream diff logic compares the actual remote event instead of a partially reconstructed copy.
Presentation also owns the transient bottom-right task center used during startup and provider work. `WorkspaceSessionViewModel` reports tracked tasks such as remembered-source loading, preview generation, provider connection checks, and Google existing-event sync; `ShellViewModel` projects them into a compact collapsed count chip plus an expandable detail list.
During interactive startup, `WorkspaceSessionViewModel` now builds the first Home preview in local-only mode and leaves Google Home preview import to the subsequent tracked Google-sync task. That keeps the initial parse/render path responsive even when remote calendar reads are slow.
Presentation now also revalidates Google connection state against the DPAPI-backed token store before startup finishes and before Home sync/apply or calendar refresh runs. A stale saved account summary or selected calendar in workspace preferences is treated as cache data only; if the token store is empty, Presentation clears that stale Google state and routes the user back to Settings instead of letting Home appear to apply changes while provider work is already disconnected.

## 3. Parser Pipeline

The parser pipeline is intentionally split by source responsibility.

### PDF timetable parser

CQEPC Chinese labels and marker tokens used by the PDF parser live in `TimetablePdfLexicon` so parsing logic can stay ASCII-safe and encoding-stable.

Input: timetable PDF

Output:

- parsed `ClassSchedule` values containing `CourseBlock` items
- unresolved ambiguous timetable blocks that cannot be parsed into regular course metadata
- parse warnings and diagnostics

The PDF parser is represented by `ITimetableParser` and is the source of truth for regular class blocks only.
The current `TimetablePdfParser` implementation is CQEPC-layout-specific and uses `PdfPig` positioned letters plus drawn page paths to:

- split the PDF into ordered class sections by `课表` headers
- analyze the CQEPC page template from weekday headers, column rectangles, left-side grid bands, and footer markers
- recover seven weekday columns from page rectangles, with weekday-header fallback
- derive timetable-body row bands from the left-side grid geometry and rebuild lines inside those layout-scoped bands
- extend the effective body-bottom when the header-page footer strip still contains a weekday-cell title fragment, so cross-page metadata carryover is not dropped before parsing
- assign band-local lines back to weekday columns so same-baseline text from adjacent columns does not merge
- parse `(n-m节)` metadata leads, raw week expressions, tagged campus/location/teacher/class-composition fields, and remaining labeled notes
- emit unresolved items instead of guessing when ambiguous blocks cannot be normalized safely, while ignoring footer practical-summary notes entirely so only regular timetable cells participate in parsing output
- silently consume successful carryover metadata tails instead of surfacing user-visible `PDF111` noise
- emit cell-level diagnostics for skipped blocks such as missing period leads, missing week expressions, merged-cell ambiguity, metadata-tag failures, and empty/non-course cells

### XLS teaching progress parser

CQEPC Chinese worksheet labels used by the XLS parser live in `TeachingProgressXlsLexicon` so matching logic is centralized and easier to maintain.

Input: teaching progress XLS

Output:

- `SchoolWeek` values
- optional warnings and diagnostics when the mapping is incomplete

The XLS parser is represented by `IAcademicCalendarParser` and must not define regular timetable events.
The current implementation scans all visible worksheets, extracts the `月 / 日 / 周` week grid only, ignores trailing arrangement semantics, emits diagnostics for malformed or conflicting grids, and falls back to a manual first-week override when workbook dates cannot be trusted.

Workspace preview stores the parsed week-1 date separately as an auto-derived timetable-resolution value so Settings can show whether the current effective date is manual or XLS-derived.

### DOCX class-time parser

CQEPC Chinese DOCX labels and row-keyword tokens used by the DOCX parser live in `ClassTimeDocxLexicon` so the parser stays ASCII-safe and encoding-stable.

Input: class-time DOCX

Output:

- `TimeProfile` values
- optional warnings and diagnostics when profile mapping is incomplete

The DOCX parser is represented by `IPeriodTimeProfileParser` and must not infer week semantics or calendar events.
The current implementation reads the DOCX package directly via Open XML ZIP/XML parsing, maps range-based slots such as `1-2` and `3-4`, infers structured course-type tags from row labels, and preserves the `5-6` noon-window note as structured profile metadata.
Those profiles now feed a resolution model that supports automatic matching, an explicit default profile, and class-scoped per-course overrides.

## 3.5 Local Source Onboarding

Before real parsing runs, the app owns a local onboarding layer for user-selected source files.

Responsibilities:

- accept source files via drag-and-drop or manual file picker
- validate supported file extensions for PDF, XLS, and DOCX
- persist user-local source references and the last used folder
- persist machine-readable onboarding state such as `SourceAttentionReason` and ordered `CatalogActivityEntry` values
- surface import status and parser-availability state
- route dropped or browsed files into the correct PDF/XLS/DOCX slot automatically
- execute timetable PDF parsing after selection so Settings can show parsed classes, warnings, and diagnostics
- allow per-file replace and remove actions without redoing unrelated settings
- keep startup safe when remembered files or folders no longer exist

The current Settings flow presents one unified source-files panel that owns drag-and-drop, bulk browse, overall status, and per-slot replace/remove actions.
The active workspace preview now parses PDF, XLS, and DOCX together when the required files are ready, while still exposing partial Settings feedback when only some sources are present.

The current storage strategy is reference-first:

- source files are selected by the user and may live anywhere on the local machine
- the app stores source metadata in `%LocalAppData%\CQEPC Timetable Sync\user-settings.json`
- workspace preferences in `%LocalAppData%\CQEPC Timetable Sync\workspace-preferences.json`
- the latest accepted snapshot in `%LocalAppData%\CQEPC Timetable Sync\latest-snapshot.json`
- Google sync mappings in `%LocalAppData%\CQEPC Timetable Sync\google-sync-mappings.json`
- Google OAuth tokens in `%LocalAppData%\CQEPC Timetable Sync\tokens\google\` with DPAPI-protected payloads
- Microsoft sync mappings in `%LocalAppData%\CQEPC Timetable Sync\microsoft-sync-mappings.json`
- Microsoft auth state and tokens in `%LocalAppData%\CQEPC Timetable Sync\tokens\microsoft\` with DPAPI-protected payloads
- `SourceStorageMode.ReferencePath` is the only active onboarding mode in the current phase
- `%LocalAppData%\CQEPC Timetable Sync\sources\` is reserved for a future app-managed copy mode
- no production logic should assume source files live inside the repo
- persisted JSON/text artifacts use UTF-8, and direct text I/O must not rely on the system-default encoding
- preference writes are coalesced during interactive editing, and `App` flushes pending workspace-preference persistence before shutdown completes

For UI regression and smoke testing, Presentation.Wpf also owns a small app-internal test harness:

- `--ui-test` / `--ui-screenshot` seeds deterministic sample data, navigates to a requested page, and exports the requested page root with WPF-native PNG rendering
- screenshot mode prefers an internal render-only path that does not call `Show()` on the shell window; when a page still needs a live presentation source, the app automatically falls back to an off-screen `ShowActivated=false` no-activate tool window so the export stays local and does not rely on foreground desktop capture
- `--ui-automation` is reserved for FlaUI runs and launches the shell in the same off-screen background mode, keeping the app out of the taskbar and reducing focus disruption while UIA still drives the tree; this remains a live window, not a headless mode
- the shell window is explicitly constrained in background mode to a fixed off-screen size with `ShowActivated=false`, `ShowInTaskbar=false`, `WS_EX_NOACTIVATE`, and `WS_EX_TOOLWINDOW` so the handle stays automation-visible without surfacing to the user's foreground workflow
- FlaUI interaction is semantic-first and should prefer UIA patterns such as `Invoke`, `SelectionItem`, `Toggle`, and `Value` instead of physical mouse input or foreground activation
- smoke coverage should keep validating direct-click affordances on presentation-owned controls such as full-surface combo boxes and compact theme toggles, not only semantic expand/select helpers
- smoke coverage also captures the program-settings overlay in both light and dark themes so theme repaint regressions are visible in automation artifacts
- real-storage manual UI coverage can call into the in-app automation bridge to open the first selected-day Home course editor and specific date-picker dropdowns without relying on fragile background card clicks
- automation screenshot diagnostics prefer app-side page rendering over desktop capture: the running app hosts a local automation-only bridge that receives screenshot requests from the FlaUI test process and calls `UiScreenshotExporter` against the current page root; FlaUI window capture remains only as a fallback diagnostic path
- FlaUI is kept as a separate smoke layer for shell launch, navigation, page-root discovery, sidebar collapse, primary-action/About entry-point checks, and background-safe automation screenshot verification

## 3.6 Presentation Localization

Localization ownership now sits in `CQEPC.TimetableSync.Presentation.Wpf`.

Key responsibilities:

- load the persisted preferred culture before the shell is shown
- support `Follow System` as a `null` preference value plus explicit `zh-CN` and `en-US`
- resolve effective culture by exact match, parent/language match, then `en-US`
- merge UTF-8 WPF resource dictionaries from `Resources/Localization/Strings.en-US.xaml` and `Resources/Localization/Strings.zh-CN.xaml`
- replace the active merged dictionaries on every runtime language switch so `DynamicResource` labels and computed view-model text re-evaluate against the new culture
- bind the program-settings language selector by a stable culture key instead of transient option-object identity so a runtime rebuild cannot leave stale selected items or duplicate entries in the WPF combo box
- keep XAML-owned text on `DynamicResource` keys
- refresh computed view-model text through `LanguageChanged` notifications and presentation formatting helpers
- keep touched Home/Import labels in those dedicated dictionaries instead of scattering hardcoded Chinese strings through page XAML

The localization boundary is explicit:

- Application and Domain may carry stable parser codes and fallback text
- Presentation localizes parser warnings, diagnostics, and unresolved-item summaries/reasons by code first
- if no resource key exists, Presentation falls back to the stored parser message or unresolved text
- `RawSourceText` is preserved exactly as parsed and is never localized
- `.editorconfig` requires UTF-8 for the text assets involved in this flow

## 4. Normalization Pipeline

CQEPC Chinese week-expression, odd/even, and sports-venue matching tokens used during normalization are centralized in `TimetableNormalizationLexicon`, while shared course-type labels live in `CourseTypeLexicon`.

The normalization pipeline converts parsed source data into export-ready schedule data.

Required order:

1. accept parsed PDF blocks, parser unresolved items, semester week ranges, and period-time profiles
2. select a class when multiple classes are present
3. resolve raw week expressions into exact week numbers
4. select the applicable period-time profile
5. convert each valid timetable block into concrete dated occurrences
6. keep unresolved items separate from valid occurrences
7. derive recurring export groups only when the merge is lossless

Important constraints:

- no silent week dropping
- no silent time guessing
- no merging across meaningful metadata differences
- all occurrence generation must preserve source fingerprints and structured metadata
- sync identity generation, logical diff keys, and week parsing use `CultureInfo.InvariantCulture`
- exact-campus profile matching is conservative in v1; when no explicit profile override exists, resolution first checks class-scoped per-course overrides, then an explicit default profile, then auto-selection narrows by campus and mapped course type, falls back to another same-campus profile only when that fallback defines the exact requested periods, and unresolveds anything still ambiguous
- parser-originated practical summaries remain unresolved; normalization-originated failures are emitted as unresolved regular course blocks with concrete reasons
- normalization-originated unresolved regular course blocks now carry stable codes so Presentation can localize them without changing the underlying fallback reason text
- before diff creation, Application applies any persisted course-schedule overrides to replace the matching source-fingerprint occurrences or unresolved items with user-confirmed concrete occurrences

The current `TimetableNormalizer` implementation lives in Infrastructure and is covered with fixture-driven tests for odd/even expansion, sparse weeks, cadence-based group splitting, location-sensitive grouping, conservative profile selection, same-campus time-profile fallback confirmations, and unresolved fallbacks.
The current class selection state is kept in memory inside the WPF view models. Separate persistence of the parsed class choice is deferred.

## 5. Sync Pipeline

The sync pipeline is a preview-first workflow, not a fire-and-forget export.

Required order:

1. load the latest local snapshot
2. build the newly normalized occurrence set
3. derive optional task candidates from explicit rules
4. compare new local data against the previous local snapshot
5. later compare app-managed remote items for the selected provider
6. classify Added, Updated, Deleted, fallback-confirmation, and Unresolved items
7. show the diff in the UI with the primary section order Unresolved, Deleted, Parsed Courses, Added, while keeping Parsed Courses visible even when the regular diff groups are empty and letting the user switch between grouped repeat rules and per-course all-times inspection
8. apply only user-approved changes
9. persist new local snapshot and provider mapping state

The current implementation completes steps 1, 2, 4, 6, 7, and 8 locally by diffing against the saved snapshot baseline.
Provider-specific remote execution is implemented through dedicated Infrastructure adapters:

- Google desktop OAuth loopback auth using a user-selected installed-app JSON
- writable-calendar discovery for the connected Google account
- Google Calendar create/update/delete for app-managed timed events
- recurring-series creation plus recurring-instance update/delete by matching `originalStartTime`
- Google Tasks create/update/delete on the default `@default` list for explicit rule-based tasks
- provider-safe event metadata in `extendedProperties.private`
- local Google sync mappings for remote IDs, fingerprints, recurring-master IDs, and original-start timestamps
- Microsoft desktop auth using MSAL with WAM preferred and browser fallback for local interactive sign-in
- writable Outlook calendar discovery plus owned Microsoft To Do list discovery for the connected Microsoft account
- Outlook Calendar create/update/delete for app-managed timed events, including recurring-series creation and recurring-member maintenance with immutable IDs
- Microsoft To Do create/update/delete for explicit rule-based tasks, plus linked-resource creation when a paired Outlook event exists
- provider-safe Microsoft metadata in Graph open extensions
- local Microsoft sync mappings for remote IDs, fingerprints, recurring-master IDs, and original-start timestamps

Preview generation remains local-first, but the Google preview path now uses two stages plus two windows:

- stage 1: build the first Home preview from local sources only, so startup parsing/normalization can finish and render without waiting on Google
- stage 2: run a tracked Google preview refresh that reads remote events and merges them into the already-rendered Home board

- a Home display window that can import the selected Google calendar into the Home board when the user enables the Google preview toggle
- a semester-scoped deletion window that limits destructive Google delete candidates to the timetable term span

Within that flow, exact same-title same-time Google events suppress duplicate Adds only when the remote item is already the exact same managed event identity, while same-title different-time Google events become red delete candidates only when they fall inside the deletion window.
The Google apply path now also treats the preview-resolved remote event as authoritative when a stored mapping has drifted to a stale remote item id, so update/delete repairs can rebind the local mapping to the managed event that preview actually matched.
Preview-side Google reconciliation also no longer treats the accepted local snapshot as proof that a remote write succeeded: if an occurrence has neither a saved mapping nor a matching managed remote event, diff generation emits a fresh `RemoteManaged` add so Home can repair the missing Google write on the next apply.
Preview-side Google reconciliation now also attaches a matched managed remote event back onto local-snapshot delete rows before apply. That prevents a class switch from consuming the managed remote event during diff generation and then quietly deleting only the local baseline while leaving the real Google event behind.
When a managed Google event matches payload but its app-managed identity metadata has drifted, reconciliation now emits a `RemoteManaged` update instead of an exact match so apply refreshes `LocalSyncId` / source-fingerprint metadata and keeps later previews controllable.
Google apply now executes accepted calendar changes in deterministic delete -> update -> add order, runs calendar writes serially, deduplicates repeated recurring-series deletes, caches recurring-instance lookups per series, and keeps managed duplicate remote items beyond the expected exact-match count visible as delete candidates so switching the selected class clears stale copies instead of hiding them as extra exact matches. For recurring adds, the Google payload builder now emits exact weekly recurrence shapes using `UNTIL` plus `EXDATE` for skipped occurrences, and the executor rolls back any series that does not materialize every expected instance before falling back to exact single-event inserts. For recurring-instance updates whose timed range already drifted away from the local occurrence, the executor no longer relies on Google exception semantics; it deletes the wrong recurring member and recreates one exact managed single event instead.
Presentation-side post-apply Google convergence now also scans the refreshed preview for represented app-managed duplicate groups whose payload count is higher than the current normalized timetable count. When such a group exists, `WorkspaceSessionViewModel` automatically applies the represented remote-managed delete rows for the extra duplicates and immediately re-reads Google preview so identical double-booked managed lessons are reduced back to the expected count without a second manual apply pass.
Google remote read-back now explicitly requests `start/end/originalStartTime` time-zone fields plus `colorId`, resolves timed instances against the event's own zone before handing them to Home or diff generation, and compares the managed remote `colorId` against the effective local Google event color during reconciliation. This closes the previous gaps where Google events could be written correctly but read back through the machine offset, or where color-only drift could be missed until a later manual edit.
Workspace apply now backfills local Google mappings and accepted local snapshot occurrences for already-exact managed matches, rather than only persisting provider results for rows that required a write. Workspace preview also persists the same exact-match mapping repair when it can confidently bind a managed Google event or recurring instance back to the local occurrence. This keeps class-switch applies and later preview-only sessions from ending in a visually correct remote calendar that the local snapshot/mapping store still cannot fully manage on the next preview.
The Google provider path now reads two preference-backed time-zone values from the current connection context on every preview/apply/edit request: a preferred write time zone and a remote read fallback time zone. Program Settings exposes that as one default UTC-time selector, defaults it to `UTC+8` (`Asia/Shanghai`), writes the selected zone explicitly into Google Calendar payloads, and uses the same value as the fallback when Google omits a remote event time zone.
The same persisted program-behavior settings now also decide whether startup should perform the automatic Google preview sync pass at all, and whether the bottom-right running-task status notification chip is visible while tracked background work is active.
Presentation now renders the Home selected-day agenda with a provider-color dot instead of the previous course-type chip. Update rows that only differ in non-layout provider-managed fields such as Google Calendar color are collapsed into one orange agenda card so Home does not show duplicate-looking before/after entries for the same lesson. The final Home agenda projection also deduplicates same-slot same-title same-location cards after diff/source normalization so a separate managed-remote cleanup row cannot bring the same visible lesson back as a second agenda card.
Accepted snapshot persistence is likewise class-scoped during apply so replacing one selected class does not reintroduce delete noise from another class slice in the next preview.

## 6. Local Persistence Responsibilities

Local persistence belongs in Infrastructure behind an Application port.

It is responsible for storing:

- imported source file references and their onboarding status
- the remembered last-used local folder
- a future app-local copy location when copy mode is implemented
- workspace preferences such as week start, first-week override, provider defaults, and course-type appearance rules
- the latest parsed and normalized snapshot
- export groups derived from normalized occurrences
- unresolved items
- a future persisted selected class
- the selected period-time profile preference
- task-generation rules
- provider item mappings
- last sync summary
- provider auth settings and connection summaries
- encrypted provider token payloads

Persistence should be local, inspectable during development, and stable enough to support diffs and regression tests.
Parser and regression tests should rely on sanitized fixtures rather than private raw timetable exports.
Tracked private timetable exports should not live in the repository; repo-local developer materials, if used, must stay in gitignored folders.
The timetable PDF parser is covered with synthetic PDF fixtures that mimic CQEPC same-template segmented grid geometry, wrapped metadata, multi-class sections, continuation pages, and footer summaries without requiring private source documents.
Google sync acceptance is also covered with focused cloned-storage UI tests that exercise live Google add, update, and delete behavior, including switching from the previous selected class to a different parsed class and reading remote events back for proof.
The teaching-progress parser is covered with in-memory worksheet fixtures rather than checked-in private school exports.
The DOCX parser is covered with synthetic Open XML fixtures that mimic the CQEPC table shape without requiring private source documents.

## 7. Provider Adapter Placement

Provider adapters will live in Infrastructure:

- `src/CQEPC.TimetableSync.Infrastructure/Providers/Google/`
- `src/CQEPC.TimetableSync.Infrastructure/Providers/Microsoft/`

Rules for those adapters:

- Google and Microsoft stay separate
- event logic and task logic stay separate inside each provider family
- each adapter only manages objects created by this app
- provider metadata storage stays provider-safe
- destructive operations are driven by the preview diff, never by blind overwrite

The Google adapter is implemented in `src/CQEPC.TimetableSync.Infrastructure/Providers/Google/` and currently includes:

- `GoogleSyncProviderAdapter` for auth, calendar discovery, and preview-first apply execution
- `GooglePayloadBuilders` plus a Google-specific text formatter for event descriptions and task notes
- `GoogleTimeZoneResolver` for Google write-zone normalization and remote-event time-zone resolution, including future preference-driven extension points
- `ProtectedFileDataStore` for DPAPI-backed OAuth token persistence
- use of `extendedProperties.private` for app-managed Google event metadata
- explicit stream-based loading of the installed-app OAuth JSON so client configuration parsing is UTF-8-safe

The Microsoft adapter is implemented in `src/CQEPC.TimetableSync.Infrastructure/Providers/Microsoft/` and currently includes:

- `MicrosoftSyncProviderAdapter` for auth, destination discovery, and preview-first apply execution
- `MicrosoftAuthService` plus `MicrosoftTokenCacheStore` for MSAL public-client auth and DPAPI-backed token caching
- `MicrosoftGraphClient` for thin Graph REST access
- `MicrosoftPayloadBuilders` plus a Microsoft-specific text formatter for event descriptions and task notes
- Graph open extensions for app-managed Microsoft metadata and immutable-ID-based recurring-member tracking

## 8. What Must Never Live in Code-Behind

The following should never live in WPF code-behind:

- parser logic
- week-expression parsing
- normalization and occurrence generation
- diff classification
- provider payload creation
- persistence rules
- destructive sync decisions
- class-selection business rules
- unresolved-item export decisions

Code-behind may handle only UI composition concerns such as:

- calling `InitializeComponent`
- view-only focus management
- transient visual state that is purely presentational

## 9. Project-by-Project Intent

### `CQEPC.TimetableSync.Domain`

Keep the domain model small, explicit, and stable. Prefer immutable records and value objects where possible.

### `CQEPC.TimetableSync.Application`

Define use-case inputs, outputs, and ports first. Delay orchestration complexity until parser behavior and normalization rules are proven with fixtures.

### `CQEPC.TimetableSync.Infrastructure`

Implement integrations incrementally: parsers first, persistence second, providers last. Avoid introducing abstractions before a real integration requires them.

### `CQEPC.TimetableSync.Presentation.Wpf`

Start with a minimal shell and page-level view models. Build the Home preview and Import / Diff surface on top of concrete occurrences, not raw parser output.
