# Operator guide

## Sign in and start a case

Use the configured local/LAN host URL and your own account. Session tokens stay in memory; idle timeout requires sign-in again. Server availability on the local PC/LAN is required, but internet is not required for report operation.

In **Patient / specimen**, enter the patient's name and local ID, explicitly reported age and unit, sex as recorded, referrer and relevant specimen/history. Enter date/time with an explicit offset, e.g. `2026-01-02 10:15 +05:30`; the saved value is an instant, not an assumed timezone. Leave unavailable optional data empty.

Select one or more investigations and choose **Create case + selected reports**. CBC/LFT/urine/histopathology/ECG are saved as independent draft reports. Select the case/report on the left to enter results. A receptionist can create minimal cases but cannot access results/signatures.

Use **New case** to clear the workspace for another patient. Search saved cases by name, local patient ID or public report number; **Older** pages through saved cases. Search terms are sent in request bodies rather than placed in logged URLs.

## Results and attribution

Fields come from the pinned schema. Enter values manually; decimal numbers use a dot and no thousands separators. Numeric comparisons are available only where explicitly configured. The application does not calculate normality, infer negatives, calculate ECG interpretation or supply normal ranges.

Units/reference text are read-only schema configuration. Select the center-authorized doctor version; enter the actual technician attribution separately. The account writing the report is recorded in the audit, not credited as a technician by assumption. Selecting a doctor is not proof of personal review.

## Save, correct and issue

Every save requires a reason and creates a new revision. If another writer saves first, your stale save is rejected; preserve/reconcile your unsaved entries and reload the latest revision. Do not repeatedly recreate the case to handle a network error.

Clinical issue requires an approved template, selected actual doctor version, required fields and recorded center authorization basis. Draft candidate schemas cannot be issued. A correction to an issued report produces a new draft and must be explicitly issued again. Old versions remain intact.

History allows loading old revisions for preview/export. Load the latest revision before correcting. Updating a doctor/template profile never changes an already saved historical revision or generated document.

## Output

Save the current changes first. **Preview saved revision** shows the server's measured A4 page plan. PDF is the primary document; DOCX is secondary and may reflow in different office software. Export to an approved protected folder. DOCX edits outside the application are not database corrections.

**Direct A4 print** uses the fixed page plan and the local Windows print queue at 100%. The application checks A4 portrait/imageable-area constraints. Verify the paper output and pad alignment. Completion of the print dialog/spool action does not prove physical printing; resolve paperout/offline conditions before repeating.

## Administration and review

Administrator controls create users, install/import draft schemas, add actual doctor/signature/stamp versions and update numbering/retention settings. Existing doctor UUIDs create new profile versions. Signatures/stamps must be authorized PNG assets with permission evidence.

MedicalReviewer records actual qualified configuration review and publishes a separate approved version. This permission must be assigned according to the center's actual process. Template definitions are developer-maintained JSON; there is no Word-like designer.

The current workspace is a compatibility/engineering increment. Clinical samples, supported scripts, real printer/pad acceptance, recovery evidence and final operator usability acceptance are pending.
