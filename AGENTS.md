# AGENTS.md

## Project
CQEPC Timetable Sync

A Windows desktop app that parses CQEPC timetable source files and syncs normalized course schedules into:
- Google Calendar
- Google Tasks
- Outlook Calendar
- Microsoft To Do

The app is local-first. Parsing, preview, diffing, and user confirmation happen locally before remote write actions.

---

## Primary Goal
Turn school-exported timetable files into a reliable, reviewable, provider-aware sync workflow.

The app must:
1. Parse CQEPC source files into normalized schedule data.
2. Show a local calendar-style preview before sync.
3. Diff newly parsed data against previous local snapshots and already-managed remote items.
4. Let the user selectively apply changes.
5. Keep provider-specific behavior explicit instead of pretending Google and Microsoft behave identically.

---

## Non-Goals
Do not expand scope into these areas unless explicitly requested:
- scraping or logging into school systems
- OCR from screenshots
- browser automation for timetable parsing
- automatic inference of unknown practical-course time slots
- background sync daemons in early versions
- multi-school generic parser architecture before CQEPC format is stable

---

## Tech Stack Rules
- Use .NET 8.
- Use WPF for the desktop UI.
- Use MVVM.
- Keep business logic out of code-behind.
- Prefer small, testable services with clear interfaces.
- Use async APIs properly for file I/O and network I/O.
- Avoid unnecessary framework churn unless there is a concrete benefit.

Recommended layers:
- `Domain`
- `Application`
- `Infrastructure`
- `Presentation`

---

## Source-of-Truth Rules

### 1. PDF timetable
The timetable PDF is the source of truth for regular class blocks.

From the PDF, parse:
- class name
- course title
- course type marker if present
- weekday
- period range
- week expression
- campus
- location
- teacher
- teaching class composition
- additional metadata text

The import UI must:
- show a class dropdown only when multiple classes are found
- show a static class label when only one class is found

Practical-course summary blocks at the bottom of a page must be captured as unresolved items.
They must **not** be auto-exported unless the user later supplies enough information.

### 2. XLS teaching progress file
The XLS file is used only for semester week-to-date mapping.

Use it to derive:
- semester week numbers
- start date and end date of each week
- optional semester span sanity checks

Do **not** treat the XLS as the source of truth for regular weekly timetable events.

Ignore internship / training / practical semantics in the XLS except where they are needed to understand week-date mapping.

The user must be allowed to manually set the first-week start date as an override.

### 3. DOCX class-time file
The DOCX file is used only for period-time profiles.

Use it to derive named time profiles such as:
- campus
- course type
- period index
- start time
- end time

Do not hardcode a single universal period table for all classes and campuses.

User overrides must be supported.

---

## Normalization Rules
Always normalize parsed data into concrete schedule occurrences before export.

Pipeline:
1. Parse raw source files.
2. Build normalized timetable blocks.
3. Resolve week expressions into exact weeks.
4. Resolve periods into exact datetimes using the selected time profile.
5. Produce concrete occurrences.
6. Merge into recurring export groups only when the merge is lossless.

Important rules:
- Never silently drop weeks.
- Never silently merge mismatched location/time/course variants.
- Keep unresolved items separate from valid occurrences.
- Preserve enough metadata to reconstruct why an occurrence exists.

Lossless merge means:
- same title
- same notes payload structure
- same location
- same provider target type
- same weekday/time shape
- compatible week pattern

If a recurring merge would hide meaningful differences, do not merge it.

---

## Metadata Mapping Rules
Normalized course data should preserve structured fields, not only display strings.

Store at least:
- `SourceClassName`
- `CourseTitle`
- `CourseType`
- `WeekExpressionRaw`
- `ResolvedWeeks`
- `Weekday`
- `PeriodStart`
- `PeriodEnd`
- `Campus`
- `Location`
- `Teacher`
- `TeachingClassComposition`
- `Notes`
- `SourceFingerprint`

Display mapping defaults:
- event title = course title
- notes/description = smaller metadata text + structured details
- location = parsed value after location marker
- class/campus/teacher composition should remain available in structured metadata

---

## Provider Rules

### Common
- Treat Google and Microsoft as separate providers with separate adapters.
- Store provider item IDs separately.
- Store local source fingerprints separately.
- Only modify items that were created and are managed by this app.
- Never blindly overwrite unrelated remote items.
- Every destructive change must be previewed before apply.

### Google
- Google Calendar is the primary target for exact timed class reminders.
- Google Tasks support should be optional and rule-based.
- Do not assume Google Tasks are equivalent to calendar alarms.
- Prefer provider-safe app metadata storage mechanisms for managed events.

### Microsoft
- Outlook Calendar support must include create/update/delete for managed events.
- Microsoft To Do support must be provider-aware and separate from calendar event logic.
- Categories may be used where supported.
- Reminder-capable task flows should remain explicit and testable.

---

## Diff and Sync Rules
The app must be preview-first.

Before any write action:
1. compare the newly parsed normalized schedule against the previous local snapshot
2. classify changes into:
   - Added
   - Updated
   - Deleted
   - Unresolved
3. show the diff clearly in the UI
4. let the user choose what to apply

UI expectations:
- Added = green emphasis
- Deleted = red emphasis with strikethrough
- Updated = clear before/after comparison
- Unresolved = separate warning area, never mixed with valid export items

Do not perform destructive sync without explicit confirmation.

---

## UI Rules
The UI should feel modern, minimal, and readable.

Required surfaces:
- Home page with calendar-style preview
- Import/diff page
- Settings page
- About overlay opened from a small button near the end of Settings

Behavior rules:
- default current date uses local computer time
- week start preference supports Monday or Sunday
- settings should allow default provider selection
- settings should allow default destination calendar/list selection
- settings should allow provider-aware category/color defaults
- file import should live in Settings or a clearly labeled import flow
- About should open as a lightweight overlay, not a heavy separate page

Animation rules:
- use subtle transitions only
- do not sacrifice clarity for motion
- avoid flashy or noisy effects

---

## Configuration and Overrides
User-configurable settings must override parsed defaults.

Support overrides for:
- first week start date
- week start display preference
- default provider
- default destination calendar/list
- period-time profile selection
- per-course or per-rule time adjustments if later implemented
- rule-based task generation
- provider-specific default category/color settings

---

## Practical / Unresolved Items
Practical-course summary items and other ambiguous source items must be represented explicitly.

Rules:
- parse them
- preserve raw source text
- mark them as unresolved
- do not auto-export them
- let future UI/features convert them into exportable items only after user confirmation

No silent guessing.

---

## Testing Rules
Every parser and normalization change must be covered by tests.

Required test coverage:
- PDF class extraction
- multi-class PDF handling
- single-class PDF handling
- week expression parsing
- odd/even/sparse week expansion
- time-profile mapping
- normalization to concrete occurrences
- lossless vs non-lossless recurrence grouping
- diff classification
- provider payload construction
- provider metadata round-trip assumptions where possible

Add regression tests for every timetable edge case discovered.

Prefer fixture-based tests with sanitized sample files when possible.

---

## Security Rules
- Never hardcode secrets.
- Never commit OAuth client secrets, tokens, tenant IDs, refresh tokens, or personal calendar identifiers.
- Use local secure storage where appropriate.
- Keep provider credentials outside source control.
- Sanitize logs that may contain personal schedule data.

---

## Documentation Rules
Keep these docs updated when behavior changes:
- `README.md`
- `SPEC.md`
- `docs/architecture.md`
- parser rule docs
- provider mapping docs

When a parsing assumption changes, update both:
1. code/tests
2. the relevant docs

---

## Workflow Rules for Changes
When making code changes:
1. understand the current architecture first
2. avoid mixing parser work, UI work, and provider work in one giant change
3. make small, reviewable commits
4. explain assumptions clearly
5. add or update tests
6. summarize limitations honestly

Do not introduce large speculative abstractions unless a real current need exists.

---

## Definition of Done
A feature is not done unless:
- it works in the intended layer
- tests are added or updated
- docs reflect the new behavior
- edge cases are at least acknowledged
- destructive sync paths remain preview-first
- unresolved timetable items are not silently exported