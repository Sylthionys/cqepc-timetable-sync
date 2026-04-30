# CQEPC Timetable Sync

CQEPC Timetable Sync is a local-first Windows desktop application that turns CQEPC timetable source files into a reviewable sync workflow for Google Calendar today, with Microsoft integration planned later.

The target stack is `.NET 8`, `WPF`, and `MVVM`. The repository now includes a usable desktop workflow with a three-part shell, a compact month Home workspace, an Import diff review surface, a grouped Settings control center for source files and defaults, a program-settings overlay for calendar display, Google Calendar default time zone, and appearance options, dark/light theme support, and an in-window About overlay.

## Philosophy

- Local-first: parsing, normalization, previewing, diffing, and confirmation happen locally.
- Preview-first: destructive changes never happen without a visible review step.
- Provider-aware: Google and Microsoft stay separate in architecture and behavior.
- No silent guessing: unresolved practical-course summary items stay unresolved until the user confirms a resolution path.

## Current Scope

The current implementation establishes:

- a solution structure aligned to `Domain`, `Application`, `Infrastructure`, and `Presentation`
- a styled WPF shell with Home, Import, Settings, a program-settings overlay, and an About overlay
- startup-safe WPF localization using UTF-8 resource dictionaries, a persisted language preference, and `Follow System` / `zh-CN` / `en-US` options
- persisted light/dark theme selection applied before the shell is shown
- a local-file onboarding layer for drag-and-drop import, manual file picking, and persisted user-local source references
- structured local-source onboarding state with per-file attention reasons and ordered activity entries, while WPF owns the user-visible wording
- application contracts for parsing, normalization, persistence, and provider adapters
- domain models for parsed blocks, normalized blocks, concrete occurrences, unresolved items, and sync diff concepts
- a real CQEPC timetable PDF parser for class discovery, same-template layout analysis, row-band-scoped regular course blocks, guarded cross-page carryover stitching that also captures footer-strip title fragments before the next page's metadata continuation, parser warnings/diagnostics, footer-summary exclusion so bottom-of-page practical notes do not surface as parsed or unresolved timetable items, and normalized tagged-metadata aliases such as treating both `鏁欏鐝粍鎴?` and `鏁欏鐝?` as the same structured teaching-class field
- a real CQEPC teaching-progress XLS parser with diagnostics, manual fallback override input, and auto-derived first-week mapping for preview/settings
- a real CQEPC class-time DOCX parser for range-based period-time profiles, structured course-type tags, and the noon-window note
- a real normalization engine that expands week expressions, resolves concrete occurrences, carries parser unresolved items forward, and produces lossless recurring export groups
- same-campus automatic time-profile fallbacks that keep valid occurrences exportable when the preferred profile lacks the requested periods, while surfacing those items as explicit confirmations in Import
- persisted workspace preferences for week-start choice, timetable-resolution settings, provider defaults, selected provider destinations, provider auth settings, task rules, one Google Calendar default event color, and per-course time-zone/color overrides
- timetable-resolution settings that separate manual vs XLS-derived first-week start, automatic vs explicit default time-profile mode, and per-course time-profile overrides
- persisted course-schedule overrides that let the user manually confirm unresolved timetable items or adjust a parsed course's name, date span, time, location, notes, repeat cadence (`every N day/week/month/year`, weekly multi-select weekdays, and monthly day-of-month / last-weekday patterns), time zone, and Google Calendar color before sync; when a whole repeat override and a single-occurrence override target the same source slot, the single occurrence remains authoritative for that date
- local course editing and diff rendering now keep a schedule occurrence's own wall-clock date/time instead of reinterpreting it through the machine-local time zone, so reopening a time-zone-overridden course no longer drifts the saved time on each edit
- a workspace preview orchestration layer that parses the three source files, normalizes occurrences, optionally generates rule-based task candidates, and builds a local diff against the latest accepted snapshot
- a provider execution seam that supports provider-specific destination discovery and preview-first apply orchestration
- Google desktop OAuth support for a local Windows app using a user-selected installed-app client JSON, a system-browser loopback flow, and DPAPI-protected local token storage
- Microsoft adapter/auth scaffolding exists in Infrastructure, but the current desktop workflow still exposes Google as the only implemented sync target
- locale-invariant sync IDs, diff keys, and week-expression expansion so preview/apply behavior does not drift with the current UI culture
- timetable PDF block fingerprints now hash the block's own normalized CQEPC content plus its page/anchor position instead of the whole-file hash, so re-exporting or lightly revising one PDF does not invalidate every unchanged lesson at once
- occurrence-level local sync ids and local snapshot diff matching now stay stable across source-fingerprint drift and small metadata edits, preventing revised PDFs for the same class from degenerating into whole-calendar delete+add batches when the actual schedule shape is still the same
- explicit UTF-8-safe text handling for persisted settings/snapshots/mappings and stream-based loading of provider auth inputs
- Google Calendar create, update, and delete support for app-managed timed events, including recurring-series creation and recurring-instance maintenance, with an explicit payload `timeZone` sourced from Program Settings
- Google Calendar recurring-series writes now emit exact weekly rules using `UNTIL` plus `EXDATE` for skipped slots instead of relying on a lossy `COUNT`, so sparse or drift-repaired course groups cannot silently drop later lessons when Google expands the series
- Google Calendar apply recovery can rebind stale local mappings to the managed remote event seen in preview before repairing update/delete drift, executes accepted Google changes in deterministic delete -> update -> add order, runs calendar writes serially, deduplicates repeated recurring-series deletes while still marking every accepted local child delete successful, clears saved child mappings when a recurring master is deleted, caches recurring-instance lookups per series, automatically recreates drifted recurring instances as exact single events when their timed range no longer matches the local occurrence, and falls back from a partially materialized recurring-series insert to per-occurrence single-event writes so a correct local parse is not left with missing Google events
- Google Calendar remote preview/read-back now requests remote event `timeZone` and `colorId` fields, resolves timed events against the event's own time zone before diffing or rendering Home, and treats Google Calendar color as part of the managed event payload so color-only drift becomes an Update instead of being skipped
- Google Home preview now preserves Google Calendar color metadata when recurring managed instances are re-aligned to saved local mappings, so a class that was already applied does not stay falsely highlighted as an orange pending update just because preview rebuilt the identity binding
- Home preview merges parsed timetable occurrences with Google Calendar remote-event recognition, keeps added timetable items green, delete candidates red with strikethrough, unrelated Google items orange, and only suppresses exact matches for Google items that are already app-managed for the same occurrence identity
- if an occurrence already exists in the accepted local snapshot but has neither a saved Google mapping nor a matching managed Google event, preview re-surfaces it as a `RemoteManaged` add so the next Home apply repairs the missing remote write instead of silently trusting the local snapshot
- local-snapshot deletes that already match a managed Google event now carry that exact remote event into apply, so switching classes cannot silently drop the delete from the provider write batch just because the stale local baseline and the remote event were matched too early during diff generation
- Google apply now also backfills local mappings and accepted local snapshot entries for already-exact managed matches, and refreshes managed Google metadata when `LocalSyncId` or `sourceFingerprint` drift even if title/time/location already match; this prevents the app from treating a correct parse as fully applied while later previews still lose control of those events
- Google preview now also persists backfilled local mappings for already-exact managed matches, including recurring instances resolved by `parentRemoteItemId + originalStartTimeUtc`, so a stale or missing `google-sync-mappings.json` entry no longer leaves some lessons unmanaged until a later successful write
- Google preview now also normalizes `google-sync-mappings.json` so one managed Google event or recurring instance cannot stay bound to multiple local sync ids after source-fingerprint drift or exact-match backfill. When that collision is detected, preview keeps the current occurrence binding, drops the stale duplicate mapping, and prevents Home from staying orange with repeated `RemoteManaged` updates after a successful apply
- Google preview/apply now scope calendar-event mappings to the currently selected Google calendar destination instead of treating `google-sync-mappings.json` as one flat cross-calendar pool; switching Home/Settings between writable Google calendars no longer reuses another calendar's mapping as a false orange update, and saving one calendar's refreshed mappings preserves the other calendars' bindings instead of overwriting them
- Google preview now reads and preserves the managed event `Class` metadata from Google payloads, and class-aware reconciliation will treat a same-class same-payload managed event with stale recurrence/color/metadata identifiers as an Update or exact match instead of a fresh Add; a different class with coincidentally identical title/time/location still remains a separate delete+add case
- when a saved Google mapping points at a stale remote event id but preview can already see another app-managed event for the same class and the same timed payload, reconciliation now rebinds to that preview-visible event instead of surfacing a second green Add
- Google preview also has a legacy-compatibility path for older app-managed Google events that never stored `Class` metadata: when a managed remote event has the exact same timed payload but no class marker at all, preview now rebinds it as the existing lesson instead of showing a duplicate green Add, and the next apply repairs that missing metadata on Google
- startup and Home Google actions now re-check the DPAPI token store before treating Google as connected; if `%LocalAppData%\\CQEPC Timetable Sync\\tokens\\google\\` no longer contains a usable token, stale saved account/calendar state is cleared and Home routes the user to Settings instead of silently refreshing on apply
- after a Google apply, the Home convergence pass now detects app-managed duplicate calendar events with the same title/time/location payload and automatically applies the represented remote delete rows for the extras, leaving only the expected occurrence count instead of making the user manually clean up one identical copy
- optional Google Tasks create, update, and delete support for explicit rule-based day-level follow-up items on the default Google task list
- Microsoft-specific destination discovery and sync operations remain planned product work rather than a released desktop capability
- provider-safe Google event metadata storage via `extendedProperties.private` plus a local Google sync-mapping store for remote IDs and source fingerprints
- each Google calendar mapping entry also remains destination-scoped by `DestinationId`, and preview/apply only reconcile the mappings that belong to the currently selected writable calendar
- provider-safe Microsoft metadata storage via Graph open extensions plus local Microsoft sync mappings for remote IDs, fingerprints, recurring-master IDs, and original-start timestamps
- a local snapshot diff/apply flow where Import keeps Parsed Courses as the no-diff fallback surface and as the trailing unchanged-course surface instead of letting it replace the primary review when Added/Updated/Deleted groups exist; keeps the compact `i` action available from both change-course headers and Parsed Courses headers as an inline right-detail settings view for per-course time-zone and Google Calendar color overrides; groups add/update/delete schedule changes into one course-based expandable `Changes` surface that now expands in two levels (`course -> repeat rule -> concrete occurrences`) so the first expansion stays focused on recurrence logic instead of dumping every date at once; course-level right-detail recurrence lists are rebuilt directly from the currently uploaded timetable parse and intentionally do not reuse calendar/diff group state or show Added/Updated/Deleted/Unchanged labels; second-level repeat-rule expansion shows the full concrete occurrence set for that rule, with unchanged children rendered as non-selectable context rows while raw Added/Updated/Deleted child identities remain authoritative for apply; lets the right detail panel switch between course-level parsed repeat rules, repeat-rule aggregate detail, concrete occurrence detail, inline course settings, and pinned unresolved-item confirmation; persists inline per-course time-zone/color overrides with Save/Reset controls that appear only when relevant; defaults ready changes to selected; renders add/update/delete rule cards with success/warning/danger emphasis and keeps delete text struck through; renders an added/deleted child occurrence inside an orange updated repeat-rule group only when the parsed recurrence still has other retained occurrences, while single-occurrence deleted rules remain red deletes; surfaces otherwise hard-to-see differences such as Google color drift or metadata/source-only drift directly in first-level diff summaries and second-level occurrence details; shows complete Google Calendar description text differences as a single-column red/green line diff instead of only isolated note fragments or a two-pane notes table; keeps class/campus/teacher/teaching-class/course-type metadata inside the Google notes payload rather than duplicating it as separate field changes; shows default Google time-zone values as `Not present` while explicit overrides render as concrete `UTC+/-HH:mm`; scopes accepted-snapshot replacement to the currently selected class so another class from an older baseline does not leak into delete candidates; and keeps `Import.ApplySelected` local-only so only the Home primary action can write Google Calendar changes
- a Home month workspace with Sunday/Monday week-start support, the calendar shown as the first surface without a separate hero card, selected-day agenda details, direct course-detail editing from the selected date, a Google Calendar preview toggle, a sync action that imports existing Google events into the Home board or routes to Settings when Google is not connected, and a primary Home apply action that writes the accepted changes directly without detouring through Import; the Home board now uses a fixed-ratio month workspace that scales uniformly with the window, keeps every day cell square, gives the left month board and right-side agenda their own independent vertical scroll regions, keeps the month header compressed to one title/context row plus one action row, renames the Google preview toggle/summary to `Current Calendar`, compresses the selected-day summary to a rectangular `count + week` strip so the calendar remains the dominant surface, renders month-cell lessons as compact two-line colored-outline entries with time above and course below, raises the visible day-cell preview count responsively from 3 to 5 as the calendar grows, groups the month board by week so each horizontal row scales to the busiest day in that week instead of forcing one month-wide fixed card height, uses per-day minimum heights plus content-sized rows so the busiest visible day no longer leaves a large blank tail or clips the final lesson card, smooths responsive 3/4/5 preview-limit switching with resize debouncing plus threshold hysteresis, and updates same-month day cells in place with interpolated height transitions instead of clearing and refilling the whole board when the preview bucket changes; the selected-day agenda also shows the event's configured calendar color as a dot instead of a course-type chip, uses a denser right-side card layout in smaller windows so time stays on one line and more detail remains visible without excessive scrolling, widens the right-side time rail so full minute ranges such as `08:30-10:00` remain readable in both normal and compact layouts, switches narrow agenda cards to a stacked compact template so the old fixed left time rail no longer leaves a large blank area under the time block, color-only Google updates collapse to one orange update card instead of rendering duplicate-looking before/after rows, the final Home agenda render deduplicates same-slot same-title same-location cards so a managed remote cleanup row cannot reintroduce a second visible copy of the same lesson, and a selected day with no schedule now keeps only the compact `No schedule | Week` summary strip instead of rendering an empty placeholder card in the agenda pane
- the Import summary bar now keeps provider/context, select/clear, and apply actions on one compact row, removes the extra apply-hint prose, disables `Import.ApplySelected` immediately after the current selection is adopted until preview-driving settings rebuild the diff, and uses matching round selection indicators for grouped course changes, repeat-rule groups, and per-occurrence rows so the control state stays visually unambiguous
- the Import review surface now has explicit compact / medium / expanded layouts: expanded widths keep the three-step progress strip and split review/details columns visible, medium widths restore the localized `Change preview` heading while keeping split review/details columns, compact widths keep the left-change/right-detail split but trim the provider/context header down to action controls only and hide the steps/stat cards, grouped expander headers no longer swallow checkbox clicks, added/deleted occurrences render a single localized `Detailed info` block instead of an empty comparison half, and real-storage UI coverage verifies that parsed-course fallback content cannot push the primary split review out of view when changes exist
- Import now pins time-profile fallback confirmation items and unresolved time-profile course blocks above the regular course diff groups, opens unresolved course rows into the right-side inline editor with parsed/defaulted schedule values, uses a masked `HH:mm` time editor with a non-deletable colon and Enter navigation from hours to minutes, highlights the selected repeat mode, exposes explicit every-N day/week/month/year repeat editing with weekdays only for weekly rules and a monthly day/last-weekday selector, promotes a single occurrence edit to a rule-level override when the user changes it to a recurring rule, resolves missing edit time zones through the current Google default (`UTC+8` / `Asia/Shanghai` by default) instead of the first zone-list entry, cancels stale local-preview rebuilds when save/reset actions are clicked in quick succession, and applies app theme brushes to opened DatePicker popups so dark-mode month arrows, weekday labels, and day text remain readable
- the Settings default Google Calendar color selector now commits by stable `colorId` rather than transient option-object identity, so choosing a palette entry such as Lavender/Light Blue no longer jumps to a neighboring color during immediate preference refresh
- interactive startup now shows the shell as soon as theme/localization are resolved, then finishes workspace preview initialization in the background so the window appears promptly even when source parsing or provider preview loading is slow
- the first startup Home preview is now local-first: it parses local sources and renders the initial Home board without waiting for Google calendar preview reads, then refreshes Google Home preview in a follow-up task so startup cannot get stuck behind remote calendar latency
- startup task-center messaging is intentionally split into two user-visible phases: `Building Home preview` means local file parsing plus first Home render, while `Startup Google event sync` means remote Google calendar reads plus Home merge refresh
- a bottom-right task center that appears only while work is running: the collapsed chip shows only the count of active tasks, and expanding it shows concrete startup/sync details such as loading remembered sources, building Home preview, and syncing existing Google Calendar events
- presentation-owned localization of parser warnings, diagnostics, and unresolved-item summaries/reasons by stable code, with fallback to stored parser text
- initial unit-test projects and fixture-driven parser coverage
- a FlaUI UIA3-based desktop smoke-test layer that launches the built WPF app against generated local fixture data and verifies the shell, navigation, Home, Import, and Settings surfaces
- focused background-safe live Google workflow tests that run against cloned actual local storage, select a concrete parsed class, and read Google events back to verify remote add, update, and delete behavior without mutating the user's primary local workspace
- first-pass implementation-oriented docs in `README.md`, `SPEC.md`, and `docs/architecture.md`

The current codebase does not yet establish:

- live remote drift detection before preview generation
- Google task-list discovery beyond the default `@default` list

## Local Source Files

- Source files are selected by the user at runtime. The app does not hardcode a repo path or assume a sample folder exists.
- The onboarding layer currently stores reference-only file metadata in user-local settings under `%LocalAppData%\CQEPC Timetable Sync\user-settings.json`.
- Settings uses one unified source-files area for drag-and-drop, bulk browse, per-slot replace/remove, and status display for PDF/XLS/DOCX.
- Local source state is persisted as data-first fields such as `SourceAttentionReason` and ordered `CatalogActivityEntry` values; presentation formats those into user-facing messages.
- Workspace preferences are stored under `%LocalAppData%\CQEPC Timetable Sync\workspace-preferences.json`.
- Google workspace preferences now expose one Program Settings control that drives both `PreferredCalendarTimeZoneId` for write payloads and `RemoteReadFallbackTimeZoneId` for remote read-back fallback. The default is `UTC+8` (`Asia/Shanghai`).
- The latest accepted local snapshot baseline is stored under `%LocalAppData%\CQEPC Timetable Sync\latest-snapshot.json`.
- Google sync mappings are stored under `%LocalAppData%\CQEPC Timetable Sync\google-sync-mappings.json`.
- Microsoft sync mappings and token storage paths are reserved for the planned Microsoft adapter rollout and are not part of the current supported user workflow.
- Google OAuth tokens are stored under `%LocalAppData%\CQEPC Timetable Sync\tokens\google\` using DPAPI-protected payload files.
- Persisted JSON and direct text writes use UTF-8 explicitly, and Chinese source metadata is preserved through parsing, local storage, UI rendering, and provider payload generation.
- Workspace preference writes are coalesced asynchronously during editing and flushed on shutdown so the latest language, theme, and timetable-resolution selections survive restart.
- Presentation localization assets live under `src/CQEPC.TimetableSync.Presentation.Wpf/Resources/Localization/` as UTF-8 `Strings.en-US.xaml` and `Strings.zh-CN.xaml`.
- The effective UI culture is resolved before the shell is shown using `preferred culture -> system culture or parent match -> en-US`.
- The app remembers the last used local folder for convenience and falls back to the user's Documents folder when that remembered folder is gone.
- Missing, moved, or deleted local files do not block startup. The app marks them as needing attention and waits for the user to replace them.
- During interactive startup, the task center should become visible before Home preview is ready so the user can inspect which initialization steps are still running.
- When the XLS parses successfully, the first-week start is auto-derived into timetable-resolution state; a manual override can still replace it until the user clears the manual value.
- `%LocalAppData%\CQEPC Timetable Sync\sources\` is reserved for a future app-managed copy mode, but the current implementation keeps `SourceStorageMode.ReferencePath` as the only active behavior.

## Localization

- Settings exposes exactly three UI-language options: `Follow System`, `Simplified Chinese (zh-CN)`, and `English`.
- Settings now moves `Week Start`, `Language`, `Google Calendar default UTC time`, `Startup Google Sync`, `Status Notifications`, `Theme`, and `About` behind a single compact `Program Settings` button placed at the end of Settings instead of keeping those controls as a large inline card.
- The Program Settings overlay now keeps the Google default UTC-time card minimal by removing the explanatory body text, and moves Theme plus About into a compact trailing action row. About is shown as a circular `i` icon button.
- Program Settings now also persists whether startup should auto-sync the Google calendar preview and whether the bottom-right running-task status notification chip is shown.
- Changing the appearance mode reapplies theme resources immediately and repaints the live shell without restarting the app.
- Theme switching now relies on runtime `DynamicResource` theme brushes across shared styles and page surfaces so dark mode repaints the full UI consistently instead of leaving mixed light cards behind.
- The native Windows title bar now follows the app theme in both light and dark modes instead of inheriting an unrelated system accent strip.
- Home month cells and selected-day agenda cards now force theme-aware text on top of their schedule surfaces so dark mode no longer leaves low-contrast course text.
- Import parsed-course rows now pin their title and detail text to theme brushes so dark mode does not fall back to black text inside parsed-course cards.
- Opening `About` from `Program Settings` now replaces that overlay visually instead of nesting one dialog inside another.
- Settings combo boxes use a full-surface click target instead of requiring the arrow glyph hit area.
- Timetable-resolution combo boxes that drive language or time-profile state now bind by stable persisted values, so selecting a mode/profile survives live preview refreshes instead of depending on transient item-object identity.
- The language combo box also binds by a stable culture key instead of transient item-object identity, which prevents duplicate entries after live rebuilds and keeps switching to English/Chinese effective immediately.
- XAML-owned labels use merged WPF resource dictionaries, while computed view-model text refreshes through the presentation localization and formatting layer when the language changes.
- The touched Home and Import surfaces now resolve their user-facing labels through those dedicated UTF-8 localization dictionaries instead of hardcoded mixed-language text.
- Runtime language switching now rebuilds the merged localization dictionaries before raising presentation refresh notifications, which keeps Settings/Home/Import labels in sync after switching to English or Chinese.
- The About overlay currently reports the release stage as `Pre-Alpha`.
- The About overlay now states the shipped sync status clearly: Google Calendar is available now, while Google Tasks / Outlook Calendar / Microsoft To Do remain planned.
- Parser warnings, parser diagnostics, and unresolved-item summaries/reasons are localized in Presentation by stable code first and fall back to stored parser text when no resource key exists.
- `RawSourceText`, saved destination names, and other user/provider-entered values are not localized or rewritten.
- `.editorconfig` requires UTF-8 for the text assets touched by this workflow.

## Source Control Hygiene

- Original school exports do not need to be committed and should normally stay outside the repo.
- If a developer wants a repo-local convenience folder for personal materials, it should live in a gitignored path such as `local-samples/`, `tests/Fixtures/Local/`, or `tests/Fixtures/SourceSamples/`.
- The repo should not carry tracked copies of private school exports.
- Parser and regression tests should not depend on private raw files. Later parser coverage should use explicitly sanitized fixtures committed in a parser-focused change instead.

## Non-Goals

Unless explicitly requested, the project does not currently target:

- school-system login, scraping, or browser automation
- OCR from screenshots
- automatic guessing of unresolved practical-course time slots
- background sync daemons in early versions
- a generic multi-school parser before CQEPC parsing rules are stable

## Solution Structure

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
  Fixtures/
docs/
  architecture.md
  parsers/
    timetable-pdf.md
    teaching-progress-xls.md
    class-time-docx.md
SPEC.md
README.md
```

`src/CQEPC.TimetableSync.Presentation.Wpf/` is the only desktop entry point in the current solution.

## Running Hybrid UI Tests

The desktop UI test strategy is split in two parts:

- internal WPF screenshot mode for deterministic visual regression artifacts
- FlaUI UIA3 smoke tests for launch, navigation, discoverability, and primary-action enablement

The app supports these internal screenshot flags:

- `--ui-test`
- `--ui-screenshot`
- `--ui-automation`
- `--page Home|Import|Settings`
- `--fixture sample`
- `--screenshot <output-path>`
- `--width <px>`
- `--height <px>`
- `--window-mode normal|background|render-only`

Generate deterministic screenshots directly from the app without foreground desktop capture:

```powershell
dotnet build src/CQEPC.TimetableSync.Presentation.Wpf/CQEPC.TimetableSync.Presentation.Wpf.csproj
.\src\CQEPC.TimetableSync.Presentation.Wpf\bin\Debug\net8.0-windows\CQEPC.TimetableSync.Presentation.Wpf.exe --ui-test --page Home --fixture sample --width 1380 --height 900 --screenshot .\artifacts\ui\home.png
.\src\CQEPC.TimetableSync.Presentation.Wpf\bin\Debug\net8.0-windows\CQEPC.TimetableSync.Presentation.Wpf.exe --ui-test --page Import --fixture sample --width 1380 --height 900 --screenshot .\artifacts\ui\import.png
.\src\CQEPC.TimetableSync.Presentation.Wpf\bin\Debug\net8.0-windows\CQEPC.TimetableSync.Presentation.Wpf.exe --ui-test --page Settings --fixture sample --width 1380 --height 900 --screenshot .\artifacts\ui\settings.png
```

Run the FlaUI smoke layer with:

```powershell
dotnet build tests/CQEPC.TimetableSync.Presentation.Wpf.UiTests/CQEPC.TimetableSync.Presentation.Wpf.UiTests.csproj
dotnet test tests/CQEPC.TimetableSync.Presentation.Wpf.UiTests/CQEPC.TimetableSync.Presentation.Wpf.UiTests.csproj --no-build
```

Build note:

- `dotnet build CQEPC.TimetableSync.sln` in `Debug` writes into `src/CQEPC.TimetableSync.Presentation.Wpf/bin/Debug/net8.0-windows/`. If a previously launched app instance from that folder is still running, Windows will lock the copied DLLs and the build can appear stuck on retries. Close the running `CQEPC.TimetableSync.Presentation.Wpf.exe` process first, or build `Release` while keeping the Debug app closed.

Or run the full workflow end to end:

```powershell
.\tools\run-ui-regression.ps1
```

Notes:

- `--ui-test` remains a backwards-compatible alias for `--ui-screenshot`.
- Internal screenshots render the requested page root to PNG inside the app process with WPF-native `RenderTargetBitmap`.
- Screenshot mode now tries `render-only` first so the app can export a page without calling `Show()` on the shell window. If WPF needs a live presentation source for a page, the app automatically falls back to a fixed-size off-screen `background` window that is `ShowActivated=false`, hidden from the taskbar, and marked `WS_EX_NOACTIVATE`.
- `--ui-automation` is reserved for FlaUI/UIA runs. It launches the shell in the same off-screen background mode so navigation and control discovery can be exercised without forcing the app to the foreground or keeping it topmost. This is still a real window, not a headless run.
- FlaUI interaction is intentionally semantic-first: tests drive controls through UIA patterns such as `Invoke`, `SelectionItem`, `Toggle`, and `Value` before any element-level fallback. The harness does not inject global mouse movement or steal foreground focus.
- Automation failure screenshots now prefer an app-side page render over desktop capture. The running WPF app exposes a local automation-only screenshot bridge so the test process can request a background-safe PNG of the current page root; FlaUI window capture remains only as a fallback when the page root is not available.
- FlaUI validates that the app launches, the shell appears, navigation works, Home/Import/Settings roots are discoverable, the sidebar can collapse safely, the Settings program-settings entry point can open in background mode, automation sessions can request app-rendered screenshots when the seeded sample workspace is ready, and the program-settings overlay can be captured in both light and dark themes after a real toggle interaction.
- Import smoke coverage now also verifies grouped change discovery, selected-occurrence detail payloads, occurrence-level selection toggles, and responsive Import page exports at `900x900`, `1380x900`, and `2048x1100`.
- Manual real-storage UI coverage also now opens the first selected-day Home course editor through the in-app automation bridge so dark-mode readability of the course editor and date-picker inputs can be checked without foreground interaction.
- The tests launch the built WPF executable from `src/CQEPC.TimetableSync.Presentation.Wpf/bin/<Configuration>/net8.0-windows/`.
- Each test run seeds an isolated local storage root through the `CQEPC_TIMETABLESYNC_STORAGE_ROOT` environment variable, so it does not depend on or overwrite a developer's normal `%LocalAppData%` data.
- The runner writes screenshots under `artifacts/ui/` and logs/test results under `artifacts/logs/`.
- FlaUI failure screenshots still land under `tmp/ui-test-screenshots/`, now with `page-render` vs `window-capture` naming so it is obvious whether the image came from app-side rendering or the fallback capture path.

Stable AutomationIds introduced for UI smoke coverage:

- `Shell.Root`
- `Shell.WorkspaceRoot`
- `Shell.Nav.Home`
- `Shell.Nav.Import`
- `Shell.Nav.Settings`
- `Home.PageRoot`
- `Home.CalendarSection`
- `Home.AgendaSection`
- `Home.Action.SyncCalendar`
- `Home.PrimaryAction.ApplySelected`
- `Import.PageRoot`
- `Import.HeaderSection`
- `Import.ActionBar`
- `Import.ApplySelected`
- `Import.ChangeGroups`
- `Import.DetailPanel`
- `Import.DetailPanelCompact`
- `Import.Detail.EditSelected`
- `Import.CourseSettings.TimeZoneCombo`
- `Import.CourseSettings.ColorCombo`
- `Import.CourseSettings.Save`
- `Import.CourseSettings.Reset`
- `Import.ParsedCoursesHint`
- `Import.ParsedCourses.Mode.RepeatRules`
- `Import.ParsedCourses.Mode.AllTimes`
- `Import.ParsedCourseGroups`
- `Import.UnresolvedCourseGroups`
- `Import.AddedChanges`
- `Import.UpdatedChanges`
- `Import.DeletedChanges`
- `Settings.PageRoot`
- `Settings.ImportSourcesSection`
- `Settings.BrowseLocalFiles`
- `Settings.SourceFileCards`
- `Settings.SourceFileCard.TimetablePdf`
- `Settings.SourceFileCard.TimetablePdf.Browse`
- `Settings.SourceFileCard.TimetablePdf.Replace`
- `Settings.SourceFileCard.TimetablePdf.Remove`
- `Settings.SourceFileCard.TeachingProgressXls`
- `Settings.SourceFileCard.TeachingProgressXls.Browse`
- `Settings.SourceFileCard.TeachingProgressXls.Replace`
- `Settings.SourceFileCard.TeachingProgressXls.Remove`
- `Settings.SourceFileCard.ClassTimeDocx`
- `Settings.SourceFileCard.ClassTimeDocx.Browse`
- `Settings.SourceFileCard.ClassTimeDocx.Replace`
- `Settings.SourceFileCard.ClassTimeDocx.Remove`
- `Settings.TimetableResolutionSection`
- `Settings.ProgramSettingsSection`
- `Settings.TimeProfileModeCombo`
- `Settings.ProgramSettingsButton`
- `Settings.ProviderSection`
- `Settings.DefaultCalendarColorCombo`
- `ProgramSettingsOverlay.Root`
- `ProgramSettingsOverlay.Close`
- `ProgramSettings.WeekStartCombo`
- `ProgramSettings.LocalizationCombo`
- `ProgramSettings.GoogleTimeZoneCombo`
- `ProgramSettings.SyncGoogleOnStartupToggle`
- `ProgramSettings.StatusNotificationsToggle`
- `ProgramSettings.ThemeToggle`
- `ProgramSettings.AboutButton`
- `CourseEditorOverlay.Root`
- `CourseEditor.StartDatePicker`
- `CourseEditor.EndDatePicker`
- `CourseEditor.TimeZoneCombo`
- `CourseEditor.ColorCombo`
- `CoursePresentationEditorOverlay.Root`
- `CoursePresentationEditor.TimeZoneCombo`
- `CoursePresentationEditor.ColorCombo`
- `AboutOverlay.Root`
- `AboutOverlay.Close`

## Layer Intent

- `Domain`: core timetable concepts, normalized schedule concepts, unresolved items, and sync diff models.
- `Application`: use-case contracts, parser contracts, normalization contracts, persistence ports, and provider adapter abstractions.
- `Infrastructure`: parser implementations, local persistence, and the Google and Microsoft provider adapter implementations.
- `Presentation.Wpf`: WPF views, view models, shell composition, UI-only concerns, and formatting of dynamic workspace/apply/diff/source-file text from structured application data.

## Planned Milestones

1. Harden PDF/XLS/DOCX parsing against additional sanitized CQEPC same-template fixtures.
2. Extend local diff and snapshot behavior for more edge cases and larger change sets.
3. Harden Google remote-sync behavior with live remote-drift handling and more edge-case coverage.
4. Harden Microsoft and Google remote-sync behavior with more edge-case coverage and failure-path tests.
5. Add live remote-drift handling and broader provider-aware destination discovery where the providers support it.

## Documentation

- Product behavior: [SPEC.md](SPEC.md)
- Architecture and dependency boundaries: [docs/architecture.md](docs/architecture.md)
- PDF parsing rules: [docs/parsers/timetable-pdf.md](docs/parsers/timetable-pdf.md)
- XLS parsing rules: [docs/parsers/teaching-progress-xls.md](docs/parsers/teaching-progress-xls.md)
- DOCX parsing rules: [docs/parsers/class-time-docx.md](docs/parsers/class-time-docx.md)
- Import diff Chinese notes: [docs/import-diff-notes.zh-cn.md](docs/import-diff-notes.zh-cn.md)

## Recent Import Diff UI Notes

- Import grouped diff rows now remove visible expander/combo arrows, keep the course `i` action at the trailing edge, preserve a persistent selected-occurrence outline after pointer exit, and render rule/occurrence rows with add/update/delete/conflict color surfaces in both light and dark themes.
- Updated Import change groups now show course-level before/after repeat summaries, right-aligned change headers, and stronger dark-theme selected-state contrast for the shared selector chrome.
- Updated occurrence cards now separate localized `Changed items`, `Shared details`, and actual `Before / After` values while keeping metadata already embedded in the Google Calendar description, such as class, campus, teacher, teaching-class composition, and course type, inside the notes payload instead of duplicating it as standalone field rows.
- Background UI smoke coverage now also captures the Import page after switching to dark theme so selector/readability regressions can be spotted from app-rendered screenshots.
- Import change headers are now left-aligned all the way through the grouped expander layout, selected dark-theme states use stronger text/background contrast, and managed-note parsing now splits both newline-delimited and slash-delimited metadata back into consistent teacher, teaching-class, and notes rows.
- Parser-produced slash-delimited metadata tails that do not include an explicit `Notes:` label, such as class-size / assessment / hour-composition / credit segments, are now preserved as the rendered `After` notes payload in Import instead of falling back to `No notes`.
- Compact and smaller-window Import layouts now switch earlier, shrink the top action/filter chrome, keep `Select current page` wired to the visible diff rows, rename the header sync action to `Sync Current Calendar`, and let grouped course cards expand from the whole card surface instead of a duplicate trailing chevron. Any remaining legacy course-presentation overlay backdrop keeps a transparent hover state so pointer movement outside it cannot tint the full background.
- Import grouped course and repeat-rule expanders now use a transparent click layer plus the same rounded muted highlight treatment as occurrence rows, so hover/selection color no longer leaks past card corners. Occurrence change-summary badges are generated only from structured changed fields, not from context summary text, so unchanged location labels cannot produce a false location-change badge; added/deleted rows keep status-only summaries. Legacy CQEPC note tails are normalized to labeled display parts before comparison.
- Expanding a first-level Import course group now makes the right detail panel list only repeat rules parsed from the currently uploaded timetable files. That course-level list is intentionally not linked to calendar diff/provider state and does not display Added, Updated, Deleted, or Unchanged labels.
- Import course `i` actions now switch the right detail panel to inline course settings with time-zone and Google Calendar color controls. Save appears only for unsaved edits, Reset appears only when a saved override exists, and those overrides survive unrelated timetable-resolution preference refreshes.
- Import course and repeat-rule detail panels now omit duplicated diff chrome such as change-summary cards, unchanged badges, parsed-only source fields, and shared unchanged-detail cards. Parsed repeat-rule lists also split sparse timetable week expressions into continuous segments, so a lesson like `3-9周,11-20周` appears as two repeat rules instead of one misleading continuous range; rule and occurrence edit forms stay collapsed until the right-panel Edit action is clicked.
- Google Calendar notes now render as a single-column code-style red/green line diff. Added/deleted rows omit note diff blocks, and note-only updates show a notes update instead of expanding every parsed metadata segment as its own field change.
- Google Calendar note payload edits now happen inline on editable `Notes:` rows inside the diff block; deleted rows and app-managed metadata remain read-only, and the old separate notes editor under the diff block is no longer shown.
- Clicking an already-expanded repeat-rule header now reselects that repeat-rule detail after reviewing one of its concrete dates, so returning from an occurrence detail does not require collapsing and reopening the group.
- Import inline editor fields stay compact at one-line height and grow only when wrapped text needs it. The top Reset Override action is keyed from any saved customization or unsaved editor/settings change, instead of only the currently visible edit surface.
- Import fallback-confirmation, local schedule-conflict, and unresolved-course cards are pinned above normal change groups; unresolved-course rows now open the right-side inline editor with Save enabled even when defaults are unchanged, then disappear from the pinned area after confirmation generates occurrences. The inline editor uses a masked `HH:mm` input plus the configured Google default time zone when parsed occurrences have no explicit provider zone. Single-hour entries such as `6` are accepted as `06:00`, Enter moves from start time to end time and then out of the time field, Save/reset preview rebuilds are latest-operation-wins, the task center shows the active local-preview refresh, selected repeat-mode buttons are visually emphasized, and dark-mode DatePicker popups are theme-adjusted and covered by a real-storage UI regression that checks opened-calendar contrast.
- Editing a single concrete occurrence keeps a single-occurrence override only while repeat remains one-time; changing that editor to a recurring rule promotes the save to a rule-level override so the rebuilt preview can show the larger recurrence instead of reverting to one-time. The Import editor supports every-N day/week/month/year recurrence plus multi-select weekdays, treats biweekly as every 2 weeks, auto-swaps inverted start/end dates on save, and shows only one date for one-time overrides. Generated override occurrences that duplicate an existing same-course/same-local-time/same-location slot are merged in the preview, while different courses sharing the same local date/time are pinned as conflicts and counted in the purple conflict metric together with unresolved items.
- Deleted occurrence details replace the edit action with Cancel Delete. Cancel Delete writes a one-time local schedule override for that deleted occurrence so the preview can return it to unchanged/normal schedule display before further manual edits.
- Repeat-rule grouping now treats single-occurrence deletions as deletes. A child Added/Deleted row is promoted to an orange updated repeat-rule group only when the parsed recurrence still has other occurrences that remain in the current timetable.
- User-visible Import labels, fallback text, filter options, and XAML headings must live in localization resource dictionaries rather than inline Chinese string literals. Parser/source metadata tokens may remain in parser lexicons or comparison helpers, but UI copy should flow through `UiText` so English and Chinese resources stay synchronized and avoid mojibake regressions. Import filter, grouping, and sort behavior is driven by semantic selection indexes instead of localized display strings, so changing resource text does not change filtering logic.
