# Windows installer engineering evidence — 2026-10-05

Issue: [#9](https://github.com/modhack2003/AK_REPORT/issues/9). Review: [PR #10](https://github.com/modhack2003/AK_REPORT/pull/10), branch `agent/astra/windows-installation-20261005`, based on `main` at `c42ecd5`. This branch is published for review; the package is not yet merged into `main`.

## Verified download

* Package version: **0.1.1**, `win-x64`.
* Tested source: `9edbe03adb4ff92a83e6996d839279cf28630daf`.
* [Successful build/install run](https://github.com/modhack2003/AK_REPORT/actions/runs/37299716853).
* [Download `windows-installers` ZIP](https://github.com/modhack2003/AK_REPORT/actions/runs/37299716853/artifacts/11341276925) (GitHub sign-in may be required).
* A second independent [PR package run](https://github.com/modhack2003/AK_REPORT/actions/runs/37299720248) passed the same checks.
* Downloaded artifact contents: full Setup (~100 MiB), Client (~1.6 MiB), `README-FIRST.txt`, `package-manifest.json` and `SHA256SUMS.txt`. All four manifest-listed files passed `sha256sum --check SHA256SUMS.txt` after downloading.

SHA-256 for these exact artifacts (other workflow builds can have different hashes):

```text
0082cd34fba17286aebb9f19be54ee510d98b51964c147e7fa7a3a9dd2421829  AK-Reporting-Setup-0.1.1-win-x64.exe
c2f8ae50227c6c99941a2dd15ada005c1e84952acb183c002d7913bba38a99a5  AK-Reporting-Client-0.1.1-win-x64.exe
```

Use **Setup** on each independent Windows 10/11 test PC. See [installation and acceptance steps](windows-testing.md). The Client installer expects a separately configured trusted HTTPS host.

## Executed checks

The hosted runner was **Windows Server 2022, build 10.0.20348**, image `windows-2022`, SDK 10.0.401. The package includes self-contained .NET 10.0.12 host/setup, PostgreSQL 17.11-1, and the Microsoft-publisher-verified Visual C++ prerequisite; compiler NSIS 3.11 and PostgreSQL archives were checked against pinned hashes. The WPF client remains .NET Framework 4.8.

| Check | Result |
|---|---|
| Full and client-only NSIS package compilation | Passed |
| Clean full installation and first-run provisioning | Passed |
| Chosen administrator/writer accounts and five draft candidates | Passed |
| Installed WPF executable creates its reporting workspace | Passed |
| Trusted localhost HTTPS without certificate-validation bypass | Passed |
| Database/API run as separate LocalService services | Passed |
| DPAPI configuration and secret-file/service-private-key NTFS ACLs | Passed |
| CA private key discarded; temporary initialization password removed | Passed |
| Writer login, synthetic case/report and PDF download | Passed |
| Service restart and repair preserve revision identity, PDF hash and owner credentials | Passed |
| Tampered DPAPI configuration rejected, then repaired after original ciphertext restored | Passed |
| Uninstall removes owned services, HTTPS key and trust while preserving database/configuration | Passed |
| Reinstallation/repair restores login and the saved report revision | Passed |
| Client-only installation/uninstall leaves retained server data intact | Passed |

[Engineering verification](https://github.com/modhack2003/AK_REPORT/actions/runs/37299716820) also passed the existing **28 domain/rendering/API/real-PostgreSQL tests** and hosted Windows net48 build. Setup/API locally compiled with zero warnings/errors during the fixes.

Installation defects found and corrected during validation: binary dependency download handling, PowerShell helper recursion, restricted `initdb` token access to temporary credentials, Windows Schannel persisted-key requirements, collection-valued PDF headers and the desktop startup readiness wait. No transport contracts or applied migrations were changed, and the desktop navigation work in PR #8 was not included.

## Pending acceptance

Actual Windows 10/11 editions/builds, normal-user UI operation, reboot/offline operation and physical printer/letterhead output require user testing under [#4](https://github.com/modhack2003/AK_REPORT/issues/4). Windows 7 client compatibility remains a separate requirement. The installers are unsigned engineering builds. Center-specific medical approval under [#5](https://github.com/modhack2003/AK_REPORT/issues/5), signing, restore/rollback and the other [release gates](testing.md) remain pending. All five candidate schemas remain drafts.

No smoke database, credentials, private keys or patient records are included in the artifact. Machine-bound DPAPI configuration is retained for same-machine repair; it is not a cross-machine backup.
