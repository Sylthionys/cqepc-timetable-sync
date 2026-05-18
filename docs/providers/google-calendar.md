# Google Calendar Provider Contract

Google Calendar is the current supported provider for timed course events.

## 1. Availability

The desktop workflow may expose Google as a supported sync target when these conditions are met:

- the user selects a Google installed-app OAuth client JSON;
- the app completes the system-browser loopback authorization flow;
- token storage is available through user-scoped DPAPI;
- writable calendar discovery succeeds;
- the user selects a destination calendar.

Persisted account summaries or selected calendar IDs are convenience state only. Startup, preview, and apply must re-check token availability and clear stale provider state when the token store is no longer valid.

## 2. Destination metadata

Writable calendar refresh should preserve:

- calendar ID;
- display name;
- primary-calendar flag;
- display background color;
- Google calendar color ID when available.

When `CalendarListEntry.backgroundColor` is missing but a `colorId` exists, the adapter may resolve the swatch through the Google Calendar color palette. Settings should use the selected calendar's preset color for the `Preset color` event-color option rather than a hardcoded fallback.

## 3. Ownership and mapping

Google provider ownership is trusted from:

- Google Calendar private extended properties written by the app;
- local Google sync mappings scoped to provider and destination calendar.

Ordinary event description text is not ownership authority. It may contain human-readable managed metadata for review, but it must not drive destructive update/delete decisions.

Mappings must be scoped to the selected calendar. A mapping saved for calendar `A` must not suppress Adds or steer Updates/Deletes while calendar `B` is selected.

## 4. Managed event metadata

App-managed Google Calendar events should carry private metadata sufficient to rebind and repair sync state:

- app-managed marker;
- local stable sync ID;
- source fingerprint;
- source kind;
- class name when available;
- recurrence/instance identity where needed;
- declared time-zone ID when needed for read-back consistency.

Older managed events may lack some metadata. A legacy event can be reused when the full timed payload matches safely, but apply should backfill missing metadata so future previews can use stricter matching.

## 5. Preview/read-back

Google preview reads selected-calendar events in the preview window and maps timed events into provider preview models.

Read-back must preserve:

- event ID and recurring master/instance identifiers;
- title;
- start/end instants;
- `start.timeZone` and `end.timeZone` when Google returns them;
- `originalStartTime.timeZone` for recurring instances;
- private extended properties;
- location;
- description;
- `colorId`;
- deleted/cancelled state where relevant.

When Google omits expanded recurring-instance `start.timeZone` / `end.timeZone`, the adapter may use app-managed `timeZoneId` metadata or `originalStartTime.timeZone` as the declared zone. Equivalent regional IANA zone differences that do not change the lesson wall-clock time should not be ordinary update work.

Unmanaged Google events may be shown as neutral current-calendar context. They are not selectable Import changes and cannot become destructive apply targets.

## 6. Diff behavior

Google-aware diffing should classify:

- local occurrences that need remote creation;
- managed remote events that need title/time/location/description/color/time-zone updates;
- managed remote events that need metadata repair;
- managed remote events that should be deleted because the local occurrence disappeared;
- exact managed matches;
- unmanaged context rows.

A previously accepted local snapshot does not prove that Google still contains the event. If an occurrence has no valid mapping and no matching managed remote event, preview must surface it as an Add so apply can repair the missing remote write.

If a saved mapping points to a stale remote ID but preview sees another managed event for the same class and payload shape, preview should reuse that event rather than create a duplicate.

If multiple managed remote events represent the same current occurrence, convergence should delete the extra managed duplicates when safe. Location drift alone should not block duplicate cleanup when the current timetable only needs one event.

## 7. Apply behavior

Google apply must write only selected effective changes for the selected destination calendar. The apply path should execute deterministic write batches and return per-change results plus updated mappings.

Required write behavior:

- create single timed events;
- create recurring series where the export group can be represented losslessly;
- update single events and recurring instances;
- delete managed single events, recurring masters, and represented instances;
- repair stale metadata and mappings;
- clear child mappings when a recurring master is deleted;
- fall back to exact single-event writes if a recurring-series insert leaves expected instances missing.

Recurring writes must preserve the exact accepted occurrence set. A sparse or drift-repaired recurrence must not rely on a lossy `COUNT`-only rule that can silently drop later lessons. A weekly rule with `UNTIL` plus exclusions is acceptable when it exactly represents the local export group.

## 8. Time zones and colors

Effective event time zone comes from this priority order:

1. occurrence-level explicit override;
2. course-level presentation override;
3. Program Settings Google Calendar default IANA time zone;
4. safe provider fallback.

Default configuration should resolve to an IANA region such as `Asia/Shanghai`, not only an offset label.

Effective Google Calendar color comes from this priority order:

1. occurrence/course override;
2. provider default event color;
3. selected calendar preset color when the user chooses `Preset color`;
4. provider default behavior.

Color-only drift is still provider payload drift and should surface as an update unless already converged.

## 9. Google Tasks

Google Tasks is optional and rule-based. Task rules are disabled by default. Tasks are separate from timed calendar events and must be previewed before apply. Google Tasks support must not be treated as a replacement for precise Google Calendar reminders.

## 10. Security

- Do not commit installed-app OAuth client JSON files.
- Do not commit tokens, refresh tokens, provider mappings, personal calendar IDs, or local account summaries.
- Token storage must use user-scoped DPAPI.
- Provider HTTP clients may use the configured network proxy, but local parsing and local JSON persistence must not.
