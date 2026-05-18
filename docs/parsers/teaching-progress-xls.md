# Teaching Progress XLS Parser

This document is the contract for the CQEPC teaching-progress workbook parser. Runtime source tokens live in `CQEPC.TimetableSync.Infrastructure/Parsing/Spreadsheet/TeachingProgressXlsLexicon.cs`.

## Supported CQEPC shape

The parser targets CQEPC semester planning workbooks with:

- visible worksheet titles containing academic-year and semester metadata such as `2025/2026学年第二学期`;
- header rows containing `月`, `日`, and `周`;
- a class column labeled `班级`;
- contiguous semester week columns between the class column and trailing arrangement columns.

The parser reads every visible worksheet that participates in the workbook shape and expects the semester grid to agree across worksheets.

## Source-of-truth role

The teaching-progress XLS is used only for semester week-to-date mapping. It is not the source of truth for regular weekly course events, practical-course semantics, locations, teachers, or period times.

## Extracted data

The parser extracts:

- semester week number;
- week start date;
- week end date.

It does not treat row symbols such as `R`, `V`, `/`, `:`, or `E` as week-date truth. It ignores trailing arrangement columns such as `理论周数`, `设计名称`, and `实习、实训名称` except when identifying where the week grid ends.

## Date resolution rules

- Month rows are forward-filled across merged or visually merged headers.
- Day cells are parsed as start/end pairs, including cross-month shapes such as `30/5` and `27/3`.
- Academic year and semester are resolved from workbook metadata.
- Week dates must be monotonic and aligned to seven-day ranges.

If workbook dates are complete and consistent, the workbook mapping wins.

## Fallback behavior

If the workbook has usable week numbers but incomplete or ambiguous dates, the parser may use `firstWeekStartOverride`:

- week 1 starts on the override date;
- each later week starts seven days later;
- each week ends six days after its start.

When fallback is applied, the parser emits a warning and diagnostic so the UI can explain that workbook dates were not trusted.

Outside the parser, workspace preview stores a parsed week-1 date as an auto-derived timetable-resolution value. That value can populate Settings without becoming a manual override.

## Diagnostics

Diagnostics should cover:

- missing or malformed week rows;
- missing or malformed date cells;
- missing academic-year or semester metadata;
- conflicting visible worksheets;
- ignored trailing arrangement columns;
- manual override fallback.

Presentation localizes diagnostic codes and falls back to parser text when a localization key is missing.

## Regression expectations

Tests should use generated or sanitized workbook fixtures. They should cover consistent grids, cross-month ranges, invalid date cells, conflicting worksheets, and manual first-week fallback.
