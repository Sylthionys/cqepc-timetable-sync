# SPEC

## 1. Product contract

CQEPC Timetable Sync is a local-first Windows desktop application that imports CQEPC timetable source files and turns them into a previewed, user-approved calendar-sync workflow.

Required source files:

- timetable PDF for regular course blocks;
- teaching-progress XLS for semester week-to-date mapping;
- class-time DOCX for period-time profiles.

The app targets `.NET 8`, `WPF`, and `MVVM`.

The supported remote apply target is Google Calendar. Microsoft Calendar and Microsoft To Do remain planned provider work even though infrastructure scaffolding exists.

## 2. Core principles

- **Local-first:** parsing, normalization, previewing, diffing, and user confirmation happen locally.
- **Preview-first:** no remote write, update, or delete is allowed without a visible review step.
- **Provider-aware:** Google and Microsoft contracts remain separate; behavior must not be flattened into a provider-neutral lowest common denominator.
- **Concrete-first normalization:** parsed schedules become exact dated occurrences before export grouping.
- **No silent guessing:** ambiguous or unresolved source data stays separate until the user explicitly resolves it.
- **Culture-stable:** stable IDs, diff keys, week expansion, persisted JSON, and provider payload decisions must not depend on the current UI culture or system-default encoding.
- **Description text is not authority:** app ownership and destructive sync identity must come from provider-safe private metadata and/or local mappings, not ordinary calendar descriptions.

## 3. Source-of-truth rules

### 3.1 Timetable PDF

The timetable PDF is the source of truth for regular weekly course blocks.

The parser must capture, when available:

- class name;
- course title;
- course type marker;
- weekday;
- period range;
- raw week expression;
- campus;
- location;
- teacher;
- teaching-class composition;
- additional labeled metadata as notes;
- block-local source fingerprint.

Practical summary/footer material at the bottom of a timetable page must not be auto-exported. Course-like practical summary text that can be isolated must be preserved as unresolved source data with raw text and class context; pure footer legends, print timestamps, and template decoration remain layout-only.

The PDF parser must stay template-local and cell-local enough to avoid cross-column text bleed. Skipped or malformed grid cells should produce diagnostics rather than disappearing silently. Conservative cross-page carryover repair is allowed only when the continuation chain is unambiguous and does not swallow a standalone top-of-page course block.

### 3.2 Teaching-progress XLS

The teaching-progress workbook is used only for semester week-to-date mapping.

It may provide:

- semester week number;
- week start date;
- week end date;
- optional semester-span sanity checks;
- fallback recomputation from a manual first-week start override when workbook dates are incomplete or ambiguous.

It must not be treated as the source of truth for regular weekly classes. Trailing arrangement columns and row symbols may help identify the week grid, but they must not define exported week-date semantics.

When a valid week-1 mapping is parsed, Settings may expose it as an auto-derived first-week start without converting it into a manual override.

### 3.3 Class-time DOCX

The class-time document is used only for period-time profiles.

It may provide named profiles that vary by:

- campus or location family;
- course type;
- period range;
- start time;
- end time;
- structured noon-window notes.

The app must not assume a single universal period table. Settings must support automatic default profile selection, explicit default profile selection, and per-course overrides scoped by class name plus exact course title.

Automatic profile selection should prefer same-campus profiles that match the inferred course type. If the preferred profile family lacks the requested period range, normalization may fall back to another same-campus profile that defines the exact period range. That fallback keeps the occurrence exportable but must be surfaced as an explicit Import confirmation before apply.

### 3.4 User overrides and local baseline

User overrides are applied above parsed source data and below provider diffing. They may confirm unresolved timetable items, adjust course/occurrence schedules, choose provider time zones or colors, or cancel a pending local delete.

The previous accepted local snapshot is the local diff baseline. It is not proof that Google still contains the corresponding remote event. Remote preview must re-read selected provider state before provider apply decisions are trusted.

## 4. Normalization contract

Normalization must follow this order:

1. parse raw source files;
2. build normalized timetable blocks;
3. resolve week expressions into explicit semester weeks;
4. resolve period ranges into exact start/end times with the effective time profile;
5. apply saved course and occurrence overrides;
6. produce concrete dated occurrences;
7. derive recurring export groups only when the merge is lossless.

Required behavior:

- never silently drop weeks;
- never silently merge mismatched title, location, note, time, time-zone, color, or source variants;
- keep unresolved items separate from valid occurrences;
- preserve enough structured metadata to explain why each occurrence exists;
- use locale-invariant formatting/parsing for stable IDs, logical diff keys, source fingerprints, and week-expression expansion;
- keep timetable PDF source fingerprints block-local, based on normalized block content plus class/page anchor rather than the whole PDF file hash;
- tolerate source-fingerprint drift and small metadata corrections by classifying them as exact/update work where schedule identity is still clear, not as synthetic delete+add batches;
- keep an override that points to a missing profile or missing periods unresolved instead of silently falling back;
- surface same-campus time-profile fallback confirmations before ordinary diff rows.

A recurring export group is lossless only when every represented occurrence shares the same title, location, note payload structure, provider target semantics, time-zone/color payload, weekday/time shape, and compatible recurrence pattern. If a recurrence would hide meaningful differences, the app must write or display separate occurrences/groups.

## 5. Workspace preview contract

Workspace preview builds the user-visible state from local sources, preferences, local snapshots, and optional provider reads.

Required behavior:

- Missing, moved, or deleted local source files must not block startup. They are represented as attention-needed source cards.
- Parser warnings, diagnostics, unresolved items, and effective resolution settings must be visible to the user before apply.
- If the PDF contains multiple classes, the user must select the target class before a ready preview is considered complete.
- Local source replacement, removal, or drag/drop must rebuild the workspace preview and refresh Home, Import, class selection, week mapping, and time-profile options.
- Home may render a local-only preview or a current-calendar preview that merges selected Google Calendar context.
- Import uses the same workspace preview result but remains a local review/adoption flow.
- Program settings may restore an opt-in DPAPI-protected Home render cache as a temporary display hint, but a normal local/provider preview must still refresh and replace it.

## 6. Import diff contract

Import is a review surface for local changes and local overrides. It must not perform provider writes.

Required behavior:

- Show Added, Updated, Deleted, conflict/fallback/unresolved, and unchanged context in grouped course/repeat/occurrence form.
- Keep raw `Added` / `Updated` / `Deleted` identities authoritative for selection and apply. UI grouping is projection-only.
- Default ready changes to selected unless a required fallback or unresolved confirmation is pending.
- Exclude unmanaged Google Calendar context rows from selectable/apply sets.
- Put same-campus time-profile fallback confirmations, local schedule conflicts, and unresolved course blocks above ordinary change groups.
- Let the user confirm unresolved course blocks through an inline editor; after confirmation, the item leaves the pinned unresolved area and re-enters the normal diff/unchanged flow as valid occurrences.
- Let course-group and repeat-rule rows include unchanged occurrence context without letting unchanged rows affect selectable checked/partial state.
- Keep `Unchanged Schedules` as the trailing context area for courses with no effective planned change. When ordinary changes exist, unchanged rules for changed courses stay inside the course group as context instead of being duplicated in the trailing area.
- Show occurrence details with the correct layer scope: course group, repeat rule, single occurrence, course settings, or pinned unresolved/fallback confirmation.
- Apply schedule edits at the layer the user selected. Single-occurrence edits must remain single-occurrence overrides unless the user turns them into a recurring rule.
- Preserve wall-clock date/time when editing time-zone-overridden courses. Editors must not round-trip occurrences through the machine-local time zone.
- A delete detail may offer a cancel-delete path that writes a single-occurrence local override and returns the occurrence to the preview.
- The top reset action must clear saved course schedule overrides, course presentation overrides, enabled optional task-generation rules, and resettable non-added local snapshot drift without hiding new parsed timetable additions.

## 7. Home and provider apply contract

Home is the provider-write apply surface.

Before provider apply:

1. build the local normalized preview;
2. read selected provider context when configured;
3. compare local occurrences against the previous local snapshot and app-managed remote items;
4. classify changes as Added, Updated, Deleted, metadata-only repair, exact match, unresolved, or unmanaged context;
5. let the user choose what to apply;
6. apply only selected effective changes.

Provider writes must:

- modify only app-managed items;
- scope local mappings to the selected provider destination calendar/list;
- never let a mapping saved for calendar `A` suppress or steer writes while calendar `B` is selected;
- perform destructive deletes only after explicit review;
- write deterministic add/update/delete batches for accepted changes;
- repair stale mappings by reusing a visible managed remote event for the same class and payload shape when safe;
- resurface previously accepted local occurrences as Adds when no saved mapping or matching managed remote event exists;
- carry the concrete managed remote event into delete plans so local snapshot deletes can remove the remote item instead of only updating the baseline;
- treat same-title/time/location matches from a different class as distinct work unless legacy metadata rules make an exact payload match safe to rebind;
- preserve remote color and time-zone metadata during read-back and recurring-instance reconciliation.

Import adoption and Home provider apply must not both write the same accepted additions. Import accepts the local baseline and refreshes Home; Home writes selected provider changes.

## 8. Provider status and contracts

### 8.1 Google Calendar

Google Calendar is the current supported timed-event provider.

Required capabilities:

- installed-app OAuth client JSON selected by the user;
- system-browser loopback authorization flow;
- user-scoped DPAPI token storage;
- persisted Google connection summary that is revalidated against token storage before startup or apply decisions;
- writable-calendar discovery including display color metadata;
- selected destination calendar persistence;
- preview reads that request timed event fields, private extended properties, time-zone metadata, recurrence/instance identity, and color ID;
- create, update, and delete for app-managed single events, recurring series, and recurring instances;
- exact recurrence writes that preserve skipped slots, using a shape such as weekly recurrence with `UNTIL` plus exclusions when needed instead of relying on a lossy count-only rule;
- metadata repair for managed events whose class/source/local identity drifted while the timed payload still matches;
- duplicate managed-event cleanup when multiple app-managed remote instances represent the same current occurrence.

Google app ownership must be trusted from private extended properties and local mappings. Ordinary description text may display managed metadata for human review, but it is not an ownership authority.

Google Calendar color and time-zone behavior:

- default event color comes from Settings and may use the selected calendar preset color;
- per-course and per-occurrence color overrides take precedence;
- `start.timeZone`, `end.timeZone`, `originalStartTime.timeZone`, and app-managed `timeZoneId` metadata must be preserved when available;
- equivalent regional IANA time-zone differences that do not change the lesson wall-clock time should not create ordinary update work;
- true time, location, title, note, color, or ownership metadata differences should remain visible.

### 8.2 Google Tasks

Google Tasks is optional and rule-based. Task rules are disabled by default and are distinct from timed calendar events. A task candidate must be previewed before apply and must not be treated as a calendar reminder substitute.

### 8.3 Microsoft

Microsoft Calendar and Microsoft To Do are planned targets. Infrastructure scaffolding may contain Microsoft auth, mapping, and payload builders, but the desktop workflow must not present Microsoft as a supported apply target until create/update/delete, read-back, token storage, mapping, and UI flows meet the same preview-first contract as Google.

Microsoft provider state must remain provider-specific. It must not reuse Google mappings, Google metadata names, or Google-only recurrence assumptions.

## 9. Settings contract

Settings must expose these areas:

- local source files for timetable PDF, teaching-progress XLS, and class-time DOCX;
- timetable resolution, including first-week start, time-profile default mode, explicit profile selection, and per-course time-profile overrides;
- provider connection/defaults, including Google OAuth JSON selection, connect/disconnect, writable-calendar refresh, selected calendar, default event color, and planned Microsoft fields where not presented as supported;
- program settings, including week start, language, regional IANA time zone, startup Google sync, status notifications, startup cloud loading, optional render caching, theme, network proxy mode, and About.

Settings selections must bind to stable persisted values, not localized display text or transient option object instances.

Network proxy settings apply only to provider HTTP clients. Local parsing and local persistence must never route through the app proxy. Proxy passwords must be stored outside JSON with user-scoped DPAPI protection.

Theme and language changes must apply immediately to the visible shell and active page. Text should flow through localization resources except for source-domain tokens and exact CQEPC examples.

## 10. Persistence and security contract

- Source files are referenced from user-local paths by default; private raw files are not repository assets.
- Missing source paths must be recoverable and visible, not startup-blocking.
- Preferences, snapshots, local source catalog state, sync mappings, and provider settings must be UTF-8 safe.
- OAuth client secrets, tokens, refresh tokens, tenant IDs, personal calendar IDs, local mappings, raw school exports, and proxy passwords must not be committed.
- Google tokens, proxy secrets, and optional Home render caches must be protected with user-scoped DPAPI where applicable.
- Stale plaintext protected-data files from older builds should be removed or ignored safely.
- Logs and diagnostics should avoid exposing personal schedule data where possible.
- Sanitized parser fixtures are allowed only when intentionally reviewed and documented as sanitized regression assets.

## 11. Localization and UI testability contract

- English is the default documentation and UI-development language; Chinese may remain where it is exact UI/domain copy, source tokens, or CQEPC examples.
- User-visible Import labels, fallback text, filter names, headings, and XAML copy must come from localization resources.
- Filter, grouping, sorting, and selection semantics must use stable keys or indexes rather than localized strings.
- WPF screenshot mode should render page roots inside the app process without relying on foreground desktop capture when possible.
- UIA/FlaUI smoke tests should prefer semantic interaction patterns over mouse-coordinate injection.
- Automation IDs that protect shell navigation, Home, Import, Settings, course editors, and provider controls should remain stable.

## 12. Definition of done

A change that affects behavior is not complete until:

- the intended layer owns the behavior;
- preview-first and provider-aware safety rules still hold;
- tests or documented validation cover the changed behavior;
- repository contracts are updated when behavior changes;
- long-form Wiki pages are updated when user/maintainer procedures change;
- source-file hygiene and secret-handling rules are preserved;
- no unresolved timetable item is silently exported;
- no unmanaged remote item can be updated or deleted by app apply.
