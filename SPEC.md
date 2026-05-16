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
- a real CQEPC timetable PDF parser for regular timetable blocks, class discovery, same-template layout analysis, warnings, unresolved practical-course summaries, and tagged-metadata alias normalization so variants such as `鏁欏鐝?` and `鏁欏鐝粍鎴?` resolve to the same structured field
- a real CQEPC teaching-progress XLS parser with diagnostics and first-week override fallback
- a real CQEPC class-time DOCX parser with range-based period-time profiles and structured noon-window notes
- a real normalization engine that expands week expressions, resolves exact local datetimes, preserves unresolved items, and derives lossless recurring export groups
- a preview orchestrator that parses available sources, builds normalized occurrences, optionally generates provider-aware task candidates, and compares them against the latest accepted local snapshot
- locale-invariant sync identity generation, logical diff keys, and week-expression expansion
- timetable-PDF source fingerprints must be block-local rather than whole-file-local: re-exporting one PDF for the same class must keep unchanged course blocks on the same fingerprint whenever their parsed CQEPC block content and layout anchor are unchanged
- local sync identity and local snapshot diff matching must stay stable across source-fingerprint drift and small metadata corrections, so a revised PDF for the same class is reconciled as exact/update work instead of a synthetic full delete+add batch
- persisted workspace preferences for week-start choice, timetable-resolution settings, program network proxy mode, provider defaults, provider auth settings, selected destinations, task rules, one default Google Calendar color, and per-course time-zone/color overrides
- persisted per-course time-zone overrides must preserve the occurrence's own wall-clock date/time in Home, Import, and editor flows; those views must not round-trip a course occurrence through the machine-local time zone before saving
- Google desktop OAuth for a Windows local app using a user-selected installed-app JSON and system-browser loopback flow
- Microsoft provider/auth scaffolding in Infrastructure, but the current desktop workflow still ships Google as the only supported sync target
- explicit UTF-8-safe local JSON persistence and safe loading of provider auth inputs
- Google writable-calendar discovery in Settings plus Google Calendar create, update, and delete for app-managed events
- Google Calendar preview/read-back must request and honor remote event time-zone metadata and `colorId` so Home rendering, diff classification, and apply reconciliation stay aligned with the parsed timetable
- Google Calendar app-managed ownership and destructive sync identity must be trusted only from provider-safe private extended properties or local Google mappings, never from ordinary event description text
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
- Microsoft writable-calendar / task-list discovery and apply flows remain planned product work instead of a released desktop capability
- DPAPI-protected local Google token storage and a separate Google sync-mapping store for remote IDs and fingerprints
- persisted Google connection summaries and selected calendars must never be treated as proof of a live connection by themselves; startup, Home sync, and Home apply must re-check the DPAPI token store and clear stale Google state before deciding whether the user is still connected
- DPAPI-protected local Microsoft token storage and a separate Microsoft sync-mapping store stay reserved for the planned Microsoft rollout and are not yet part of the supported desktop flow
- a preview-first apply flow where Import only saves the accepted local snapshot baseline and refreshes Home preview, while provider writes remain exclusive to the Home primary apply action
- Import diff presentation groups must remain projection-only. The raw Added/Updated/Deleted identities stay authoritative for selection and apply, even when the UI adds unchanged child occurrence rows for context or regroups the changes by course and repeat rule.
- Import diff summaries must surface meaningful hidden drift such as provider color-only changes or metadata/source-only updates so the user can see why a row is classified as Added, Updated, or Deleted even when title/time/location look identical at first glance.
- Import detail editing is scoped by the selected layer: course groups expose all parsed repeat rules directly from the currently uploaded timetable sources, repeat-rule groups expose aggregate edits for all child occurrences, concrete occurrence rows expose occurrence-only edits, pinned unresolved rows open the same right-detail inline editor for confirmation, and course `i` opens inline per-course time-zone/color settings with save/reset controls. Course-level detail must not reuse provider/diff group state or label parsed repeat rules as Added, Updated, Deleted, or Unchanged. Sparse week expressions such as `3-9,11-20` must stay split into separate continuous repeat-rule segments in Import details, and rule/occurrence edit forms must stay collapsed until the user invokes the detail-panel Edit action. Unchanged Schedules repeat-rule clicks select the aggregate repeat-rule detail and must not switch the left list into the all-times single-occurrence view. Rule-level editing must infer weekly or biweekly rules from sparse same-weekday occurrence sets even when holiday/skipped weeks create larger 7-day or 14-day multiples.
- Import time-profile fallback confirmations and unresolved time-profile course blocks must be pinned ahead of regular course diff groups, course time edits must use a masked `HH:mm` input with an immutable colon separator, unresolved edit defaults must use parsed metadata plus any matching week/date and time-profile evidence, and editor time-zone seeding must use the current Google default time zone when parsed occurrences do not carry an explicit provider zone.
- Import and Home course-editor time-zone controls must use the same localized regional IANA display text, Common-category ordering, and themed dropdown hover/selection chrome as the Program Settings regional time-zone selector, while allowing narrower popup widths and category columns in side-panel or small-window editor layouts. Common must contain only recent selections followed by the fixed popular IANA list, not every available regional zone.
- Import note differences use the final Google Calendar description text as a single-column red/green line diff. For unusually large legacy descriptions, the UI may keep a few unchanged context lines and collapse the changed middle block so review stays responsive. Metadata already embedded in that description, such as class, campus, teacher, teaching class, and course type, must not be duplicated as separate field-diff rows.
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
- The theme control lives in the inline Program Settings section on the Settings page.
- Settings secondary navigation is a shell-level column beside the primary sidebar, not a card inside the Settings content area.
- The Settings secondary navigation column must be visible only while Settings is the active page.
- Each primary and Settings secondary navigation option must expose a clear shell-owned vector icon, preserve localized automation names, and visibly indicate the selected page or Settings section; the Sync option icon should switch in real time to a colored Google or Microsoft mark based on the default provider.
- Page and Settings-section changes should use subtle opacity/position transitions every time the active page or section changes. Settings-section transitions must replay on every selected-section change, not only the first time a section is opened.
- Clicking primary or secondary sidebar options should play a smooth eased icon feedback animation on the relevant vector parts using scale, translate, opacity, or rotation rather than a simple linear spin; light/dark theme changes should repaint the shell immediately while limiting visible transition motion to the theme button.
- The shell theme-transition overlay must remain transparent during interactive theme changes so page content is not covered by sun, horizon, or color-wash effects.
- DatePicker popups must use app theme resources for their text, arrows, month/year buttons, active dates, inactive dates, and selected text so opened calendars remain readable in both themes.
- The Settings secondary navigation column must visually match the primary sidebar style so it reads as an expanded Settings sub-sidebar while still remaining an independent column.
- Entering Settings from another shell page must collapse the primary sidebar while remembering its previous state. Leaving Settings must restore the primary sidebar only if it was expanded before entering Settings.
- The primary sidebar must show a bottom provider-data refresh affordance only outside Settings. It must follow the selected default provider, use the same colored Google/Microsoft mark as the Settings secondary `Sync` item, stack the mark and complete session-scoped last provider-data refresh time in expanded mode, keep only the centered provider mark in collapsed mode, and hide entirely while Settings is active. Provider refresh and startup Google sync must show a smooth Google-colored spinner around the mark and hide the timestamp until the provider work completes.
- Clicking the bottom provider mark/row must refresh the selected provider's data: Google should reuse the existing calendar-preview sync path, while Microsoft should refresh connection/destination data and then rebuild preview state. Missing OAuth JSON/client configuration, stale login state, provider failures, and timeouts should route the user to Settings > Sync and show a dismissible `Unable to connect` tip with the concrete reason.
- Settings secondary navigation labels should be short enough to fit in English and Chinese without clipping; the timetable-resolution secondary label is shortened to `Timetable` / `课表`, and the Settings shell/content layout must adapt to smaller window widths by reducing the secondary rail width and stacking fields before text is cut off.
- The Settings page content should not repeat a top-level `Settings` title or explanatory subtitle above the selected section.
- The selected theme is persisted in workspace preferences.
- Startup applies the persisted theme before the shell is shown.
- Interactive startup should show the shell immediately after theme and culture are applied; heavier workspace initialization may continue after first paint so the window does not stay hidden behind preview loading.
- Closing the visible shell during interactive startup must cancel deferred initialization, give pending preference/cache persistence only a short bounded flush window, and then exit the process so Debug output binaries are not held open after the window disappears.
- While interactive startup initialization is still running, the shell should surface a bottom-right task center before Home preview is ready so the user can inspect which startup steps are still in progress.
- The first startup Home preview must be allowed to complete from local sources alone. When startup cloud loading is enabled, a Google-backed preview may run in parallel with the local preview; if it finishes first, Home may adopt the merged result directly, otherwise Home should show the local result first and then adopt the merged provider result.
- Startup task messaging must keep those phases separate: the local parse/render task should not masquerade as Google sync work, and the Google refresh should clearly indicate that it is reading remote calendar state and merging it into the Home board.
- Program Settings must expose switches for startup cloud loading, Home render caching, and restoring the last Home render on startup. Home render caching and startup restore are opt-in, any persisted render cache must be protected at rest with user-local DPAPI, and stale plaintext cache files from older builds must be removed. Restored Home renders are display hints only; the normal local/provider preview must still refresh and replace stale cached content.
- Changing the selected theme must refresh the visible shell and page chrome immediately without requiring an app restart.
- Theme switching applies immediately without restart.
- Theme switching must repaint the active page immediately without requiring navigation to another shell section.
- Theme switching must repaint through runtime `DynamicResource` brush references so shared styles, page cards, overlays, combo boxes, and settings panels all change together.
- The native Windows title bar must also repaint with theme-aligned colors so the minimize/maximize/close strip does not fall back to an unrelated system accent in either light or dark mode.
- Themed vertical scroll bars must preserve a readable minimum thumb height so very long pages do not render the rounded thumb as visually clipped.
- Settings combo boxes must open when the user clicks anywhere on the combo surface, not only the arrow glyph.
- Settings combo-box selected values and dropdown item text should be centered.
- Time-zone dropdowns are the exception to centered generic combo text: regional IANA options should remain left-aligned and use the Program Settings time-zone selection theme so long city/country labels stay readable.
- Settings text and password inputs must use theme resources, show the I-beam cursor on hover, and keep placeholder overlays offset so an empty focused input places the caret before the hint instead of on top of it.
- Settings timetable-resolution controls must keep the default time-profile mode selector compact and place it beside the explicit profile selector when width allows, while stacking cleanly on narrow windows.
- Program Settings should show `Week Start`, `Language`, regional IANA time zone, startup Google sync, status notifications, startup cloud loading, opt-in Home render caching, opt-in restore-last-Home-render, numeric status-tip duration, theme, and About inline in the selected Settings section.
- Program Settings should split those controls into two content cards: display/language/time-zone settings and program behavior.
- Startup Google sync and status notifications should use long switch controls, not checkbox glyphs.
- Theme and About should render as compact centered actions below the Program Settings cards. The About entry point should be a circular `i` icon button.
- The theme action should use centered vector sun/moon icons with a smooth animated transition so dark-mode moon alignment does not depend on a font glyph.
- The sidebar collapse/expand arrow should remain centered when the sidebar is collapsed.
- The task-center chip should remain visually compact when collapsed and show only the count of running tasks.
- Expanding the task center should show concrete task details only, without redundant generic status copy.
- Home month cards and selected-day agenda cards must keep readable foreground contrast in dark mode.
- Import unchanged-schedule course group titles and details must keep readable foreground contrast in dark mode.
- Course-editor date inputs and their picker surfaces must keep readable foreground contrast in dark mode.
- Opened DatePicker calendar popups must inherit application theme brushes rather than system light brushes; month navigation arrows, weekday labels, and day buttons must keep readable dark-mode foreground contrast, and UI regression coverage should verify day-button foreground/background contrast in dark mode.
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

- `鈽卄 => theory
- `鈽哷 => lab
- `鈼哷 => practical
- `鈻燻 => computer
- `銆嘸 => extracurricular

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
Changing any source-file slot through browse, replace, remove, or drag-and-drop must rebuild the workspace preview immediately so parsed classes, time profiles, Home, and Import reflect the latest files.
The original school files do not need to live inside the repo.
App-local copy storage may be added later, but it is not part of the current onboarding UI or workflow.

## 6. Provider Selection Flow

Provider selection is explicit and provider-aware.

Expected flow:

1. User chooses a default provider in Settings; when a provider is connected, the default-provider option text includes the connected account summary, for example `Google: student@example.com`.
2. If Google is selected, the user picks a desktop OAuth client JSON, connects the account through the installed-app loopback flow, refreshes writable calendars, and selects one Google calendar.
3. If Microsoft is selected, the user enters a public client application ID, optionally enters a tenant ID, chooses whether to prefer WAM, connects the account, refreshes writable calendars and owned task lists, and selects one Outlook calendar plus one Microsoft To Do list.
4. If task rules are enabled, the provider-specific task destination is shown explicitly.
5. Google Tasks use the default `@default` task list in v1 rather than custom task-list discovery.
6. The app stores provider-specific defaults, destination IDs, connection summaries, and task rules separately.
7. Google calendar descriptors stored in preferences include display color metadata when Google returns it, so provider defaults and course editors can render the selected calendar's preset color accurately.
8. When entering the Import / Diff page, the chosen provider is shown clearly in the summary area.
9. The user can change the provider before previewing or applying sync.
10. The diff shown to the user reflects the selected provider only.

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
- when the selected day has no occurrences, switch the summary to `No schedule | Week` and leave the agenda list empty instead of showing an extra placeholder card
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

The current implementation uses an editor-style shell with a dedicated month workspace centered on the local computer date, honors Sunday/Monday week-start preference, keeps the Home board on a fixed-ratio scaled workspace, constrains normal-window resizing through native aspect-ratio sizing rather than `SizeChanged` bounce-back, shows selected-day occurrence details in a dedicated independently scrolling agenda pane beside the scheduling board, keeps the month header compressed to one title/context row plus one action row, lets the left month board scroll independently so the current month gets larger cells before the trailing overflow week appears, uses compact two-line colored-outline month-cell lessons with time above and course below, groups the month board by week so each horizontal row scales to that week's busiest visible day instead of using one fixed card height for the whole month, applies card minimum heights plus content-sized row growth so the last visible lesson card is not clipped while the busiest day also avoids excessive blank space below its final entry, switches the right-side agenda cards to a stacked compact template in narrow windows so the time block does not leave a large unused area beneath it, and leaves the right-side agenda list visually empty on no-schedule days while the summary strip switches to `No schedule | Week`.
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
The Import page must adapt explicitly across window sizes: compact, medium, and expanded widths all keep the primary split review/details layout, with the change list on the left and selected-occurrence details on the right.
The step strip (`Select changes -> Preview -> Apply`) must remain visible in fullscreen layout, while medium widths use the localized `Change preview` heading and compact widths hide the step strip; selecting any course / repeat-rule / occurrence row must not be blocked by expander chrome or nested header hit targets.
The expanded metric row must keep its count and percentage denominators explicit: Added / Updated / Deleted / Conflict / Unchanged percentages all use the same review total, defined as effective planned changes plus pinned conflicts/unresolved items plus current parsed occurrences that are not part of any effective planned change. Deleted-only plans must not subtract a missing old occurrence from the current unchanged count or produce percentages above 100%.
Compact and relatively small Import widths must downshift early enough that toolbar chips, filters, and action buttons stay proportional to the viewport; `Select current page` must actively toggle the currently visible diff rows instead of acting as a passive indicator, the header sync action must read `Sync Current Calendar` and invoke the current-calendar refresh flow, and course / repeat-rule cards must expand from the whole card body without rendering duplicate chevrons. Course `i` settings must open inline in the right detail panel, and any remaining legacy course-presentation overlay backdrop must remain visually transparent on hover so pointer movement outside it cannot tint the full background.
Course and repeat-rule expander hover/selection visuals must use the same rounded muted highlight language as occurrence rows and must not let WPF default control highlights bleed past card corners.
Compact widths must hide the step strip and remove non-essential provider/context labels from the top summary card so action controls remain tappable in the minimum supported window size.
Time-profile fallback confirmation cards and unresolved regular-course cards must render at the top of the left review list before normal Added/Updated/Deleted course groups. They remain explicit user confirmation items rather than hidden parser warnings. Clicking an unresolved row opens the right-side inline course editor; saving a valid confirmation removes that item from the pinned unresolved area and lets the generated occurrence appear in the regular change/review flow.
When a pinned unresolved regular-course block matches a local-snapshot delete by source fingerprint, or by the same class plus raw schedule shape (course title, weekday, period range, and week expression), Import treats that delete as a placeholder difference waiting for user confirmation. It is excluded from effective planned changes, delete counts, selectable apply IDs, resettable import drift, and the lower change list. Home preview must still retain the previous snapshot occurrence while the unresolved item is pending, so hiding the delete in Import cannot make the course disappear before confirmation.
User-visible Import labels, fallbacks, filter options, and XAML headings must come from localization resource dictionaries through `UiText`; only parser/source metadata tokens are allowed to stay in parser lexicons or comparison helpers. This prevents mojibake from entering presentation code and keeps English and Chinese resource keys aligned. Import workflow filters, grouping, and sorting must not branch on localized display text; the UI may show localized strings, but ViewModel logic must use stable semantic state such as indexes or enum-like values.

### Required Diff Groups

- Unresolved
- Unchanged Schedules
- Changes

The `Changes` surface must merge added, updated, and deleted schedule diffs by course title instead of scattering them across separate repeated section layouts.
Each course group should summarize the change mix compactly, show repeat-logic summaries at the group level, and expand first into repeat-rule groups before exposing concrete occurrence rows.
Repeat-rule groups should preserve the raw Added / Updated / Deleted diff identity for apply logic, while the UI consumes a derived grouped presentation model.
The right detail panel must not be limited to one concrete occurrence. Expanding a course group should select course-level detail and show all repeat logic parsed from the current uploaded source timetable, independent of Google Calendar remote state, saved local snapshot state, and the left-side diff projection. This level lists repeat rules only, not every concrete date inside each rule, and it must not show Added / Updated / Deleted / Unchanged badges or otherwise imply provider apply status. Expanding or clicking a repeat-rule group should select aggregate repeat-rule detail even when that group is already expanded, then let the left side list every concrete occurrence row for that rule. In Unchanged Schedules, clicking a repeat-rule card selects that rule's aggregate information and must not jump to the all-times view or select an arbitrary concrete occurrence. The Unchanged Schedules `Repeat Rules` / `All Times` mode controls are mutually exclusive and must always keep one mode selected. Selecting one concrete unchanged occurrence from the right-side repeat-rule detail should switch the left Unchanged Schedules section into the all-times view, rebuild and remeasure the same left review pane, scroll that pane to the matching row, and mark that same occurrence selected without moving an outer page scroll area; compact/small-window layouts must reserve enough internal space for the left pane scrollbar so this all-times view does not clip the thumb at the column edge. Rows backed by raw Added/Updated/Deleted changes remain selectable; unchanged rows are visible context and open occurrence detail but do not participate in apply selection. If a course group mixes selectable change rules with unchanged context rules, the course-level selection state must count only selectable rules so unchanged context cannot force a false partial state or block the checked state; clicking an indeterminate group selector must clear the selectable changes instead of leaving the group stuck checked. Selecting an occurrence should select concrete occurrence detail.
The course-group `i` action must switch the right detail panel to inline course settings for that course instead of opening a modal, even when the previous right-detail selection belonged to another course/rule/occurrence. Inline settings must include independently changeable time zone and provider color values, show Save only when there are unsaved edits, show Reset only when a saved override exists, and persist those overrides through workspace preferences.
Import inline editor text boxes should stay compact at one-line height and grow only when their content wraps. The top-level Reset Override action should be visible and executable whenever there is any saved course customization, enabled optional task-generation rule, resettable local-snapshot drift, or unsaved editor/course-settings change, not only while the exact detail page containing that change is active.
Reset Override restores import defaults locally. It must clear course schedule overrides, course presentation overrides, and provider task-generation rules back to their default disabled state. It may locally accept resettable local-snapshot drift rows such as stale update/delete entries from an older baseline or test data, but it must not silently accept normal Added rows from the current parse because those represent real new courses that still need review.
Import inline editor start/end time fields should use a time-specific `HH:mm` mask. The colon separator is not user-deletable; compact hour-only input such as `6` is valid and normalizes to `06:00`. Enter from the start-time field moves to the end-time field; Enter from the end-time field completes that field.
Import inline editor repeat-mode controls must visibly distinguish the selected `one-time`, `weekly`, or `biweekly` shortcut. Recurring edits also support an explicit every-N `day/week/month/year` unit; weekly rules support multi-select weekdays and biweekly is represented as every 2 weeks. Weekly every-N rules are anchored from each selected weekday's first occurrence on or after the start date, so a selected weekday earlier than the start date's weekday is not skipped from the first valid cycle. Non-weekly rules must hide weekday toggles. Monthly rules must expose a second selector for either the start-date day of month or the last selected weekday in each month.
Schedule-detail edits should use the selected layer as their scope: course-level edits affect the parsed course/repeat source, repeat-rule edits affect every occurrence represented by that repeat rule, and occurrence edits affect only the selected concrete occurrence. If an occurrence edit changes its repeat kind from one-time to any recurring rule, the save is promoted to a rule-level override so the recurrence can be rebuilt instead of being forced back to one-time. Single-occurrence edits show one date only. Repeating edits show start and end dates, offer an explicit date-swap action, and auto-swap inverted start/end dates during save. Changing interval/unit/monthly pattern/weekday selections must immediately update the editor summary and occurrence count, persist into timetable-resolution preferences, rebuild the local preview, and then let export grouping write Google Calendar either as an exact weekly RRULE when lossless or as exact single events otherwise. When adding, deleting, or cancelling deletion of one concrete occurrence changes the shape of its parent recurrence, the saved result must rebuild the repeat-rule summary and occurrence list from the new source of truth.
Deleted occurrence details should not expose the generic edit form first. Their right-panel action is Cancel Delete; it creates a one-time local schedule override marked as a retained deleted occurrence, but retained overrides are still eligible only when their class/source/date binding exists in the current normalized timetable. A retained flag must never revive an override from an old source file, another selected class, or any other inactive source context.

When a saved course-schedule override generates an occurrence that duplicates an existing occurrence with the same class, course title, local date/time, target kind, and location, the preview must merge that duplicate before building export groups or Import change groups. If different course titles share the same class, local date, and local time range, the Import page must pin those items as schedule conflicts above normal change groups. Pinned schedule conflicts and unresolved items are counted in the purple Conflict metric.
If a concrete Added or Deleted occurrence belongs to a repeat rule that is still present in the parsed timetable and that rule still has other concrete occurrences, the repeat-rule group may render as an Updated rule while the child occurrence keeps its raw Added or Deleted status. If the repeat rule contains only that one occurrence and the occurrence is deleted, the rule must render as Deleted, not Updated. The accepted apply identity must still be the raw planned change id; the orange rule grouping is presentation-only and anticipates a lossless recurring-provider update.
Expanded delete/add rows should stay compact and omit verbose metadata dumps. Expanded update rows should show matched `Before` / `After` structures with the same compact field set.
Expanded update rows should also expose a dedicated changed-field list plus a separate unchanged-detail section so users can see the change focus before reading the full before/after values.
Added and deleted occurrences must collapse to one populated detail block instead of showing an empty `Before` or `After` counterpart.
Occurrence-level update details must include behaviorally meaningful differences such as course title, time, location, time zone, provider target, and provider color. Metadata already present in the Google Calendar description, such as class, campus, teacher, teaching-class composition, and course type, should not be repeated as standalone before/after fields; those values remain visible inside the Google note/description payload.
Selecting an occurrence must always populate a visible detail surface with status badges plus `Before`, `After`, and shared-detail sections; all supported window sizes must keep the primary left-change/right-detail layout, while compact mode hides nonessential workflow chrome instead of moving the detail surface below the list. Left review cards should not show unchanged badges or remote-source badges such as managed remote event; those sources can remain in detailed fields where they explain a comparison.
Course-group and repeat-rule headers should remain left-aligned and readable in dark theme, with selected and expanded states using high-contrast text/background combinations.
Managed-note metadata shown in Import may arrive either as newline-delimited fields or slash-delimited fields; the presentation layer must split both forms back into stable `Teacher`, `Teaching Class`, and `Notes` values before rendering `Changed items`, `Shared details`, and `Before / After`.
When parsed local metadata keeps the note tail only as slash-delimited tagged segments such as class size, assessment mode, hour composition, or credits, Import must still render that tail as the `After` notes value instead of collapsing it to `No notes`.
Unchanged occurrence details must not show a note-difference block, because the before/after provider payload is intentionally identical. They should expose the current notes through the normal editable `Notes` field so a user can still create a single-occurrence notes override.
Import change-summary badges must be driven by structured changed-field labels only. Context strings such as pending-location labels are allowed in the row title/detail context, but they must not be parsed as a location delta unless the structured localized `Location` comparison actually changed. Added and deleted occurrence summaries should remain status-only except for explicit conflict indicators.
Import note display must normalize legacy CQEPC slash tails before comparison, including shorthand assessment and hour/credit forms such as assessment mode, theory-hours, and trailing credit numbers, so `Before / After` note rows use consistent labeled formatting.
Occurrence note differences must show the complete Google Calendar description text as a single-column line-based red/green diff, using code-review style removed/added rows rather than a side-by-side before/after notes table. Structured note parsing may continue to support badges and fallback fields, but the provider-note diff must preserve the final description context instead of presenting only isolated note fragments. Very large legacy descriptions may be bounded by keeping unchanged context and summarizing the changed block rather than allocating an unbounded LCS matrix. User-editable Google note payload lines should be edited inline inside that diff; deleted rows and app-managed metadata rows such as `managedBy`, `localSyncId`, `sourceFingerprint`, and `sourceKind` remain read-only. Added and deleted rows do not show a note diff because there is no comparable opposite side.
If notes are the only changed payload, the right-side group/detail summary should say the notes changed without listing every parsed note subfield as an independent update.
Default Google time-zone values should render as `Not present`; only an explicit per-course or per-occurrence time-zone override should surface the selected IANA region plus its date-specific UTC offset confirmation in Import.

Auxiliary sections such as time-profile fallback confirmations may follow after the primary groups.

Courses that do not require cloud-calendar changes must expose direct local editing.
The Unchanged Schedules section must remain available when regular add/delete/update groups are empty, but it must not replace or push away the primary split review/details layout when regular changes exist.
When regular changes exist, unchanged local courses should still be visible after the change list as the trailing unchanged-schedules section. That trailing section is course-scoped and should list only courses that are not already represented by the changed-course groups; unchanged repeat rules for a changed course stay in that changed course group as context.
When regular changes exist, the course-presentation `i` action remains available from the change-course header.

It must support both:

- a grouped repeat-rule view for editing one repeat pattern at a time
- an all-times view that lists every concrete parsed occurrence under each course heading
When multiple valid parsed schedules share the same course name, Import should group them under one course header and show one editable row per independently editable schedule series.
Selecting one of those unchanged-schedule course rows should open the same local editor used by Home so the user can change name, date span, time range, location, notes, and repeat logic before apply.
Editable unchanged-schedule course and unresolved-course rows should use card-click interaction directly; no extra trailing `Edit` badge is required when the entire row already opens the editor.

Unresolved items must appear ahead of the regular diff groups when they require manual confirmation for export timing. Opening an unresolved item in the right-side editor must allow Save even when the seeded default fields have not changed, because Save is the explicit confirmation that converts the unresolved source fingerprint into concrete occurrences.
When multiple unresolved items share the same course name, Import should group them under one course header and list each distinct time/source line clearly.
Selecting one of those unresolved time lines should open a local editor that can confirm the course by changing name, date, time, location, notes, and repeat logic.

Deleted and Added items must group by course title, with one course header per title and one time row per occurrence/time series.

Diff pairing must prefer stable source identity over mutable display fields.
If a parser fix changes a title, location, teacher, or other editable metadata while the underlying source block is still the same, the item should classify as `Updated` rather than a synthetic `Deleted` plus `Added` pair.
If the source fingerprint changes but class, date, period/time shape, title, location, or nearby metadata still prove that the old and new rows represent the same occurrence, local snapshot diff should still pair them one-to-one before considering Added/Deleted fallbacks. Previous-snapshot deduplication must include source identity, so distinct historical rows that share the same visible schedule shape but carry different source fingerprints remain independently deletable when one source row disappears.
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
`Import.ApplySelected` always updates only the accepted local snapshot baseline plus the Home preview; it must not write Google Calendar, Google Tasks, or any other provider directly. Provider writes belong to the Home primary apply action after the user has already accepted the local Import review. A later preview must still repair any accepted occurrence that no longer has either a saved Google mapping or a matching managed Google event.
Import Reset Override is also local-only: it may clear import defaults and resettable local snapshot drift, but it must not write Google Calendar, Google Tasks, or any other provider directly.
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
  - selected from the shell-level Settings secondary navigation
  - rendered inline on the Settings page, not as a separate overlay
  - split into two cards: display/language/time-zone/network settings and program behavior
  - includes week-start selection for Monday or Sunday
  - includes a selector for `Follow System`, `Simplified Chinese (zh-CN)`, and `English`
  - persists `null` for `Follow System` and applies language changes immediately without restart
  - includes a regional IANA time-zone selector that defaults to `Asia/Shanghai`
  - the regional IANA time-zone selector must drive both preferred write time zone and remote read fallback time zone, with UTC offsets shown only as confirmation hints
  - the time-zone selector must use a theme-aware popup in light and dark modes; search placeholder text must be visually subdued compared with real input
  - entering a time-zone search query must search the complete selector catalog instead of only the currently selected category
  - time-zone search must match localized display names, English/IANA names, country/region names and codes, and UTC/GMT offsets
  - time-zone candidates should display localized city or country/region names according to the active UI language where the app has data for them
  - the `Common` time-zone category must list only built-in popular regions plus persisted recent user selections; recent selections sort before popular regions, newest first
  - popular time-zone display names and search terms must be localized for supported UI languages while preserving the stable IANA id for provider payloads and shared editor models
  - Google Calendar writes must send the selected time zone explicitly in the Google API payload
  - for Google calendar previews, parsed calendar occurrences without an explicit course or single-occurrence time-zone override must inherit the current regional IANA default before Home rendering, Import diffing, recurrence grouping, and provider apply payload construction
  - Google Calendar reads must preserve returned `start.timeZone`, `end.timeZone`, and `originalStartTime.timeZone` metadata through remote-event diffing, and must also honor app-managed `timeZoneId` metadata when Google omits expanded recurring-instance `start.timeZone` and `end.timeZone`; if the declared zone, UTC instant, and displayed local wall time are unchanged, the event is exact, not a repeated metadata-only update. A truly missing Google time-zone declaration with unchanged UTC instant and wall time may be metadata-only rather than a normal update, while an equivalent regional IANA id difference such as `Asia/Shanghai` versus `Asia/Hong_Kong` is not surfaced as an Import change
  - user-requested time-zone changes in local course settings remain diff-visible by IANA id when they change behavior, but provider remote-preview reconciliation must not turn equivalent Google regional metadata drift into visible updates
  - includes a persisted device-level network proxy selector that defaults to the Windows system proxy and does not travel with timetable source files or snapshots
  - network proxy selector options are `System Proxy`, `Direct`, and `Custom Proxy`; `System Proxy` uses .NET `HttpClient.DefaultProxy`, `Direct` explicitly disables proxy use, and `Custom Proxy` accepts an HTTP proxy URI such as `http://127.0.0.1:7890`
  - Custom proxy supports optional username/password, `Bypass local`, and a bypass list that defaults to `localhost`, `127.0.0.1`, and `::1` so Google OAuth loopback callbacks never go through the custom proxy; proxy passwords are stored outside JSON using user-local DPAPI protection, and unreadable or corrupted password blobs are treated as no saved password so startup can continue and the user can re-enter credentials
  - the custom proxy UI must stay theme-matched in both light and dark modes, and the bypass list editor must make the one-host/domain/network-per-line format explicit
  - Google and Microsoft provider HTTP clients must consume the selected proxy mode for remote provider traffic only; local PDF/XLS/DOCX parsing and local JSON/snapshot storage must never route through the app proxy
  - Program Settings exposes a network `Test Connection` action that distinguishes configuration errors, proxy reachability failures, Google API reachability failures, and authentication/permission failures
  - includes a persisted startup Google-sync switch
  - includes a persisted status-notification switch for the bottom-right running-task chip
  - includes a persisted `Light` / `Dark` appearance toggle with immediate runtime apply
  - keeps the animated circular vector sun/moon theme control below the cards in a centered action row
  - includes an About entry point below the cards in the same centered action row
- Provider Defaults
  - default provider
  - Google desktop OAuth JSON selection, explicit stream-based load for the JSON file, connect/disconnect actions, and writable-calendar refresh
  - Microsoft public-client configuration, connect/disconnect actions, and writable calendar/task-list refresh
  - provider-specific destination calendar selection
  - Microsoft To Do task-list selection
  - Google Tasks default-list summary for the current Google v1 flow
  - one provider default event color selector that mirrors the Google Calendar app event-color preset list and applies to all courses unless a course-level override is saved
  - the `Preset color` option must display the selected Google calendar's own preset color, not a hardcoded swatch
  - Google writable-calendar refresh must preserve calendar display color metadata from `CalendarListEntry.backgroundColor` or resolve `CalendarListEntry.colorId` through the Google Calendar `Colors.calendar` palette; missing color metadata may trigger a refresh through the existing startup/Home sync calendar-load path, but it must not add a second independent startup refresh
  - no separate course-type color-mapping card in Settings
- Sync Behavior
  - preview required before sync
  - deletion confirmation
- Task Rules
  - provider-aware rule-based task generation settings
  - default task-generation rules are disabled, and Reset Override must restore enabled provider rules to those disabled defaults
- About
  - launched from the Program Settings action row rather than a standalone Settings card
- shows the current release stage as `Pre-Alpha`
  - states clearly that Google Calendar is currently available while Microsoft targets remain planned

## 11. About Overlay

The About surface is an overlay opened from Program Settings, not a heavy separate page.

It should include:

- app name
- version
- short purpose statement
- local-first and preview-first philosophy
- current sync availability and planned targets
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
- map profile course types from source labels into domain types, including theory labels -> `Theory`, experiment/training/practical/computer-room labels -> `PracticalTraining`, and sports title or location matches -> `SportsVenue`
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
- automation should also be able to request an app-rendered screenshot of a named automation element such as the whole shell window when the test needs both navigation rails instead of just a page root
- smoke coverage should verify that closing the automation main window exits the WPF process and releases build output file locks
- the desktop smoke layer should continue covering shell launch, page navigation, primary actions, and other stable entry points such as sidebar toggles or Settings-level actions
