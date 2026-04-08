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
- persisted workspace preferences for week-start choice, timetable-resolution settings, provider defaults, provider auth settings, selected destinations, task rules, and course-type category/color rules
- Google desktop OAuth for a Windows local app using a user-selected installed-app JSON and system-browser loopback flow
- Microsoft desktop auth for a Windows local app using a public-client MSAL flow with WAM preferred and browser fallback
- explicit UTF-8-safe local JSON persistence and safe loading of provider auth inputs
- Google writable-calendar discovery in Settings plus Google Calendar create, update, and delete for app-managed events
- Home preview can optionally import the selected Google calendar into the board so existing remote events are visible before apply, with added timetable items in green, delete candidates in red with strikethrough, unrelated Google items in orange, and exact same-time managed matches kept on a neutral existing-item surface
- optional Google Tasks create, update, and delete for explicit rule-based items on the default Google task list
- Microsoft writable-calendar and owned-task-list discovery in Settings plus Outlook Calendar create, update, and delete for app-managed events
- optional Microsoft To Do create, update, and delete for explicit rule-based items, with linked-resource creation when paired Outlook events are available
- DPAPI-protected local Google token storage and a separate Google sync-mapping store for remote IDs and fingerprints
- DPAPI-protected local Microsoft token storage and a separate Microsoft sync-mapping store for remote IDs and fingerprints
- a preview-first apply flow where Import always saves the accepted local snapshot baseline and, for configured providers, also executes the accepted remote writes
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
- The theme control lives at the end of Settings as a compact sun/moon toggle instead of being mixed into the calendar display selectors.
- The selected theme is persisted in workspace preferences.
- Startup applies the persisted theme before the shell is shown.
- Changing the selected theme must refresh the visible shell and page chrome immediately without requiring an app restart.
- Theme switching applies immediately without restart.
- Theme switching must repaint the active page immediately without requiring navigation to another shell section.
- Theme switching must repaint through runtime `DynamicResource` brush references so shared styles, page cards, overlays, combo boxes, and settings panels all change together.
- Settings combo boxes must open when the user clicks anywhere on the combo surface, not only the arrow glyph.
- The Calendar Display section should keep `Week Start` and `Language` as two half-width controls, place the theme toggle on its own centered row, and keep the About action centered at the bottom instead of nesting both actions inside a third appearance card.
- Settings combinations that change language or time-profile resolution should bind by stable persisted values so selection is not lost when preview refresh rebuilds the option objects.

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
- same title + different time inside the semester window: show the remote item as a red delete candidate and the parsed timetable occurrence as the green add/update candidate
- remote items outside the semester delete window: show in orange for awareness only, but do not create delete actions
- the semester delete window comes from XLS first-week to last-week when available; otherwise it falls back to the parsed timetable occurrence range

### Required Behavior

- show a calendar-style preview of concrete occurrences
- use a dense month grid with a clear selected-day agenda rather than a loose card mosaic
- remove the separate top hero card so the calendar board is the first visible surface on Home
- default to the local computer date
- respect the week-start preference of Monday or Sunday
- show course title, time, and location clearly
- show the selected class context clearly
- surface warnings and unresolved-item counts without mixing them into valid occurrences
- allow clicking a course in the selected-day agenda to edit its local course details, including name, date span, time range, and repeat cadence
- when Google is the selected provider, the Home sync action should refresh existing Google calendar events into the Home board without applying timetable changes
- when Google is the selected provider, let the Home primary action apply accepted changes directly to Google Calendar without first navigating to Import
- when Google is not connected, the Home sync action should navigate the user to Settings instead of failing silently
- format dynamic workspace/apply/diff/source-file status text in the presentation layer from structured state rather than persisting localized messages
- support an empty state when no valid snapshot exists

### Initial Bootstrap Expectation

The current implementation uses an editor-style shell with a dedicated month workspace centered on the local computer date, honors Sunday/Monday week-start preference, shows selected-day occurrence details in a dedicated agenda pane beside the scheduling board, and keeps the empty preview state to a single pending-preview title instead of duplicating that message as a second caption.

## 9. Import / Diff Page

### Purpose

The Import / Diff page is the required preview-first gate before any destructive sync.

### Required Summary Area

- selected provider
- selected destination calendar/list
- selected class
- file import status
- parse warnings count
- unresolved-item count

The top summary/action area should stay compact: a single-row provider/context strip plus the apply/select/clear actions, without a repeated page title or verbose apply-status prose.

### Required Diff Groups

- Unresolved
- Deleted
- Parsed Courses
- Added

Auxiliary sections such as `Updated` and time-profile fallback confirmations may follow after the primary groups.

Parsed courses must appear after Deleted and before Added, and expose direct local editing.
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
`Apply` always updates the accepted local snapshot baseline. When the selected provider is configured, the accepted changes are also written to that provider's managed calendar and task surfaces. On Home, the Google sync action is responsible for refreshing existing remote events into the preview, while the Google apply action is responsible only for writing the accepted changes.
For Google Calendar, accepted calendar writes must execute in delete -> update -> add order so class switches and drift repairs remove stale managed events before creating replacement events.

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
- Calendar Display
  - week starts on Monday or Sunday
- Language
  - selector for `Follow System`, `Simplified Chinese (zh-CN)`, and `English`
  - persisted preference with `null` representing `Follow System`
  - immediate runtime apply without restart
- Appearance
  - selector for `Light` or `Dark`
  - persisted preference
  - immediate runtime apply without restart
  - circular sun/moon control with animated state transition
  - compact about/info action beside the theme control rather than a separate full-width card
- Provider Defaults
  - default provider
  - Google desktop OAuth JSON selection, explicit stream-based load for the JSON file, connect/disconnect actions, and writable-calendar refresh
  - Microsoft public-client configuration, connect/disconnect actions, and writable calendar/task-list refresh
  - provider-specific destination calendar selection
  - Microsoft To Do task-list selection
  - Google Tasks default-list summary for the current Google v1 flow
  - provider-specific category or color defaults
- Sync Behavior
  - preview required before sync
  - deletion confirmation
- Task Rules
  - provider-aware rule-based task generation settings
- About
  - small button near the end of Settings

## 11. About Overlay

The About surface is an overlay opened from the end of Settings, not a heavy separate page.

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
