# Teaching Progress XLS Parser

## Supported CQEPC Shape

The parser targets the real CQEPC teaching-progress workbook layout used for semester planning sheets:

- a visible worksheet title containing academic-year and semester metadata such as `2025/2026学年第二学期`
- a header band with `月`, `日`, and `周`
- a class column labeled `班级`
- contiguous semester week columns between the class column and the trailing arrangement columns

The parser reads every visible worksheet and expects them to agree on the semester grid.

Chinese worksheet tokens used by the parser are centralized in `CQEPC.TimetableSync.Infrastructure/Parsing/Spreadsheet/TeachingProgressXlsLexicon.cs`. Keep the workbook parser implementation ASCII-safe and add or update CQEPC Chinese labels in that lexicon file instead of scattering literals through parsing logic or tests.

## Extracted Data

The parser extracts only:

- semester week numbers
- week start dates
- week end dates

It does not treat row symbols such as `R`, `V`, `/`, `:`, or `E` as week-date truth.
It also ignores trailing columns such as `理论周数`, `设计名称`, and `实习、实训名称` except when identifying where the week grid ends.

## Date Resolution Rules

- The month row is forward-filled across merged or visually merged headers.
- Day cells are parsed as `start/end`, including cross-month shapes such as `30/5` and `27/3`.
- Academic year and semester are resolved from workbook metadata.
- Week dates must be monotonic and aligned to 7-day ranges.

If workbook dates are complete and consistent, the spreadsheet wins.

## Fallback Behavior

If the workbook contains usable week numbers but its date mapping is incomplete or ambiguous, the parser can use `firstWeekStartOverride`:

- week 1 starts on the override date
- each later week starts 7 days later
- each week ends 6 days after its start

When fallback is applied, the parser emits both a warning and a diagnostic so the UI can surface that the workbook dates were not trusted.

Outside the parser, workspace preview stores the parsed week-1 date as an auto-derived timetable-resolution value. That lets Settings show the effective first-week start even before PDF or DOCX parsing is complete, while still keeping manual override separate and user-controlled.

## Diagnostics

The parser emits diagnostics and warnings for:

- malformed or non-sequential week rows
- missing or malformed date cells
- missing academic-year or semester metadata
- conflicting visible worksheets
- ignored trailing arrangement columns
- manual override fallback being applied

The current implementation uses stable codes such as `XLS001`-`XLS004` for grid parsing and `XLS100`-`XLS104` for workbook-level failures or fallback conditions. WPF localizes those messages in Presentation by code first and falls back to the stored parser text if a localization key is missing.

Tests for this parser use in-memory worksheet fixtures and do not depend on private raw school exports.
