# A K Reporting — quick operator guide

## Sign in

The application shows your supplied centre logo for about one second, then fades to the login page. Administrators and report writers use the **same login page**. Enter the username/password chosen during setup. The local endpoint is `https://localhost:7043`; it is tucked under **Connection settings**.

## Prepare a report

1. Choose **New report**.
2. Enter **Patient name** and choose **Report date**. Today's date is already selected.
3. Tick one or more tests, then choose **Continue to results**. Each test is a separate report; the first opens automatically.
4. Enter the available results. Leave unknown/unused inputs blank. Age, patient ID, sex, referring doctor, notes, specimen, method, technician and collection date are under **Optional details** and may be left blank. A blank age does not send an age unit.
5. Optionally choose an administrator-saved doctor under **Doctor signature / stamp**. Choose **Refresh doctor library** if the administrator has just updated it.
6. Choose **Save & preview**. Then **Export PDF**, **Export Word**, or **Print A4**. PDF is the printing reference.

An ordinary draft save does not require a save note. A correction to an issued report still needs the actual correction reason. Final issue requires a medically approved template, actual authorizing doctor and authorization evidence; this is separate from creating, saving and exporting a draft.

**Unsaved changes** is shown when editing. Save before closing or switching to another report. A failed host request keeps your entries; after an uncertain save, use **Reload latest saved version** to check the stored revision. Session lock/sign-out clears protected content.

## Open a saved report

Search by patient name, ID or report number on the left. Select the patient, then select the report below. Each report has its own saved revisions. Use **Revision history / correction** to inspect history or reload the latest saved version.

## Administrator: doctors and signatures

Administrators can prepare reports with the same workflow as writers. Choose **Doctors & settings** to manage the doctor library:

* **Add doctor**, enter the actual name and supplied details, and record the actual permission to use the details/images.
* Upload a **signature PNG** and/or **stamp PNG** (each up to 2 MiB). The images are previewed before saving. Both are optional.
* Choose **Save doctor**. Writers can select that doctor for a report.
* To update, select the doctor from the list, edit the details or replace an image, and save. No UUID needs to be typed. Existing images are kept unless replaced or removed.
* Each update creates a new version. Previous reports retain their original pinned details, signature and stamp. An inactive doctor is unavailable for new selection.

Less frequent user/template/centre controls are under **Users, configuration and centre settings**.

## Supply or change the AK logo

In first-run setup, choose **Upload your AK logo…** and select a PNG/JPEG up to 2 MiB and 4096 × 4096 pixels. This is optional; without it the centre name is shown. The supplied image is used on startup, login and the workspace header. It does not change report letterhead geometry.

For an existing installation, open **Local Setup and Repair**, upload the logo and choose **Repair existing setup**. Restart the reporting client. Repair preserves accounts, reports and the existing logo when no replacement is selected.

## Uninstall

Open the normal Windows uninstall entry. The full installer offers:

* **Remove application only — keep reports for reinstallation**: removes binaries/services/certificate trust while retaining the local database, accounts, configuration and logo.
* **Remove everything**: permanently removes this installation's binaries, database, accounts, logo, protected configuration, services and its HTTPS certificate/key/trust. Use this only when you intend to erase the local installation.

Files exported or backed up to your own chosen folders remain there. Shared Windows prerequisites are not removed. Client-only uninstall does not erase a separate host's reports. Administrator/scripted complete removal uses `Uninstall.exe /S /PURGE`; normal `/S` preserves data.

These are engineering test builds. Use synthetic patient data for acceptance. The five candidate configurations remain drafts pending qualified centre review.
