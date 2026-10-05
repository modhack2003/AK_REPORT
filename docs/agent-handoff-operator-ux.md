# Agent handoff — operator UX, branding and complete removal

Task: [#11](https://github.com/modhack2003/AK_REPORT/issues/11). Branch: `agent/astra/operator-ux-20261005`. Base: `origin/main` at `18f8d6b`, after installer PR #10 merged. The user installed 0.1.1 and reported usability blockers; this task is the 0.2.0 operator-facing revision.

## Earlier delivered work

The five-report foundation and offline Windows installer are integrated into `main`. They provide structured CBC/LFT/Urine Routine/Histopathology/ECG drafts, append-only revisions, pinned template/doctor versions, shared measured A4 PDF/DOCX/preview/print, PostgreSQL, authenticated HTTP-only net48 WPF, protected local HTTPS services and data-preserving repair/uninstall. Clinical/printer/OS acceptance remains pending.

## Changes in this task

* Imported the published recovery checkpoint `6d044d6` from PR #8 into this feature branch as `ed67ba6`. Preserve `WorkspaceNavigation`, its session invalidation and eight regression tests. PR #8/shared main were not rewritten.
* Added a ~1 second splash with a short fade, logo/centre identity and one common login page. Connection details are under an expander.
* Reorganized reporting into **Patient & test → Results → Preview/export**, with saved-patient/report navigation, labelled controls, optional-detail groups, help, busy feedback and visible saved/unsaved state.
* Minimal creation requires patient name, a report date and a test selection before case creation. DatePicker replaces ISO-offset typing; unchanged historical dates retain their original timestamp/offset. Blank age sends no unit. Other metadata is optional. New reports open results directly; save/preview is a single action.
* Administrators now have all writer permissions in the domain and reporting routes and see the same reporting workspace. Qualified medical-template approval remains the MedicalReviewer action.
* Draft save notes may be blank; the audit reason becomes `Draft updated`. Issued-report corrections and final issue still require their actual reason/evidence.
* Added selection-based administrator doctor management, image previews, new/update versioning, asset retention/removal and active/inactive actions. No typed doctor UUID. Added administrator-only `GET /doctors/versions/{id}` for existing assets; writers still get only the asset-free list and use pinned IDs for rendering.
* First-run/repair accepts a supplied PNG logo (UI also converts JPEG), bounds size/dimensions, stores it in a public read-only branding folder, and leaves secret ACLs restricted. No actual logo or clinical images are committed. Logo is UI branding, not a change to historical report layouts.
* Full uninstall offers data preservation or explicit permanent removal. `/PURGE` removes owned ProgramData, binaries, services and HTTPS key/trust; it rejects linked paths and another install root. Ordinary uninstall/reinstall still preserves data. External exports/backups are not searched/deleted.

## Files / ownership

* Desktop: `MainWindow.xaml(.cs)`, `App.xaml`, `MetadataEditor.cs`, `ResultEditor.cs`, `CatalogPanel.cs`, new `DoctorLibraryPanel.cs`, `ApiClient.cs`.
* Permissions/draft notes: `src/AkReporting.Domain/Errors.cs`, `src/AkReporting.Application/ReportService.cs`, API report/catalog routes.
* Setup: `Branding.cs`, `SetupForm.cs`, `InitialAccounts`/`Provisioner`, setup CLI, NTFS branding traversal; NSIS uninstall and package 0.2.0.
* Checks: new API acceptance case in `ApiTests.cs`, actual installed UI automation in `packaging/windows/Test-Desktop.ps1`, extended installation/branding/purge smoke; synthetic-only UI preview artifacts.
* Operator instructions: `docs/operator-guide.md`, Windows installation/testing docs and bundled README.

## Compatibility / data impact

No SQL migration or transport-contract change. The new doctor asset-read route is additive and administrator-only. Administrator reporting is an intentional permission expansion requested by the user. Historical report/template/doctor data remains pinned and append-only. Desktop still targets .NET Framework 4.8/Win7 compatibility; host/setup remain separate .NET 10. No medical units/ranges/rules, real credentials, real signatures or clinical approval are seeded.

## Verification state

Executed locally: net48 WPF, setup and server Release builds passed with zero warnings/errors; **37 tests passed, 0 skipped**, against an isolated real PostgreSQL 17 container. This includes the imported eight navigation tests and administrator/minimal-input/doctor-version/pinned-document regression case.

Windows package checks run `Test-Installation.ps1` and `Test-Desktop.ps1`: real installed login, administrator reporting, missing-name/test validation, minimal-input creation, blank optional fields, draft save/preview, doctor editor, supplied logo, repair, retained-data reinstall and complete removal. They also produce synthetic-only screenshots for visual inspection. Hosted results/download links will be recorded in this task's PR/issue as they complete. Do not claim real Win10/11, Win7 or physical-printer acceptance from a hosted build.

## Next agent / user acceptance

Read AGENTS.md and required architecture/medical/compatibility/collaboration docs. Fetch the remote and inspect task #11 before editing these owned paths. Consult `docs/operator-guide.md` for intended behavior. Test the new package on the user's actual Windows 10/11 machines, including training a new writer, uploading the actual supplied AK logo locally, signature/stamp replacement, optional blanks, multiple investigations, revision recovery and both uninstall choices. Record only synthetic screenshots in GitHub. Clinical review stays #5; hardware/printer evidence stays #4.
