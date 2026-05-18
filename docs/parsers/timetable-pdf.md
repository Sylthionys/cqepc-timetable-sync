# Timetable PDF Parser

This document is the contract for the CQEPC timetable PDF parser. Human-readable source-token examples live in [timetable-pdf-source-tokens.md](./timetable-pdf-source-tokens.md). Runtime token matching lives in `CQEPC.TimetableSync.Infrastructure/Parsing/Pdf/TimetablePdfLexicon.cs`.

## Supported CQEPC shape

The parser targets text-based CQEPC timetable PDFs with the current school-export layout:

- class header lines ending with the timetable suffix token;
- seven weekday columns from Monday through Sunday;
- regular timetable blocks inside the weekday grid;
- period-range lead text inside regular blocks;
- labeled metadata segments for campus, venue, teacher, teaching class, assessment, hours, credits, and related note payload;
- footer legend and print-time rows beneath the grid;
- optional practical-course summary/footer material below the regular timetable grid.

Scanned or image-only PDFs are not supported.

## Source-of-truth role

The timetable PDF is the source of truth for regular weekly course blocks. It is not the source of truth for semester dates or period-time profiles.

Practical summary/footer material must not be exported automatically. Course-like practical summary text that can be isolated should be preserved as unresolved source data with raw text and class context; pure footer legends, print timestamps, and template decoration remain layout-only.

## Parsing strategy

The implementation uses `PdfPig` and should remain layout-aware:

1. read positioned text and drawn page paths;
2. detect class sections;
3. infer page template regions from weekday headers, column rectangles, row bands, and footer markers;
4. rebuild wrapped lines within row-band and weekday-column bounds;
5. parse title lines followed by a metadata lead;
6. assign structured metadata fields from labeled segments;
7. generate parser warnings/diagnostics for skipped or malformed source shapes;
8. produce `ClassSchedule` values with `CourseBlock` children and block-local `SourceFingerprint` values.

The extraction window must stay cell-local enough to avoid same-baseline bleed between adjacent weekday columns.

## Carryover and repair rules

Conservative repair is allowed for CQEPC exports that split a regular course block across a page boundary:

- metadata-only top-of-page tails may attach to the previous weekday cell when the target is unambiguous;
- title fragments in a previous page's footer strip may participate in carryover matching with a next-page metadata tail;
- a top-of-page block that is already a standalone course must not be swallowed by the previous page's residue;
- if extraction merges a metadata tail and the next standalone course into one top-of-page block, the parser must split the merged block before carryover resolution;
- trailing tagged metadata may be recovered from an exactly matching peer block only when the structured identity is already safe: same title, campus, teacher, and teaching-class composition.

Repair must not borrow week expressions, weekdays, periods, locations, class headers, or unrelated metadata.

## Extracted fields

For each regular timetable block, the parser extracts:

- `CourseTitle`;
- `CourseType` when a CQEPC title marker is present;
- `Weekday`;
- `PeriodRange`;
- `WeekExpression.RawText`;
- `Campus`;
- `Location`;
- `Teacher`;
- `TeachingClassComposition`;
- `Notes` for remaining labeled metadata;
- `SourceFingerprint`.

The raw week-expression text is preserved exactly as extracted between the period-range lead and the first recognized tagged metadata field.

`SourceFingerprint` is block-local. It hashes normalized block content plus parsing anchor information instead of the whole PDF file hash, so unchanged lessons stay stable across renamed or lightly revised exports.

## Tagged metadata rules

Known tagged metadata labels are canonicalized before structured assignment. The short teaching-class alias must be treated as the same field as the longer teaching-class composition label so teacher values do not absorb teaching-class payload.

Unknown trailing labeled metadata is preserved in `Notes` instead of guessed into new structured fields.

When the parser keeps trailing metadata only as slash-delimited tagged note segments, downstream Import rendering must preserve those segments in the resulting notes payload.

If a regular block still contains visibly truncated tagged metadata after carryover repair, the parser must keep the raw block unresolved instead of exporting a partially parsed course.

## Unresolved and diagnostic behavior

The parser emits diagnostics for malformed grid regions, skipped cells, truncated tagged metadata, and parsing failures. Stable diagnostic codes should be kept so Presentation can localize messages by code and fall back to parser text.

A regular block that contains course-like text but cannot be parsed into a valid title plus metadata payload should become an unresolved item carrying the raw source text and class context.

Practical-summary rows that describe ambiguous course work should produce unresolved review items rather than auto-exportable occurrences. Pure footer legend or print-time rows are layout-only.

## Model shape

The parser keeps the cross-layer output shape:

- `ClassSchedule(ClassName, CourseBlocks[])`;
- `CourseBlock(ClassName, Weekday, CourseMetadata, SourceFingerprint, CourseType?)`;
- `CourseMetadata(CourseTitle, WeekExpression, PeriodRange, Notes, Campus, Location, Teacher, TeachingClassComposition)`;
- `UnresolvedItem` for regular source blocks that remain ambiguous.

## Regression expectations

Parser tests should cover:

- single-class and multi-class PDFs;
- weekday column isolation;
- wrapped line reconstruction;
- cross-page carryover and refusal cases;
- practical-summary/footer unresolved handling;
- tagged metadata aliases;
- block-local fingerprint stability;
- unresolved malformed blocks;
- UTF-8-safe Chinese source-token handling.

Tests should use generated or sanitized fixtures, not private raw school exports.
