# Desktop navigation and revision recovery

Tracked in [issue #7](https://github.com/modhack2003/AK_REPORT/issues/7), a scoped part of [v1 hardening #3](https://github.com/modhack2003/AK_REPORT/issues/3). Base: `c42ecd5`. Work branch: `agent/astra/desktop-navigation-recovery-20261005`.

## Operator behavior

* Refresh, search, older-page navigation, case selection and report selection fetch the destination before replacing the current editor. Report loads also resolve their pinned template before replacement. A failed load retains the active case/report, revision context, raw editor input, reasons and existing preview, and restores the list selection to the active record.
* A successful destination load asks before discarding unsaved entries. The prompt includes save/issue reason and authorization-basis text, and defaults to **No**. Declining retains the original editor. Navigation does not parse or normalize unfinished input.
* Failed older-page requests retain the current offset. Retrying requests the same next page. Paging follows the last successfully loaded search, even if new search text has been typed.
* **Load latest saved revision** reads the current saved revision with the existing API. It can be used after a stale-save conflict or a timeout with an uncertain save outcome. A failed reload retains input; a successful reload replaces it only after confirmation. Review the saved record and explicitly re-enter any intended correction with a reason.
* History can be fetched while an editor contains unsaved input. Selecting a historical revision follows the same replacement confirmation. A new report load, save or issue clears the previous history list; historical viewing does not assert that an old revision is the latest.
* Session lock invalidates in-flight navigation, save, issue and history responses. A delayed response or a pending discard dialog cannot repopulate the locked editor. Window close also invalidates pending replacements.

These changes use the existing HTTP API and transport contracts, preserve net48/Windows 7 SP1 targeting, and require no database migration. Clinical configuration, historical revisions and pinned template/signature versions retain their existing semantics.

## Executed engineering checks — 2026-10-05

| Check | Result |
|---|---|
| Baseline Release server build | Passed; zero warnings/errors |
| Baseline tests using isolated PostgreSQL 17 | 28 passed; zero failures/skips |
| Updated Release tests using isolated PostgreSQL 17 | 36 passed; zero failures/skips |
| Recovery regression coverage | Eight checks: disconnect/timeout/pinned-template load failure, pending load and declined replacement, paging retry, explicit latest reload, and session lock during fetch or confirmation |
| net48 WPF Linux cross-build | Passed; zero warnings/errors |

The test project compiles the actual desktop `WorkspaceNavigation` helper as linked source. Tests inject failed/delayed destination loads and verify editor/selection/revision retention and session invalidation. They do not execute WPF controls or printer drivers. Synthetic rendering goldens continue to pass without a baseline update.

## Windows/manual acceptance still required

Use synthetic records in an isolated engineering installation and record the exact Windows build and client artifact hash:

1. Edit a report, including unfinished numeric/date text and reason/authorization text. Disconnect the host, then attempt refresh/search/page/case/report navigation. Confirm all entries remain and the visible selection returns to the active record.
2. Restore the host and repeat navigation. Decline replacement; confirm input and revision context remain. Accept replacement; confirm the selected saved record and its pinned template/doctor are displayed.
3. Fail an older-page request, reconnect and retry. Confirm the same page is requested, with the last successfully loaded search query.
4. Save a correction from a second writer. Attempt a stale save from the first client. Fetch history without losing input, then use **Load latest saved revision**. Verify both decline and accept paths, and explicitly re-enter the intended correction on the latest revision.
5. Interrupt the response to a save after submission. Review the latest saved revision to determine whether it committed; confirm no automatic duplicate save or merge occurs.
6. Switch between reports and historical revisions. Confirm the report title, selected history entry, results, metadata and pinned attribution stay associated with the same report.
7. Expire/lock the session while a load is pending and while the discard dialog is open. Confirm the protected editor stays cleared when the response/dialog completes. Check close during a pending request as well.

Actual Win7 SP1/Win10/Win11 runtime and physical-printer acceptance remain tracked in [issue #4](https://github.com/modhack2003/AK_REPORT/issues/4). Input retention here is in-memory while the authorized workspace remains open; crash recovery, protected restart recovery and the remaining operator lifecycle work still require their own scoped implementation. Release remains engineering validation in progress under `docs/testing.md`.
