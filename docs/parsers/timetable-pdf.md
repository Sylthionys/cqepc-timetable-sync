# Timetable PDF Parser

Readable Chinese token examples referenced below live in [timetable-pdf.zh-cn.md](./timetable-pdf.zh-cn.md). Runtime parser tokens still live in `CQEPC.TimetableSync.Infrastructure/Parsing/Pdf/TimetablePdfLexicon.cs`.

## Supported CQEPC Shape

The parser targets text-based CQEPC timetable PDFs with the current school-export layout:

- a class header line ending with the timetable suffix token
- seven weekday columns for Monday through Sunday
- regular timetable blocks placed inside the weekday grid
- footer legend and print-time rows beneath the grid
- optional practical-course summary blocks at the bottom

Scanned or image-only PDFs are not supported in v1.

## Parsing Strategy

The parser is implemented in `CQEPC.TimetableSync.Infrastructure/Parsing/Pdf/TimetablePdfParser.cs` and uses `PdfPig`.

Chinese timetable labels and markers used by the parser are centralized in `CQEPC.TimetableSync.Infrastructure/Parsing/Pdf/TimetablePdfLexicon.cs`. Keep the main parser implementation ASCII-safe and add or update CQEPC tokens in that lexicon file instead of scattering literals through parsing logic or tests.

Per page, it:

1. reads positioned letters and drawn page paths
2. groups pages into class sections by detecting class-header lines
3. analyzes the CQEPC page template from weekday headers, column rectangles, left-side grid bands, and footer markers
4. derives weekday column bounds, timetable-body top/bottom, and row-band regions from that layout
5. rebuilds wrapped lines inside layout-scoped row bands before assigning each line back to a weekday column
6. keeps extraction cell-local enough to avoid same-baseline text bleed across adjacent weekdays
7. repairs same-column split blocks before unresolved classification when title-only or metadata-only orphan fragments can be merged losslessly
8. carries bottom-of-page title fragments into the next page and can continue through an additional top-of-page title fragment before parsing the final metadata lead
9. silently consumes successful top-of-page metadata carryover tails instead of surfacing user-visible noise diagnostics
10. refuses cross-page carryover stitching when the next top-of-page block is already a standalone course cell, so one page's residue does not swallow the next page's new course
11. splits a merged top-of-page block when metadata carryover residue and the next standalone course were extracted into one column block
12. also defers bottom-of-page blocks whose tagged metadata is visibly truncated and completes them from the next page's metadata-only tail when the stitch is lossless
13. when the source PDF clips only the trailing tagged-note tail and no continuation page exists, can conservatively recover the missing teaching-count/assessment/hour/credit fields from an exactly matching peer block with the same title, campus, teacher, and teaching-class composition
14. parses each block as wrapped title lines followed by a metadata lead starting with the period-range token

## Extracted Fields

Pages that begin with metadata continuation from the previous timetable cell are treated as carryover: metadata-only fragments are consumed internally when they can be attached to the previous parsed block, mixed carryover-plus-title blocks are trimmed back to the new course title before metadata parsing, merged top-of-page metadata-prefix-plus-course blocks are split before carryover resolution, and title continuations split across a page break are reassembled before metadata parsing when the weekday-column neighbor chain is unambiguous.
Standalone top-of-page course cells are not treated as continuation targets, even when the previous page ended with a title fragment in the same weekday column.
Bottom-of-page blocks that already contain the period lead but stop mid-tagged-metadata are also treated as carryover candidates; if the next page starts with the missing metadata tail in the same weekday column, the parser upgrades the block instead of emitting a truncated unresolved item.
If the CQEPC header page pushes a title-only fragment into the footer strip below the last normal grid band, that footer-strip title still has to stay inside the weekday-column extraction window so the next page's metadata-only continuation can resolve into the intended course instead of disappearing.
If a CQEPC export visibly clips only the trailing note-style metadata and there is no continuation page, the parser may still recover that tail from another fully parsed block only when the structured identity already matches exactly enough to make the fill lossless in practice: same course title, same campus, same teacher, and same teaching-class composition. It does not borrow week expressions, weekdays, periods, locations, or class headers.

For each regular timetable block, the parser extracts:

- `CourseTitle`
- `CourseType` from the CQEPC title marker when present
- `Weekday`
- `PeriodRange`
- `WeekExpression.RawText`
- `Campus`
- `Location`
- `Teacher`
- `TeachingClassComposition`
- `Notes` for remaining labeled metadata such as class size, assessment mode, hour composition, or credits
- `SourceFingerprint`

The parser preserves the raw week-expression text exactly as extracted between the period-range lead and the first tagged metadata field.
For regular timetable blocks, `SourceFingerprint` is intentionally block-local. It hashes the normalized block text together with the class/page anchor used during parsing, not the whole PDF file hash. This keeps unchanged lessons stable across renamed or lightly revised timetable exports while still letting genuinely changed blocks receive a new fingerprint.

## Tagged Metadata Rules

Tagged metadata is extracted from label markers inside the block payload. See the companion token file for the exact Chinese labels.
Known label aliases must be canonicalized before structured-field assignment. In particular, the shorter `/教学班:` form must be treated the same as `/教学班组成:` so teacher values do not accidentally absorb the teaching-class payload.

Unknown trailing labeled metadata is preserved in `Notes` instead of being guessed into new structured fields.

When the parser keeps that trailing metadata only as slash-delimited tagged note segments, downstream Import diff rendering must preserve the recovered tail as `After` notes even when there is no explicit `Notes:` label in the payload.

The parser also tolerates short metadata-tail fragments such as split credit/hour suffixes when they can be attached losslessly to the immediately preceding block in the same weekday column or the previous page carryover target.

If a regular block still contains visibly truncated tagged metadata after carryover repair, such as half a label (`/教学班人`, `/考核`, `/课程学`) or a fixed-format tail that stops before later required tags, the parser keeps the raw block as unresolved instead of exporting a partially parsed course.

## Unresolved Items

The parser emits `UnresolvedItem` values for one course-block case:

- `AmbiguousItem`
  - generated when a block contains text but cannot be parsed into a valid title plus period-range metadata payload
  - also generated when the source PDF itself truncates fixed-format tagged metadata and the parser can no longer recover a lossless structured block
  - currently emitted with stable codes such as `PDF106`, `PDF107`, `PDF108`, or `PDF109` depending on the failure
  - carries the raw truncated source text so the UI can show exactly what the PDF still contains

Footer practical-summary notes are treated as layout/footer markers only. They are not parsed into timetable output and do not produce unresolved items.

## Model Shape

The parser keeps the cross-layer output shape unchanged:

- `ClassSchedule(ClassName, CourseBlocks[])`
- `CourseBlock(ClassName, Weekday, CourseMetadata, SourceFingerprint, CourseType?)`
- `CourseMetadata(CourseTitle, WeekExpression, PeriodRange, Notes, Campus, Location, Teacher, TeachingClassComposition)`
- `UnresolvedItem(AmbiguousItem, ClassName, Summary, RawSourceText, Reason, SourceFingerprint)`

## Diagnostics and Warnings

The parser can emit diagnostics or warnings for:

- missing PDF files
- unreadable PDFs
- pages without extractable text
- pages where weekday columns cannot be resolved
- pages that appear before any class header is found
- ambiguous timetable blocks that are kept unresolved instead of dropped
- skipped regular blocks with specific reasons such as missing period leads, missing week expressions, likely merged text, metadata-tag failures, and empty/non-course cells

Those diagnostic and unresolved codes are stable parser output. WPF localizes them in Presentation by code first and falls back to the stored parser message or unresolved reason when a localization key is missing.

## Regression Coverage

Synthetic PDF fixtures cover:

- multi-class PDFs
- same-template segmented CQEPC grid geometry for both header pages and continuation pages
- same-baseline blocks in adjacent weekday columns
- shared teaching groups
- wrapped metadata inside one timetable cell
- wrapped title-only fragments followed by metadata-only fragments in the same column
- cross-page title continuation followed by a later metadata lead
- rejection of false carryover merges when the next page already starts with a standalone course block
- top-of-page metadata tails that are consumed without user-visible carryover diagnostics
- top-of-page metadata tails that accidentally merged into the next standalone course block
- sparse or odd/even week expressions
- practical-course summary footer blocks as excluded footer content, ensuring the parser still uses them to find the grid boundary without surfacing them as parsed or unresolved timetable items
- malformed regular timetable blocks
- source-truncated metadata payloads that must stay unresolved instead of being half-parsed

Fixtures are generated in code and do not depend on private school exports.

## Known Limitations

- v1 supports text-based PDFs only; there is no OCR fallback
- the parser is intentionally CQEPC-layout-specific and depends on the current CQEPC same-template header, weekday-column layout, left-side grid bands, footer legend, and practical-summary footer format
- raw punctuation may vary between fullwidth and ASCII forms depending on how the source PDF encodes glyphs; parser matching is tolerant, while raw extracted text is preserved as returned by the PDF text layer
- same-column orphan recovery is intentionally conservative; if a neighbor merge is not clearly local and lossless, the block stays unresolved
- practical-course summaries at the page footer are ignored by design; if the school only provides a course in that footer note and not in the timetable grid, the parser will not emit it
- some school-exported PDFs visibly clip metadata near the page edge or page bottom; those blocks are now treated as unresolved source truncation rather than silently exported with partial metadata
- source fingerprints are designed to survive whole-file churn, not semester-spanning identity reuse; later layers still need occurrence-level sync identity plus snapshot/provider state to decide whether two exports describe the same concrete lessons
