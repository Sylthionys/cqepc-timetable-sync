# CQEPC Timetable Sync

CQEPC Timetable Sync is a local-first Windows desktop application that turns CQEPC timetable source files into a reviewable sync workflow for Google and Microsoft productivity tools.

The target stack is `.NET 8`, `WPF`, and `MVVM`. The repository now includes a usable desktop workflow with a three-part shell, a compact month Home workspace, an Import diff review surface, a grouped Settings control center for source files and defaults, dark/light theme support, and an in-window About overlay.

## Philosophy

- Local-first: parsing, normalization, previewing, diffing, and confirmation happen locally.
- Preview-first: destructive changes never happen without a visible review step.
- Provider-aware: Google and Microsoft stay separate in architecture and behavior.
- No silent guessing: unresolved practical-course summary items stay unresolved until the user confirms a resolution path.

## Current Scope

The current implementation establishes:

- a solution structure aligned to `Domain`, `Application`, `Infrastructure`, and `Presentation`
- a styled WPF shell with Home, Import, Settings, and About overlay surfaces
- startup-safe WPF localization using UTF-8 resource dictionaries, a persisted language preference, and `Follow System` / `zh-CN` / `en-US` options
- persisted light/dark theme selection applied before the shell is shown
- a local-file onboarding layer for drag-and-drop import, manual file picking, and persisted user-local source references
- structured local-source onboarding state with per-file attention reasons and ordered activity entries, while WPF owns the user-visible wording
- application contracts for parsing, normalization, persistence, and provider adapters
- domain models for parsed blocks, normalized blocks, concrete occurrences, unresolved items, and sync diff concepts
- a real CQEPC timetable PDF parser for class discovery, same-template layout analysis, row-band-scoped regular course blocks, guarded cross-page carryover stitching, parser warnings/diagnostics, and footer-summary exclusion so bottom-of-page practical notes do not surface as parsed or unresolved timetable items
- a real CQEPC teaching-progress XLS parser with diagnostics, manual fallback override input, and auto-derived first-week mapping for preview/settings
- a real CQEPC class-time DOCX parser for range-based period-time profiles, structured course-type tags, and the noon-window note
- a real normalization engine that expands week expressions, resolves concrete occurrences, carries parser unresolved items forward, and produces lossless recurring export groups
- same-campus automatic time-profile fallbacks that keep valid occurrences exportable when the preferred profile lacks the requested periods, while surfacing those items as explicit confirmations in Import
- persisted workspace preferences for week-start choice, timetable-resolution settings, provider defaults, selected provider destinations, provider auth settings, task rules, and course-type category/color mappings
- timetable-resolution settings that separate manual vs XLS-derived first-week start, automatic vs explicit default time-profile mode, and per-course time-profile overrides
- persisted course-schedule overrides that let the user manually confirm unresolved timetable items or adjust a parsed course's name, date span, time, location, notes, and repeat cadence before sync
- a workspace preview orchestration layer that parses the three source files, normalizes occurrences, optionally generates rule-based task candidates, and builds a local diff against the latest accepted snapshot
- a provider execution seam that supports provider-specific destination discovery and preview-first apply orchestration
- Google desktop OAuth support for a local Windows app using a user-selected installed-app client JSON, a system-browser loopback flow, and DPAPI-protected local token storage
- Microsoft desktop auth support for a local Windows app using a user-supplied public client app ID, an MSAL WAM-first flow, browser fallback, and DPAPI-protected local token storage
- locale-invariant sync IDs, diff keys, and week-expression expansion so preview/apply behavior does not drift with the current UI culture
- explicit UTF-8-safe text handling for persisted settings/snapshots/mappings and stream-based loading of provider auth inputs
- Google Calendar create, update, and delete support for app-managed timed events, including recurring-series creation and recurring-instance maintenance
- Google Calendar apply recovery that can rebind stale local mappings to the managed remote event seen in preview before repairing update/delete drift, and executes accepted Google changes in delete -> update -> add order so switching classes does not temporarily stack new events on top of stale managed ones
- Home preview merges parsed timetable occurrences with Google Calendar remote-event recognition, keeps added timetable items green, delete candidates red with strikethrough, unrelated Google items orange, and same-time managed matches on the board as neutral existing items instead of misclassifying them as adds
- optional Google Tasks create, update, and delete support for explicit rule-based day-level follow-up items on the default Google task list
- Outlook Calendar discovery plus create, update, and delete support for app-managed timed events, including recurring-series creation and recurring-member maintenance with immutable IDs
- Microsoft To Do task-list discovery plus create, update, and delete support for explicit rule-based follow-up items, with linked-resource creation when a paired Outlook event exists
- provider-safe Google event metadata storage via `extendedProperties.private` plus a local Google sync-mapping store for remote IDs and source fingerprints
- provider-safe Microsoft metadata storage via Graph open extensions plus local Microsoft sync mappings for remote IDs, fingerprints, recurring-master IDs, and original-start timestamps
- a local snapshot diff/apply flow where Import shows the primary sections in the order Unresolved, Deleted, Parsed Courses, and Added; keeps auxiliary fallback/updated sections after those; labels Calendar vs Task changes clearly; groups same-name parsed, deleted, added, and unresolved items under course headers; keeps Parsed Courses available even when regular diff sections are empty; lets the user switch that surface between grouped repeat rules and per-course all-times inspection before saving the accepted local baseline; and scopes accepted-snapshot replacement to the currently selected class so another class from an older baseline does not leak into delete candidates
- a Home month workspace with Sunday/Monday week-start support, the calendar shown as the first surface without a separate hero card, selected-day agenda details, direct course-detail editing from the selected date, a Google Calendar preview toggle, a sync action that imports existing Google events into the Home board or routes to Settings when Google is not connected, and a primary Home apply action that writes the accepted changes directly without detouring through Import
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
- The latest accepted local snapshot baseline is stored under `%LocalAppData%\CQEPC Timetable Sync\latest-snapshot.json`.
- Google sync mappings are stored under `%LocalAppData%\CQEPC Timetable Sync\google-sync-mappings.json`.
- Microsoft sync mappings are stored under `%LocalAppData%\CQEPC Timetable Sync\microsoft-sync-mappings.json`.
- Google OAuth tokens are stored under `%LocalAppData%\CQEPC Timetable Sync\tokens\google\` using DPAPI-protected payload files.
- Microsoft auth state and tokens are stored under `%LocalAppData%\CQEPC Timetable Sync\tokens\microsoft\` using DPAPI-protected payload files.
- Persisted JSON and direct text writes use UTF-8 explicitly, and Chinese source metadata is preserved through parsing, local storage, UI rendering, and provider payload generation.
- Workspace preference writes are coalesced asynchronously during editing and flushed on shutdown so the latest language, theme, and timetable-resolution selections survive restart.
- Presentation localization assets live under `src/CQEPC.TimetableSync.Presentation.Wpf/Resources/Localization/` as UTF-8 `Strings.en-US.xaml` and `Strings.zh-CN.xaml`.
- The effective UI culture is resolved before the shell is shown using `preferred culture -> system culture or parent match -> en-US`.
- The app remembers the last used local folder for convenience and falls back to the user's Documents folder when that remembered folder is gone.
- Missing, moved, or deleted local files do not block startup. The app marks them as needing attention and waits for the user to replace them.
- When the XLS parses successfully, the first-week start is auto-derived into timetable-resolution state; a manual override can still replace it until the user clears the manual value.
- `%LocalAppData%\CQEPC Timetable Sync\sources\` is reserved for a future app-managed copy mode, but the current implementation keeps `SourceStorageMode.ReferencePath` as the only active behavior.

## Localization

- Settings exposes exactly three UI-language options: `Follow System`, `Simplified Chinese (zh-CN)`, and `English`.
- Settings now keeps `Week Start` and `Language` as equal-width selectors inside the Calendar Display section, places the theme toggle on its own centered row, and centers the About action as a separate bottom button.
- Changing the appearance mode reapplies theme resources immediately and repaints the live shell without restarting the app.
- Theme switching now relies on runtime `DynamicResource` theme brushes across shared styles and page surfaces so dark mode repaints the full UI consistently instead of leaving mixed light cards behind.
- Settings combo boxes use a full-surface click target instead of requiring the arrow glyph hit area.
- Timetable-resolution combo boxes that drive language or time-profile state now bind by stable persisted values, so selecting a mode/profile survives live preview refreshes instead of depending on transient item-object identity.
- XAML-owned labels use merged WPF resource dictionaries, while computed view-model text refreshes through the presentation localization and formatting layer when the language changes.
- The touched Home and Import surfaces now resolve their user-facing labels through those dedicated UTF-8 localization dictionaries instead of hardcoded mixed-language text.
- Runtime language switching now rebuilds the merged localization dictionaries before raising presentation refresh notifications, which keeps Settings/Home/Import labels in sync after switching to English or Chinese.
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
- FlaUI validates that the app launches, the shell appears, navigation works, Home/Import/Settings roots are discoverable, the sidebar can collapse safely, the Settings About entry point can open in background mode, automation sessions can request app-rendered screenshots when the seeded sample workspace is ready, and the scrolled calendar-display section can be captured in both light and dark themes after a real toggle interaction.
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
- `Settings.CalendarDisplaySection`
- `Settings.TimeProfileModeCombo`
- `Settings.WeekStartCombo`
- `Settings.LocalizationCombo`
- `Settings.ThemeToggle`
- `Settings.ProviderSection`
- `Settings.CategoryColorSection`
- `Settings.AboutButton`
- `Settings.AboutButton.Automation`
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
