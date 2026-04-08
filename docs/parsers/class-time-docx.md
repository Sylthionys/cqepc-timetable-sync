# Class-Time DOCX Parser

## Supported CQEPC Shape

The parser targets the CQEPC class-time Word document layout used for period-time profiles:

- a title paragraph for the semester class-time sheet
- an optional note paragraph such as `第5-6节为中午时段，原则上不安排课程`
- a single main table whose first row starts with `教学地点`
- remaining header cells shaped like `第1-2节`, `第3-4节`, `第5-6节`, `第7-8节`, `第9-10节`, `第11-12节`

Each data row in that table is treated as one named time profile.

Chinese DOCX labels and row-keyword tokens used by the parser are centralized in `CQEPC.TimetableSync.Infrastructure/Parsing/Word/ClassTimeDocxLexicon.cs`. Keep the main parser implementation ASCII-safe and add or update CQEPC Chinese labels in that lexicon file instead of scattering literals through parsing logic or tests.

## Extracted Data

The parser extracts:

- profile name from the first-column row label
- deterministic profile ID from the normalized row label
- campus or location-family text
- applicable structured course-type tags
- period ranges
- start and end times
- structured noon-window notes for periods `5-6`

The parser does not infer calendar events, week mappings, or missing time slots.
Those parsed profiles are later consumed by timetable resolution, which now supports automatic matching, an explicit default profile, and per-course overrides.
Automatic matching prefers same-campus profiles that match the inferred course type, but if that profile family does not define the requested period range, normalization may conservatively fall back to another same-campus profile that does define that exact period range.
When that fallback path is used, the course is still normalized into concrete occurrences, but Import must surface the fallback as a top-of-page confirmation item and leave the related diff change unchecked until the user confirms it.

## Profile Mapping Rules

- Period columns are range-based. `第1-2节` becomes `PeriodRange(1, 2)`, not two inferred single-period rows.
- Time cells accept plain ranges such as `8:30-10:00` and annotated ranges such as `8:30-9:50(课间不休息)`.
- Cells containing `—` or blank values are treated as intentionally unavailable and are skipped without guessing.
- The first-column row label remains the profile display name.
- Campus extraction prefers:
  - text before the first course-type parenthesis, or
  - the substring ending at `校区` when campus and venue text are combined in one label

## Course-Type Tags

Structured course-type tags are inferred from row-label keywords:

- `理论` => `Theory`
- `实训` or `机房` => `PracticalTraining`
- `体育` => `SportsVenue`

Rows may map to multiple tags. For example, `九龙坡校区实训场地、机房、体育场地` maps to both `PracticalTraining` and `SportsVenue`.

## Noon-Window Note

When the DOCX note paragraph states that periods `5-6` are generally noon time, the parser attaches a structured `NoonWindow` note to each parsed profile for `PeriodRange(5, 6)`.

The note is preserved for later UI deemphasis. It is not treated as a blocking warning and does not remove the period-time slot from the parsed profile.

## Diagnostics

The parser emits diagnostics for:

- missing or unreadable DOCX packages
- missing or malformed profile tables
- malformed header cells
- malformed row labels
- malformed time cells

The current implementation uses stable codes such as `DOCX001`-`DOCX004` for table-shape issues and `DOCX100`-`DOCX101` for file-level failures. WPF localizes those diagnostics in Presentation by code first and falls back to the stored parser text if a localization key is missing.

Malformed rows are skipped, while other valid rows continue parsing.
