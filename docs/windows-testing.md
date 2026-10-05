# Windows 10/11 installer test guide

Use the **full Setup** executable on each independent test PC. The **Client** executable only installs a front-end for an already configured host. The initial full package requires x64 Windows 10/11 and .NET Framework 4.8 or a compatible later in-place 4.x. It bundles modern host/setup runtimes, PostgreSQL and the signed Visual C++ prerequisite. No SDK or separately installed PostgreSQL is required.

## Download and install

The [initial 0.1.1 package and executed-check record](windows-installer-evidence.md) documents the installation baseline. The operator UX update is tracked in [#11](https://github.com/modhack2003/AK_REPORT/issues/11); use its latest successful 0.2.0 package for the redesigned interface. See the [operator guide](operator-guide.md).

1. Download the `windows-installers` artifact from the successful **Windows installation package** GitHub Actions run. Extract the ZIP.
2. Verify the executable's SHA-256 against `SHA256SUMS.txt` (PowerShell: `Get-FileHash .\AK-Reporting-Setup-*.exe -Algorithm SHA256`). The build is an unsigned engineering installer; publisher/signing acceptance is pending.
3. Run `AK-Reporting-Setup-<version>-win-x64.exe`. Approve its administrator/UAC request.
4. Keep **Run first-time setup / repair local services** selected on the finish page. Enter your own separate administrator/writer account names and passwords, optionally upload your AK logo, and click **Initialize / resume setup**. For an existing installation choose **Repair existing setup**; existing accounts/reports remain.
5. Wait for **Ready**. Close the elevated setup utility. Launch **A K Diagnostic Reporting** from the desktop/Start menu as a normal Windows user.
6. The logo splash fades to a common login page. Keep endpoint `https://localhost:7043` under Connection settings. Writers and administrators can prepare reports; administrators also get Doctors & settings. Choose New report, enter name/date, tick tests, Continue to results, then Save & preview. Optional details can be blank.

The database/API run as services and should start after a reboot. The local package binds only loopback; it does not expose a server port to the LAN or alter firewall rules. Internet can be disconnected after download. No database credentials belong in the endpoint field.

## Test and report

Use clearly synthetic names/results. Do not attach actual patient reports or real doctor signatures to GitHub.

| Test | Record on both Windows 10 and 11 |
|---|---|
| Installer | OS edition/build, x64 architecture, artifact name/hash, setup completes without SDK/internet |
| First run | Administrator and writer login; incorrect password; draft candidates available |
| Branding | Upload supplied logo in setup/repair; ~1 second splash/fade/login; logo retained after repair |
| Minimal input | Name/date/test only; no raw ISO timestamps; blank age/sex/ID/history/sample accepted; no case created with missing name/test |
| Doctor library | Administrator adds/selects/updates doctor details and PNG assets; writer selects new version; older report signature/hash retained |
| Multi-investigation | One synthetic case containing CBC/LFT/Urine/Histo/ECG; each saves/exports independently |
| Results | Numeric/text/multiline entries, long names/notes, empty optional fields, µ/α, controlled formatting |
| Revision | Save v1, correct with a reason, inspect prior revision, verify stale/reload handling |
| Documents | PDF/DOCX export, readable text, page count, no clipping, draft marker, multiple pages |
| Printer | Printer/driver name, A4 at 100%, physical top/bottom letterhead measurements, repeats/cancel/offline |
| Restart | Close/reopen client; restart host; reboot Windows; saved case/revisions still accessible |
| Offline | Disconnect internet and repeat creation, correction, PDF export and printing |
| Repair | Run Start menu Local Setup and Repair; existing account/report IDs and historical PDF hashes remain |
| Uninstall/reinstall | Uninstall removes services/client, preserves ProgramData; reinstall + Repair restores access |
| Complete removal | Choose Remove everything; local services, certificates, binaries, database/accounts/config/logo removed; external exports retained |

Return screenshots with synthetic data only, exact failing steps, OS build and displayed error/stage. Put installation problems in [#9](https://github.com/modhack2003/AK_REPORT/issues/9), OS/printer measurements in [#4](https://github.com/modhack2003/AK_REPORT/issues/4), and clinical configuration work in [#5](https://github.com/modhack2003/AK_REPORT/issues/5).

## Troubleshooting

* **Missing Framework 4.8:** install Microsoft's official offline prerequisite for that Windows edition, then retry. Most current Win10/11 PCs already have compatible 4.x.
* **First-run error:** note the current stage. Check Windows Services for `AKReportingDatabase` and `AKReportingHost`, available disk space, and port conflicts on 55432/7043. An administrator can read `%ProgramData%\AK Diagnostic Reporting\administration\setup-status.log`; it records stage/error type and tool exit codes, not credentials or clinical values. Database initialization errors can also include `initdb` filesystem diagnostics from before application data exists.
* **Interrupted initial account creation:** run Initialize/resume with the original administrator password and desired writer details. Repair without credentials is for a completed installation.
* **Login cannot connect:** confirm both services are running and `https://localhost:7043/health/live` works with normal certificate trust. Never bypass TLS validation.
* **Existing DB/configuration mismatch:** preserve ProgramData and use reviewed restore support. Setup refuses to reinitialize a nonempty unmatched cluster.
* **DOCX fonts:** Noto fonts are embedded in PDFs/WPF; Office can substitute a font if not installed. Use PDF as printing authority. Office output/printing requires separate acceptance.
* **Unsupported script:** Bengali and other unvalidated shaping/font combinations fail explicitly. Record the required script; do not replace medical text silently.

Default uninstall keeps `%ProgramData%\AK Diagnostic Reporting`. **Remove everything** explicitly deletes this local data, accounts, logo and configuration as well as application files/services/trust. Exports/backups in other chosen folders are retained. Protected configuration is DPAPI-bound to this Windows installation. A directory copy alone is not a valid move/restore procedure; see `backup-restore.md`. Do not delete data merely to make an installer retry succeed.
