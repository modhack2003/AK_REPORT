A K Diagnostic Reporting — Windows engineering test package

For Windows 10/11 x64. .NET Framework 4.8 (or later compatible 4.x) is required.

1. Run AK-Reporting-Setup-*-win-x64.exe and approve the administrator prompt.
   The package includes its own API runtime, PostgreSQL binaries and Visual C++ prerequisite.
   No internet is needed while installing or reporting.
2. At the finish page, run first-time setup. Choose separate administrator and writer
   usernames/passwords (12–256 characters). Click Initialize / resume setup.
3. Wait for Ready. Close setup and open the A K Diagnostic Reporting desktop shortcut
   normally (not as administrator). Keep https://localhost:7043 in the endpoint field.
4. Sign in as the writer, create a synthetic patient case, select investigations,
   enter results, save a revision, preview and export PDF/DOCX. Test your printer.
5. Sign in as the administrator for user/doctor/draft-template maintenance.

Use invented software-test data only during acceptance. The bundled schemas are DRAFT;
no units, reference intervals, diagnoses, professional credentials or clinical approval
are supplied. Do not approve templates merely to bypass the draft gate.

Data: C:\ProgramData\AK Diagnostic Reporting
Services: AKReportingDatabase and AKReportingHost
API: localhost HTTPS port 7043. Database: loopback port 55432.
No automatic LAN exposure. The Client installer is for an existing separately configured
trusted HTTPS LAN host; use the full Setup package on each independent test PC.

Uninstall removes services/application files but preserves the database and encrypted
configuration. Reinstall in the original folder and choose Repair existing setup.
DPAPI secrets are machine-bound: use PostgreSQL backup/restore for moving computers.

The engineering installers are not yet code-signed; verify SHA256SUMS.txt against the
downloaded artifact. Windows may display an unknown-publisher prompt. Code signing
is required before a production release; do not disable Windows security features.

If setup fails: note the displayed stage, check Windows Services and port conflicts,
then run Local Setup and Repair again. Do not delete/reinitialize the saved database.
An administrator can read administration\setup-status.log (stage/error type, tool exit
codes and initialization diagnostics; no credentials or report values).
Report issues without patient names, signatures, passwords or private keys.

Read docs\windows-testing.md for the acceptance checklist and docs\backup-restore.md
for controlled backup/restore. Feedback belongs in GitHub issue #4 or #9.
