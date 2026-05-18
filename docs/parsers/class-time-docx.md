# Class-Time DOCX Parser

This document is the contract for the CQEPC class-time Word document parser. Runtime source tokens live in `CQEPC.TimetableSync.Infrastructure/Parsing/Word/ClassTimeDocxLexicon.cs`.

## Supported CQEPC shape

The parser targets CQEPC class-time documents with:

- a title paragraph for the semester class-time sheet;
- an optional note paragraph such as `第5-6节为中午时段，原则上不安排课程`;
- a main table whose first row starts with `教学地点`;
- period header cells such as `第1-2节`, `第3-4节`, `第5-6节`, `第7-8节`, `第9-10节`, and `第11-12节`;
- data rows where each row is one named time profile.

## Source-of-truth role

The class-time DOCX is used only for period-time profiles. It is not the source of truth for week dates, regular course blocks, or provider payload choices.

## Extracted data

The parser extracts:

- profile display name from the first-column row label;
- deterministic profile ID from the normalized row label;
- campus or location-family text;
- structured course-type tags;
- period ranges;
- start and end times;
- structured noon-window notes for periods `5-6`.

The parser does not infer calendar events, week mappings, or missing time slots.

## Profile mapping rules

- Period columns are range-based. `第1-2节` maps to `PeriodRange(1, 2)`, not two inferred single-period rows.
- Time cells accept plain ranges such as `8:30-10:00` and annotated ranges such as `8:30-9:50(课间不休息)`.
- Cells containing `—` or blank values are intentionally unavailable and must be skipped without guessing.
- The first-column row label remains the profile display name.
- Campus extraction prefers text before the first course-type parenthesis, or the substring ending at `校区` when campus and venue text are combined.

## Course-type tags

Structured course-type tags are inferred from row-label keywords:

- `理论` => `Theory`;
- `实训` or `机房` => `PracticalTraining`;
- `体育` => `SportsVenue`.

Rows may map to multiple tags. For example, `九龙坡校区实训场地、机房、体育场地` maps to both `PracticalTraining` and `SportsVenue`.

## Noon-window note

When the DOCX note states that periods `5-6` are generally noon time, the parser attaches a structured `NoonWindow` note to each parsed profile for `PeriodRange(5, 6)`.

The note is preserved for later UI deemphasis. It is not a blocking warning and does not remove the period-time slot from the parsed profile.

## Normalization integration

Normalization consumes parsed profiles through timetable-resolution settings:

- automatic default profile selection;
- explicit default profile selection;
- per-course time-profile overrides.

Automatic matching prefers same-campus profiles that match the inferred course type. If that preferred family lacks the requested period range, normalization may fall back to another same-campus profile that defines the exact period range. That fallback must be surfaced as an Import confirmation and left unchecked until the user confirms it.

If a configured override points to a missing profile or a profile that lacks the requested periods, the related course stays unresolved rather than silently using another profile.

## Diagnostics

Diagnostics should cover:

- missing or unreadable DOCX packages;
- missing or malformed profile tables;
- malformed header cells;
- malformed row labels;
- malformed time cells.

Malformed rows may be skipped while other valid rows continue parsing. Presentation localizes diagnostic codes and falls back to parser text when needed.

## Regression expectations

Tests should use generated or sanitized DOCX fixtures. They should cover profile IDs, campus extraction, course-type tags, noon-window notes, unavailable cells, malformed times, and automatic/fallback normalization integration.
