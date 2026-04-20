# SPEC

## 1. Product Summary

CQEPC Timetable Sync is a local-first Windows desktop application that imports three school-exported source files and turns them into a reviewable sync workflow:

- timetable PDF for regular class blocks
- teaching progress XLS for semester week-to-date mapping
- class-time DOCX for period-time profiles

The app targets `.NET 8`, `WPF`, and `MVVM`.

The first implementation goal is not a full feature-complete sync client. It is a correct architecture and a clear product shape for the parser, normalization, preview, diff, and later provider-specific sync layers.

## 2. Core Principles

- Local-first: source parsing, normalization, diffing, and confirmation happen on the local machine.
- Preview-first: the user sees a local preview and a sync diff before any destructive remote action.
- Provider-aware: Google and Microsoft remain separate adapters with separate storage and sync rules.
- Culture-stable and UTF-8-safe: stable IDs, diff keys, week parsing, and persisted text handling must not depend on the current locale or system-default encoding.
- No silent guessing: unresolved practical-course summary items and other ambiguous data stay unresolved.
- Concrete-first normalization: schedule data is expanded into exact dated occurrences before any export grouping.

## 3. Current Implementation Scope

The current codebase defines:

- the solution structure and project boundaries
- a usable WPF shell with Home, Import, Settings, and About overlay surfaces
- persisted light/dark theme support for shell and workspace surfaces
- startup-safe resource-dictionary localization with persisted language preference, `Follow System` / `zh-CN` / `en-US` support, and live language switching
- the initial source, normalization, and sync concepts
- structured onboarding state (`SourceAttentionReason`, ordered `CatalogActivityEntry`) and structured workspace preview/apply status models
- a real CQEPC timetable PDF parser for regular timetable blocks, class discovery, same-template layout analysis, warnings, and unresolved practical-course summaries
- a real CQEPC teaching-progress XLS parser with diagnostics and first-week override fallback
- a real CQEPC class-time DOCX parser with range-based period-time profiles and structured noon-window notes
- a real normalization engine that expands week expressions, resolves exact local datetimes, preserves unresolved items, and derives lossless recurring export groups
- a preview orchestrator that parses available sources, builds normalized occurrences, optionally generates provider-aware task candidates, and compares them against the latest accepted local snapshot
- locale-invariant sync identity generation, logical diff keys, and week-expression expansion
- timetable-PDF source fingerprints must be block-local rather than whole-file-local: re-exporting one PDF for the same class must keep unchanged course blocks on the same fingerprint whenever their parsed CQEPC block content and layout anchor are unchanged
- local sync identity and local snapshot diff matching must stay stable across source-fingerprint drift and small metadata corrections, so a revised PDF for the same class is reconciled as exact/update work instead of a synthetic full delete+add batch
- persisted workspace preferences for week-start choice, timetable-resolution settings, provider defaults, provider auth settings, selected destinations, task rules, one default Google Calendar color, and per-course time-zone/color overrides
- persisted per-course time-zone overrides must preserve the occurrence's own wall-clock date/time in Home, Import, and editor flows; those views must not round-trip a course occurrence through the machine-local time zone before saving
- Google desktop OAuth for a Windows local app using a user-selected installed-app JSON and system-browser loopback flow
- Microsoft desktop auth for a Windows local app using a public-client MSAL flow with WAM preferred and browser fallback
- explicit UTF-8-safe local JSON persistence and safe loading of provider auth inputs
- Google writable-calendar discovery in Settings plus Google Calendar create, update, and delete for app-managed events
- Google Calendar preview/read-back must request and honor remote event time-zone metadata and `colorId` so Home rendering, diff classification, and apply reconciliation stay aligned with the parsed timetable
- When Google Home preview re-associates a managed recurring instance with a saved local mapping, it must preserve the remote event payload fields, including Google Calendar color id, so diff classification does not manufacture a follow-up update after a successful apply
- Google recurring export must preserve the exact set of accepted occurrences. Series writes must not depend on a lossy `COUNT`-only weekly rule when the local export group contains skipped slots; the write payload must represent the full intended schedule shape, and the apply path must not report success while later expected Google instances are missing
- Home preview can optionally import the selected Google calendar into the board so existing remote events are visible before apply, with added timetable items in green, delete candidates in red with strikethrough, unrelated Google items in orange, and exact same-time managed matches kept on a neutral existing-item surface
- A previously accepted local snapshot does not prove that Google still contains the corresponding managed event. If an occurrence has neither a saved Google mapping nor a matching managed Google event during preview, Home must surface it again as an Added calendar change so the missing remote write can be repaired
- If a local-snapshot deletion already matches a managed Google event in the preview window, that delete must carry the concrete remote event into apply. Diff generation must not consume the managed remote event so early that Home later removes it only from the local baseline and leaves the Google event behind.
- If a managed Google event matches title/time/location but its app-managed metadata (`LocalSyncId`, `sourceFingerprint`, recurring-instance identity) has drifted, preview must surface it as an Update so apply refreshes the metadata instead of treating it as a safe exact match.
- That metadata-rebind path must be class-aware: a managed Google event for the same class and the same payload shape can be refreshed in place, but a different class that only happens to share title/time/location must not suppress the new class add.
- If the saved Google mapping points to a stale remote item id but preview already finds another managed Google event for the same class and the same payload shape, preview must reuse that visible managed event instead of emitting a second Add for the same lesson.
- Google preview and apply must scope local calendar-event mappings to the currently selected Google calendar destination. A mapping saved for calendar `A` must not suppress adds, manufacture updates, or steer delete/update writes while the user is previewing or applying against calendar `B`.
- Older app-managed Google events may lack persisted class metadata entirely. In that legacy case, preview may still reuse the managed remote event when the full timed payload matches exactly, and apply must then backfill the missing metadata so future previews can return to strict class-aware matching.
- After a Google apply, if preview still shows app-managed duplicate events with the same title/time/location payload and the current normalized timetable only needs one of them, the convergence pass must automatically apply the represented remote delete change for the extra duplicate instead of leaving two identical lessons visible.
- optional Google Tasks create, update, and delete for explicit rule-based items on the default Google task list
- Microsoft writable-calendar and owned-task-list discovery in Settings plus Outlook Calendar create, update, and delete for app-managed events
- optional Microsoft To Do create, update, and delete for explicit rule-based items, with linked-resource creation when paired Outlook events are available
- DPAPI-protected local Google token storage and a separate Google sync-mapping store for remote IDs and fingerprints
- persisted Google connection summaries and selected calendars must never be treated as proof of a live connection by themselves; startup, Home sync, and Home apply must re-check the DPAPI token store and clear stale Google state before deciding whether the user is still connected
- DPAPI-protected local Microsoft token storage and a separate Microsoft sync-mapping store for remote IDs and fingerprints
- a preview-first apply flow where Import only saves the accepted local snapshot baseline and refreshes Home preview, while provider writes remain exclusive to the Home primary apply action
- Import diff presentation groups must remain projection-only. The raw Added/Updated/Deleted identities stay authoritative for selection and apply, even when the UI hides unchanged child rows or regroups the changes by course and repeat rule.
- Import diff summaries must surface meaningful hidden drift such as provider color-only changes or metadata/source-only updates so the user can see why a row is classified as Added, Updated, or Deleted even when title/time/location look identical at first glance.
- presentation-owned localization of parser warnings, diagnostics, and unresolved-item copy by stable code, with fallback to stored parser text and preserved raw source content
- the product behavior expected from the first real implementations

The current codebase does not yet include:

- live remote drift detection before preview creation
- Google task-list discovery beyond the default task list

## 3.1 Localization and Language Behavior

- Settings exposes exactly three UI-language options:
  - `Follow System`
  - `Simplified Chinese (zh-CN)`
  - `English`
- The persisted value is `null` for `Follow System` and an explicit culture name for the other two options.
- Startup resolves the effective culture before first paint using this fallback chain:
  - explicit preferred culture
  - system culture or parent-culture match
  - `en-US`
- Changing the language in Settings applies immediately and refreshes both XAML-owned `DynamicResource` labels and computed view-model text without restart.
- Parser warnings, parser diagnostics, and unresolved-item summaries/reasons are localized in Presentation by stable code first, then fall back to the stored parser message or unresolved text.
- `RawSourceText` remains exactly as parsed and is never localized.
- Localization dictionaries and touched text assets must remain UTF-8.

## 3.2 Appearance and Theme Behavior

- Settings exposes explicit `Light` and `Dark` theme options.
- The theme control lives inside a `Program Settings` overlay opened from the end of Settings.
- The Settings page should expose that entry as a compact trailing button, not as a full-width inline settings card.
- The selected theme is persisted in workspace preferences.
- Startup applies the persisted theme before the shell is shown.
- Interactive startup should show the shell immediately after theme and culture are applied; heavier workspace initialization may continue after first paint so the window does not stay hidden behind preview loading.
- While interactive startup initialization is still running, the shell should surface a bottom-right task center before Home preview is ready so the user can inspect which startup steps are still in progress.
- The first startup Home preview must be allowed to complete from local sources alone. Provider-backed Google Home preview import should run as a follow-up refresh instead of blocking the initial timetable parse/render step.
- Startup task messaging must keep those phases separate: the local parse/render task should not masquerade as Google sync work, and the follow-up Google refresh should clearly indicate that it is reading remote calendar state and merging it into the already-visible Home board.
- Changing the selected theme must refresh the visible shell and page chrome immediately without requiring an app restart.
- Theme switching applies immediately without restart.
- Theme switching must repaint the active page immediately without requiring navigation to another shell section.
- Theme switching must repaint through runtime `DynamicResource` brush references so shared styles, page cards, overlays, combo boxes, and settings panels all change together.
- Settings combo boxes must open when the user clicks anywhere on the combo surface, not only the arrow glyph.
- Settings should expose a single `Program Settings` button near the end of the page. Opening it should show `Week Start`, `Language`, `Startup Google Sync`, `Status Notifications`, `Theme`, and `About` in one lightweight overlay instead of keeping those controls inline on the main Settings page.
- The `About` action should visually replace the `Program Settings` overlay rather than nesting a second dialog inside it.
- The Google default UTC-time selector inside Program Settings should stay as a compact selector card without extra explanatory body text.
- Theme and About should render as compact trailing actions at the bottom of Program Settings instead of large cards. The About entry point should be a circular `i` icon button.
- The task-center chip should remain visually compact when collapsed and show only the count of running tasks.
- Expanding the task center should show concrete task details only, without redundant generic status copy.
- Home month cards and selected-day agenda cards must keep readable foreground contrast in dark mode.
- Import parsed-course group titles and details must keep readable foreground contrast in dark mode.
- Course-editor date inputs and their picker surfaces must keep readable foreground contrast in dark mode.
- Settings combinations that change language or time-profile resolution should bind by stable persisted values so selection is not lost when preview refresh rebuilds the option objects.
- The language selector must bind by a stable persisted culture key so runtime language rebuilds cannot introduce duplicate options and switching to English or Chinese applies immediately.

## 4. Source File Rules

### 4.1 Timetable PDF

The timetable PDF is the source of truth for regular course blocks.

The parser must capture, when available:

- class name
- course title
- course type marker
- weekday
- period range
- raw week expression
- campus
- location
- teacher
- teaching class composition
- additional metadata text

Practical-course summary blocks at the bottom of a timetable page must be ignored by the PDF parser. They must not be auto-exported, and they must not appear as unresolved items.
Regular timetable parsing should stay template-local and cell-local to avoid cross-column text bleed, and skipped grid cells should emit specific diagnostics rather than disappearing silently.
Successful top-of-page carryover stitching should stay internal and should not create user-visible noise diagnostics.

Successful cross-page carryover stitching must stay conservative: metadata-only tails and obvious split-title continuations may attach to the previous weekday cell, but a top-of-page block that already forms a standalone course must be treated as a new course rather than swallowed by the previous page residue.
If extractor block-building accidentally merges a top-of-page metadata tail and the next standalone course into one weekday block, the parser must split that merged block before carryover resolution.
If the CQEPC template spills a title-only fragment into the header page's footer strip, that fragment must still participate in carryover matching with the next page's top-of-page metadata tail instead of being dropped outside the last grid band.

The current parser maps CQEPC course markers as:

- `★` => theory
- `☆` => lab
- `◆` => practical
- `■` => computer
- `〇` => extracurricular

### 4.2 Teaching Progress XLS

The teaching progress spreadsheet is used only for semester week-to-date mapping.

It must provide:

- semester week number
- week start date
- week end date
- optional semester-span sanity checks
- fallback recomputation from a manual first-week start override when workbook dates are incomplete or ambiguous

It must not be treated as the source of truth for regular weekly classes.
Trailing arrangement columns and row symbols may help identify the week grid, but they must not define exported week-date semantics.
When the workbook yields a valid week-1 mapping, Settings should auto-populate the effective first-week start from the XLS result without turning that value into a manual override.

### 4.3 Class-Time DOCX

The class-time document is used only for period-time profile parsing.

It must provide named profiles that can vary by:

- campus
- course type
- period range
- start time
- end time

The app must not assume a single universal period table.
The parser must preserve the noon-window note for periods `5-6` as structured metadata so the UI can visually de-emphasize that slot later.
Settings must support automatic default profile selection, an explicit default profile, and per-course overrides scoped by class name plus exact course title.
Automatic profile selection should prefer same-campus profiles that match the inferred course type, but when that type-specific profile does not define the requested period range it may fall back to another same-campus profile that does define the exact periods.
Those same-campus fallback resolutions must still be surfaced as explicit confirmation items in Import, placed ahead of the regular diff sections, and left unchecked until the user confirms them.

## 5. File Import Flow

The file import flow belongs in Settings or a clearly labeled import entry point.

Expected flow:

1. User drags the timetable PDF, teaching progress XLS, and class-time DOCX into one unified source-files area or selects them with a file picker.
2. The app validates supported file extensions exactly as `.pdf`, `.xls`, and `.docx`, auto-detects each file type, and routes each file to the correct required slot.
3. The app stores user-local source references, keeps `SourceStorageMode.ReferencePath` as the active behavior, and remembers the last used folder in local configuration.
4. The app persists machine-readable onboarding state for each file, including import/parse state, `SourceAttentionReason`, and ordered `CatalogActivityEntry` values.
5. The app shows selected file name, import status, parse-state labels, and presentation-formatted detail text for each required file, plus an overall required-files summary.
6. Missing, moved, or deleted local files never block startup; they are shown as attention-needed instead.
7. The app parses each file locally after the relevant parser is available.
8. The app surfaces parse warnings and diagnostics immediately after parsing runs.
9. If the PDF contains multiple classes, the user selects one target class.
10. The app resolves week-date mapping and time-profile selection inputs.
    Same-campus automatic time-profile fallbacks are allowed only when the fallback defines the exact requested period range, and they must remain visibly marked for user confirmation.
11. The app normalizes the result into concrete occurrences.
    Before sync diffing, locally stored course-schedule overrides are applied so manual confirmations and edits are reflected in both Home and Import.
12. The app stores a local snapshot and refreshes the Home page preview.
13. The app prepares an Import / Diff view before any sync action.

Locally stored course-schedule overrides and Google preview items must be displayed and edited using their own resolved wall-clock date/time.
The app must not derive Home/Import/editor display time from `DateTimeOffset.LocalDateTime`, because doing so can shift lessons onto the wrong date when a per-course calendar time zone differs from the machine-local zone.

The user must be able to replace any one source file without redoing unrelated settings.
The user must also be able to remove any one detected source file from the unified source-files area.
The original school files do not need to live inside the repo.
App-local copy storage may be added later, but it is not part of the current onboarding UI or workflow.

## 6. Provider Selection Flow

Provider selection is explicit and provider-aware.

Expected flow:

1. User chooses a default provider in Settings.
2. If Google is selected, the user picks a desktop OAuth client JSON, connects the account through the installed-app loopback flow, refreshes writable calendars, and selects one Google calendar.
3. If Microsoft is selected, the user enters a public client application ID, optionally enters a tenant ID, chooses whether to prefer WAM, connects the account, refreshes writable calendars and owned task lists, and selects one Outlook calendar plus one Microsoft To Do list.
4. If task rules are enabled, the provider-specific task destination is shown explicitly.
5. Google Tasks use the default `@default` task list in v1 rather than custom task-list discovery.
6. The app stores provider-specific defaults, destination IDs, connection summaries, and task rules separately.
7. When entering the Import / Diff page, the chosen provider is shown clearly in the summary area.
8. The user can change the provider before previewing or applying sync.
9. The diff shown to the user reflects the selected provider only.

The app must not flatten Google and Microsoft into a fake identical abstraction at the UX level.

## 7. Class Selector Behavior

The class selector is driven by the timetable PDF result.

- If exactly one class is present, show a static class label.
- If multiple classes are present, show a dropdown and require an explicit class selection.
- The selected class applies to normalization, Home page preview, and Import / Diff output.
- The current implementation keeps the parsed multi-class result in memory for the active session so the user can switch classes without re-importing the PDF.
- Separate persistence of the selected parsed class is deferred.

## 8. Home Page Calendar Preview

### Purpose

The Home page is the everyday local preview surface for normalized course occurrences.

When the default provider is Google and Home preview import is enabled, Home also merges the selected Google calendar into the board with provider-aware rules:

- same title + same time: show as already existing, do not generate a duplicate Add
- same title + same time + different managed Google color: classify as an Update rather than an exact match, and repair that color on apply
- same title + different time inside the semester window: show the remote item as a red delete candidate and the parsed timetable occurrence as the green add/update candidate
- remote items outside the semester delete window: show in orange for awareness only, but do not create delete actions
- the semester delete window comes from XLS first-week to last-week when available; otherwise it falls back to the parsed timetable occurrence range
- remote timed events must be interpreted in the remote event's own time zone first, not by the local machine offset

### Required Behavior

- show a calendar-style preview of concrete occurrences
- use a dense month grid with a clear selected-day agenda rather than a loose card mosaic
- render month-cell lessons as compact two-line entries with time above and course below, using colored outlines so compact windows still surface multiple visible lessons per day without collapsing into time-only single lines
- remove the separate top hero card so the calendar board is the first visible surface on Home
- keep the month workspace on a fixed design ratio that scales uniformly with the window so compact sizes preserve the same layout rather than turning the board into a taller scrolling stack
- let the left month board scroll independently so compact window sizes can prioritize the current month's usable cell height and push the trailing overflow week below the fold
- keep each day card visually square so the month board reads like a true calendar rather than a variable-height list
- default to the local computer date
- respect the week-start preference of Monday or Sunday
- show course title, time, and location clearly
- keep the selected-day summary compact and rectangular, focusing on occurrence count and school week instead of repeating the selected date text already visible in the calendar
- let the selected-day agenda pane scroll independently from the month board when many items are present
- show the selected-day agenda event color as a small dot using the effective configured event color instead of repeating the course-type label chip
- when the selected day has no occurrences, switch the summary to `No schedule / 无安排 | Week` and leave the agenda list empty instead of showing an extra placeholder card
- commit the Settings default Google Calendar color selector by stable color id so immediate preference refreshes cannot flip the visible choice to a neighboring palette entry
- show the selected class context clearly
- surface warnings and unresolved-item counts without mixing them into valid occurrences
- allow clicking a course in the selected-day agenda to edit its local course details, including name, date span, time range, and repeat cadence
- when Google is the selected provider, the Home sync action should refresh existing Google calendar events into the Home board without applying timetable changes
- when Google is the selected provider, let the Home primary action apply accepted changes directly to Google Calendar without first navigating to Import
- when Google is not connected, the Home sync action should navigate the user to Settings instead of failing silently
- format dynamic workspace/apply/diff/source-file status text in the presentation layer from structured state rather than persisting localized messages
- support an empty state when no valid snapshot exists

### Initial Bootstrap Expectation

The current implementation uses an editor-style shell with a dedicated month workspace centered on the local computer date, honors Sunday/Monday week-start preference, keeps the Home board on a fixed-ratio scaled workspace, constrains normal-window resizing through native aspect-ratio sizing rather than `SizeChanged` bounce-back, shows selected-day occurrence details in a dedicated independently scrolling agenda pane beside the scheduling board, keeps the month header compressed to one title/context row plus one action row, lets the left month board scroll independently so the current month gets larger cells before the trailing overflow week appears, uses compact two-line colored-outline month-cell lessons with time above and course below, groups the month board by week so each horizontal row scales to that week's busiest visible day instead of using one fixed card height for the whole month, applies card minimum heights plus content-sized row growth so the last visible lesson card is not clipped while the busiest day also avoids excessive blank space below its final entry, switches the right-side agenda cards to a stacked compact template in narrow windows so the time block does not leave a large unused area beneath it, and leaves the right-side agenda list visually empty on no-schedule days while the summary strip switches to `No schedule / 无安排 | Week`.
When an accepted update changes only non-layout provider-managed fields such as Google Calendar color, the Home selected-day agenda should render one orange update card rather than two visually identical before/after rows.
If preview also carries an extra managed-remote cleanup row for the same visible lesson slot, Home should still render only one final selected-day card for that title/time/location combination and prefer the update representation over the cleanup duplicate.

## 9. Import / Diff Page

### Purpose

The Import / Diff page is the required preview-first gate before any destructive sync.
Applying selected changes here updates only the accepted local snapshot and the Home preview. It must not write Google Calendar or other remote providers directly.

### Required Summary Area

- selected provider
- selected destination calendar/list
- selected class
- file import status
- parse warnings count
- unresolved-item count

The top summary/action area should stay compact: a single-row provider/context strip plus the apply/select/clear actions, without a repeated page title or verbose apply-status prose.
The selected destination summary in that strip should use a compact rectangular status treatment instead of a large pill so the action buttons remain visible in narrower windows.
Ready import changes should default to selected, except items that require explicit fallback confirmation.
After Import applies the current local selection, `Import.ApplySelected` should become disabled until another preview-driving option such as class or time-profile settings rebuilds the diff.
Grouped course changes and their child occurrence rows should use matching round selection indicators so the visible checked state is unambiguous.

### Required Diff Groups

- Unresolved
- Parsed Courses
- Changes

The `Changes` surface must merge added, updated, and deleted schedule diffs by course title instead of scattering them across separate repeated section layouts.
Each course group should summarize the change mix compactly, show repeat-logic summaries at the group level, and expand first into repeat-rule groups before exposing concrete occurrence rows.
Repeat-rule groups should preserve the raw Added / Updated / Deleted diff identity for apply logic, while the UI consumes a derived grouped presentation model.
Expanded delete/add rows should stay compact and omit verbose metadata dumps. Expanded update rows should show matched `Before` / `After` structures with the same compact field set.
Default Google time-zone values should render as `Not present / 不存在`; only an explicit per-course or per-occurrence time-zone override should surface a concrete `UTC±HH:mm` value in Import.

Auxiliary sections such as time-profile fallback confirmations may follow after the primary groups.

Parsed courses must expose direct local editing.
The Parsed Courses section must remain available even when regular add/delete/update groups are empty.

It must support both:

- a grouped repeat-rule view for editing one repeat pattern at a time
- an all-times view that lists every concrete parsed occurrence under each course heading
When multiple valid parsed schedules share the same course name, Import should group them under one course header and show one editable row per independently editable schedule series.
Selecting one of those parsed-course rows should open the same local editor used by Home so the user can change name, date span, time range, location, notes, and repeat logic before apply.
Editable parsed-course and unresolved-course rows should use card-click interaction directly; no extra trailing `Edit` badge is required when the entire row already opens the editor.

Unresolved items must appear ahead of the regular diff groups when they require manual confirmation for export timing.
When multiple unresolved items share the same course name, Import should group them under one course header and list each distinct time/source line clearly.
Selecting one of those unresolved time lines should open a local editor that can confirm the course by changing name, date, time, location, notes, and repeat logic.

Deleted and Added items must group by course title, with one course header per title and one time row per occurrence/time series.

Diff pairing must prefer stable source identity over mutable display fields.
If a parser fix changes a title, location, teacher, or other editable metadata while the underlying source block is still the same, the item should classify as `Updated` rather than a synthetic `Deleted` plus `Added` pair.
If the previously saved snapshot belongs to a different selected class than the current preview, that snapshot must not produce delete candidates for the current class review.
When the user applies accepted changes for one selected class, the saved snapshot baseline must replace only that class slice and keep other class slices out of the next review for the current class.
When Google preview resolves a managed remote event through app metadata or conflict matching but the stored Google mapping points at a stale remote item id, apply must repair the preview-resolved remote event and rebind the local mapping instead of writing to the stale id.
When the user switches from one parsed class to another, the preview must show Adds for the newly selected class and provider-managed Deletes for the previously managed class slice only where those remote events still fall inside the deletion window.
Same-title same-time suppression must not hide a newly parsed occurrence behind any Google event unless that remote item already matches the same managed occurrence identity.

### Visual Behavior Notes

- Added items use green emphasis.
- Updated items show before and after values in a clear comparison layout.
- Deleted items use red emphasis and strikethrough.
- Same-time managed Google matches use a synced green surface instead of dropping back to the neutral white card styling.
- Task-backed changes are labeled explicitly so date-level task items are not mistaken for exact timed calendar reminders.
- Parsed-course editing should use a lightweight Outlook-style editor layout with the editable form on the left and a compact live schedule summary on the right.
- Unresolved manual-confirmation cards stay visually separated, remain preview-local until saved, and must not be auto-applied.
- Unresolved items use warning styling and live in a separate section.
- The summary/action area should read like a review desk rather than a generic dashboard, with provider and preview scope visible before the change lists.

### Required Actions

- select all
- clear all
- inspect item details
- apply selected changes
- cancel and return

No destructive remote change may bypass this page or an equivalent confirmation surface.
`Apply` always updates the accepted local snapshot baseline. When the selected provider is configured, the accepted changes are also written to that provider's managed calendar and task surfaces. On Home, the Google sync action is responsible for refreshing existing remote events into the preview, while the Google apply action is responsible only for writing the accepted changes. A later preview must still repair any accepted occurrence that no longer has either a saved Google mapping or a matching managed Google event.
When Google already contains an exact managed match for a current occurrence, apply must also backfill the local snapshot and local Google mapping state for that exact match instead of leaving it unmanaged locally.
Preview must also repair missing local Google mappings for already-exact managed matches when the remote event can be bound confidently by managed metadata or recurring-instance identity (`parentRemoteItemId` + `originalStartTimeUtc`). A stale mapping file must not leave later Google repairs blocked until another write succeeds.
Preview must also normalize stale Google mapping collisions. If multiple local sync ids point at the same managed Google event or recurring instance, preview must keep one binding for that remote identity, prefer the currently parsed occurrence when it still exists, and drop the stale duplicate mapping before diff classification so Home can return to white after apply.
When the user switches the selected Google calendar and later switches back, Home must rebuild preview strictly from that selected calendar's remote events plus that calendar's scoped local mappings. A successful apply against another writable calendar must not leave orange `Updated` rows behind on the original calendar, and a no-op apply after switching back must finish promptly without repeated provider polling.
For Google Calendar, accepted calendar writes must execute in deterministic delete -> update -> add order so class switches and drift repairs remove stale managed events before creating replacement events, and calendar writes should run serially for reliability when large batches include overlapping recurring changes. Managed Google color is part of the update payload, so a selected change that only differs by `colorId` must still repair the existing managed event instead of being treated as unchanged or recreated. The apply path should also deduplicate repeated recurring-series deletes and reuse recurring-instance lookups per series so large batch repairs do not degrade into repeated list-instance scans. When multiple managed remote events resolve to the same course occurrence, preview must keep only the expected exact-match count and leave the extra managed copies represented as deletions rather than suppressing every duplicate as exact. If Google returns an incomplete recurring-series materialization after insert, the provider must roll that series back and fall back to exact single-event writes instead of leaving the user with a partially applied schedule.

## 10. Settings Page

### Purpose

The Settings page owns configuration, source-file import, and sync defaults.

### Required Sections

- Source Files
  - one unified drag-and-drop target area for PDF, XLS, and DOCX
  - bulk browse action via file picker
  - auto-detect and route PDF, XLS, and DOCX into their required slots
  - import, replace, remove PDF, XLS, and DOCX
  - show filename, last import time, import status, parse-status label, and presentation-formatted detail text
  - keep onboarding activities and file-attention reasons as structured state, not persisted display strings
  - show which required files are still missing
  - keep onboarding reference-first for now; no user-visible copy-mode toggle yet
- Timetable Resolution
  - effective first-week start with clear auto-derived vs manual state
  - clear-manual action that returns to the XLS-derived value when available
  - selected class
  - static class label when only one class is parsed
  - dropdown plus explicit selection when multiple classes are parsed
  - parser warning and diagnostic summaries
  - default time-profile mode (`Automatic` or `Specific Profile`)
  - explicit default time-profile selector when specific mode is active
  - per-course override list scoped to class + exact course title
- Program Settings
  - represented by one button near the end of Settings
  - opens as a lightweight overlay instead of a full page
  - includes week-start selection for Monday or Sunday
  - includes a selector for `Follow System`, `Simplified Chinese (zh-CN)`, and `English`
  - persists `null` for `Follow System` and applies language changes immediately without restart
  - includes a Google default UTC-time selector that defaults to `UTC+8`
  - the Google default UTC-time selector must drive both preferred write time zone and remote read fallback time zone
  - Google Calendar writes must send the selected time zone explicitly in the Google API payload
  - includes a persisted startup Google-sync toggle
  - includes a persisted status-notification toggle for the bottom-right running-task chip
  - includes a persisted `Light` / `Dark` appearance toggle with immediate runtime apply
  - keeps the animated circular sun/moon theme control inside the overlay
  - includes an About entry point inside the overlay
- Provider Defaults
  - default provider
  - Google desktop OAuth JSON selection, explicit stream-based load for the JSON file, connect/disconnect actions, and writable-calendar refresh
  - Microsoft public-client configuration, connect/disconnect actions, and writable calendar/task-list refresh
  - provider-specific destination calendar selection
  - Microsoft To Do task-list selection
  - Google Tasks default-list summary for the current Google v1 flow
  - one provider default event color selector that mirrors the Google Calendar app preset list and applies to all courses unless a course-level override is saved
  - no separate course-type color-mapping card in Settings
- Sync Behavior
  - preview required before sync
  - deletion confirmation
- Task Rules
  - provider-aware rule-based task generation settings
- About
  - launched from Program Settings rather than a standalone Settings card
  - shows the current release stage as `Pre-Alpha`

## 11. About Overlay

The About surface is an overlay opened from Program Settings, not a heavy separate page.

It should include:

- app name
- version
- short purpose statement
- local-first and preview-first philosophy
- supported provider families
- optional repository link

Behavior expectations:

- lightweight presentation
- subtle transition only
- easy close or dismiss action

## 12. Unresolved Item Handling

Unresolved practical-course summary items and other ambiguous source items must be modeled explicitly.

Rules:

- preserve the raw source text
- preserve the source class context
- preserve the reason the item is unresolved
- show them in a separate warning area
- do not merge them into valid occurrence previews
- do not export them automatically

Future versions may allow the user to resolve them manually, but only with explicit confirmation.

## 13. Rule-Based Task Generation Concept

Task generation is optional and always previewed.

The first version should support provider-aware rules such as:

- create a task for the first class in the morning
- create a task for the first class in the afternoon
- create tasks only for selected course titles
- apply provider-specific reminder or category defaults

Task generation must:

- be configured in Settings
- be shown in Import / Diff before apply
- remain distinct from event generation
- respect provider differences between Google Tasks and Microsoft To Do
- keep Google Calendar as the precise timed reminder target and Google Tasks as optional day-level follow-up items only

## 14. Normalization Rules

The normalization pipeline must follow this order:

1. parse raw source files
2. build normalized timetable blocks
3. resolve week expressions into explicit semester weeks
4. resolve period ranges into exact start and end times using the selected profile
5. produce concrete dated occurrences
6. derive recurring export groups only when the merge is lossless

Important constraints:

- never silently drop weeks
- never silently merge mismatched title, location, notes, or time variants
- keep unresolved items separate from valid occurrences
- keep enough structured metadata to explain why an occurrence exists
- stable IDs, logical diff keys, and week parsing must use locale-invariant formatting/parsing rules
- preserve parser-originated practical summaries and ambiguous items; normalization-only failures become `RegularCourseBlock` unresolved items instead of guessed occurrences
- assign stable parser and normalization codes so Presentation can localize warnings, diagnostics, and unresolved summaries/reasons by code with fallback text
- auto-select time profiles conservatively only after checking class-scoped per-course overrides and an explicit default profile
- when automatic selection falls back to another same-campus profile because the preferred profile family lacks the requested periods, keep the occurrences exportable but surface the fallback as a structured confirmation item instead of `NRM004`
- map profile course types as `理论 -> Theory`, `实验/实训/实践/上机 -> PracticalTraining`, and `体育/体育场地` title or location matches -> `SportsVenue`
- if a configured override points to a missing profile or missing periods, keep the course unresolved instead of silently falling back

## 15. Sync Rules

The sync pipeline must remain preview-first and provider-aware.

Before any write action:

1. compare the new normalized result against the previous local snapshot
2. compare against app-managed remote items for the selected provider later
3. classify changes as Added, Updated, Deleted, or Unresolved
4. let the user choose what to apply
5. apply only selected changes

Additional constraints:

- only modify items created and managed by this app
- store provider item IDs separately by provider
- store source fingerprints separately
- use provider-safe metadata storage for remote items
- never overwrite unrelated remote items
- never perform delete or update actions without explicit review

## 16. Initial Deliverables for the Real Implementation Phase

The remaining implementation phases after the current XLS work should produce:

1. parser logic for PDF based on sanitized fixtures rather than private raw files, plus continued DOCX regression coverage
2. wiring of parsed DOCX profiles into the import and normalization flow
3. local snapshot persistence for normalized occurrences, export groups, and unresolved items
4. Import / Diff classification
5. WPF Home page preview based on occurrences
6. provider adapters implemented separately for Google and Microsoft

## 17. Repository Hygiene for Source Materials

- Original school-exported files are local input materials, not required repository assets.
- Private raw files must not be required by tests, CI, or normal developer onboarding.
- If sanitized fixtures are needed later, they should be committed intentionally and documented as sanitized regression assets.
- Personal working folders inside the repo, if used at all, must live in gitignored paths such as `local-samples/`, `tests/Fixtures/Local/`, or `tests/Fixtures/SourceSamples/`.
- The repository should not keep tracked copies of private school exports, even as temporary bootstrap material.

## 18. Desktop UI Test Harness

The WPF desktop layer must support app-internal UI regression and smoke testing without depending on the app window taking foreground focus.

Required behavior:

- internal screenshot export should use app-side WPF rendering for page roots rather than OS desktop capture
- screenshot mode should prefer a render-only path that does not call `Show()` on the shell window when WPF allows it
- if a live presentation source is still required, the fallback window must stay non-topmost, `ShowActivated=false`, hidden from the taskbar, and configured as a no-activate background window
- FlaUI / UIA smoke automation should use the same background window strategy rather than a normal foreground launch; this is a live off-screen window model, not a headless mode
- background automation should prefer semantic UIA interactions such as `Invoke`, `SelectionItem`, `Toggle`, and `Value` rather than foreground mouse injection or coordinate clicks
- automation failure screenshots should prefer an app-side background-safe render of the active page root, with whole-window capture kept only as a fallback diagnostic path when page-root rendering is unavailable
- the desktop smoke layer should continue covering shell launch, page navigation, primary actions, and other stable entry points such as sidebar toggles or Settings-level actions
