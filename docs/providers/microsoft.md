# Microsoft Provider Contract

Microsoft Calendar and Microsoft To Do are planned provider targets. This document defines the boundary that scaffolding must follow until the desktop workflow supports Microsoft apply.

## 1. Current status

Infrastructure may contain Microsoft authentication, Graph client, payload builder, token cache, and mapping factory scaffolding. The desktop workflow must still present Google Calendar as the only supported timed-event apply target until Microsoft read-back, preview, create/update/delete, mapping repair, token storage, and UI flows satisfy the same preview-first contract.

## 2. Provider separation

Microsoft behavior must remain provider-specific:

- no reuse of Google mapping stores;
- no reliance on Google private extended property names;
- no Google-only recurrence assumptions;
- no Google Calendar color IDs or Google Tasks concepts in Microsoft payload decisions;
- no shared destructive ownership checks that cannot be represented safely in Microsoft Graph.

Application-level provider abstractions may be shared, but Infrastructure must translate them into Microsoft-specific payloads, metadata, and limitations.

## 3. Planned calendar capability contract

Before Microsoft Calendar becomes supported, it must provide:

- public-client authentication suitable for a local Windows app;
- DPAPI-protected token cache storage;
- connection-state validation at startup and before apply;
- writable calendar discovery;
- selected destination calendar persistence;
- preview reads for the selected calendar and preview window;
- provider-safe app ownership metadata;
- create, update, and delete for app-managed timed events;
- recurrence and instance handling that preserves the exact local occurrence set;
- mapping storage scoped to provider and destination calendar;
- metadata repair and stale-mapping recovery where Microsoft Graph supports it;
- failure handling that reports per-change results without corrupting local mappings.

## 4. Planned To Do capability contract

Microsoft To Do support is optional and rule-based, like Google Tasks. It must remain separate from timed calendar events and must be previewed before apply.

Before support is exposed, the app must provide:

- task-list discovery;
- selected destination task-list persistence;
- create/update/delete for app-managed task items;
- provider-specific reminder/category behavior;
- mapping storage scoped to provider and task list;
- clear UI that tasks are follow-up items, not exact timed lesson reminders.

## 5. Security

Do not commit client secrets, tenant IDs, tokens, refresh tokens, personal calendar IDs, task-list IDs, or local Microsoft mapping stores. Token caches must be user-local and protected where possible.

## 6. Documentation rule

When Microsoft behavior moves from scaffolding to supported workflow, update:

- [SPEC.md](../../SPEC.md);
- [README.md](../../README.md);
- this provider contract;
- the GitHub Wiki guide index and any provider-sync guide linked from it;
- tests for Microsoft auth, payloads, read-back, mapping, and apply failure paths.
