# Windows installation package — issue #9

Base: `main` at `c42ecd5`. First package targets **Windows 10/11 x64** for local engineering acceptance. The WPF executable remains net48; the host/setup utility ship self-contained .NET 10. This packaging does not change the Windows 7 client runtime decision.

## Package contents and topology

* NSIS setup executable, compiled from repository source; no network downloads during installation.
* Client: net48 WPF, fixed fonts/license, shortcuts, prerequisite check for Framework 4.8.
* Host: self-contained win-x64 .NET 10, Windows-service integration, HTTPS `https://localhost:7043` only.
* PostgreSQL: pinned official EDB PostgreSQL 17 Windows binaries, bundled licenses, isolated cluster on `127.0.0.1:55432`, SCRAM authentication.
* First-run setup: elevated Windows utility, explicit administrator/writer credentials entered in UI; installs draft candidates only. No seeded passwords or medical approval.
* Smaller client-only package: WPF and documentation, for a separately configured HTTPS LAN host. Local package does not open LAN/firewall access automatically.

Installed binaries: `%ProgramFiles%\AK Diagnostic Reporting`. Persistent database/configuration: `%ProgramData%\AK Diagnostic Reporting`. Data is outside the uninstallable application directory.

## Credentials, TLS and services

The utility generates unique PostgreSQL owner/runtime secrets and a machine-specific local certificate authority/server certificate. Only the public root enters the local-machine trusted store; the CA private key is discarded. Runtime secrets and server PFX are Windows DPAPI machine-protected, with NTFS ACLs restricted to administrators/SYSTEM and the API service SID. Windows Schannel requires a persisted server private key: elevated setup installs the leaf certificate in LocalMachine/My and restricts its machine-key file to administrators/SYSTEM and read access for the API service SID. Other LocalService processes receive no key-file grant. Uninstall removes the leaf/key and public trust anchor; repair recreates them from protected configuration. Owner credentials have separate administrator-only ACLs. No private key/password/database dump enters source or CI artifacts.

`AKReportingDatabase` and `AKReportingHost` run as LocalService with enabled service SIDs and separate folder grants. The runtime login uses the existing `grant-runtime.sql`; it is not a database owner/superuser. The host depends on the database service. Desktop remains an ordinary user process with no database secret.

Configuration/migration/users/drafts are provisioned before host readiness is declared. Retries preserve the existing cluster and credentials. Repairs rerun checksum-aware migrations and service configuration; they never reinitialize or delete an existing database. Uninstall stops/removes only this package's services and binaries; report data and encrypted recovery configuration remain. Reinstall/repair can reconnect them on the same Windows installation. DPAPI blobs are not a cross-machine backup; use PostgreSQL backup/restore for migration.

## Verification and release

Executed installation results, source identity and the downloadable 0.1.1 package are recorded in [installer evidence](windows-installer-evidence.md).

The 0.2.0 update adds a supplied first-run logo in a user-readable, administrator-writable branding folder. Secret folders retain separate restricted ACLs; ordinary users get traversal only on the parent. Default removal preserves data, while explicitly selected complete removal deletes owned application/data folders and the local key/trust after service ownership and linked-path checks. See [operator guide](operator-guide.md) and [agent handoff](agent-handoff-operator-ux.md).

Build workflow pins/checks the PostgreSQL archive and compiler, verifies Microsoft redistributable signature, publishes self-contained payloads, compiles installers and creates SHA-256 manifests. Windows smoke test installs silently, provisions synthetic accounts through standard input (no password CLI arguments), verifies trusted HTTPS, authenticates/writes/renders, restarts services, repairs without data loss, uninstalls and confirms data retention. Artifacts contain installers/docs/manifests only, not the smoke database or credentials.

Windows CI is service/install evidence on its runner OS. User Win10/Win11 UI, printer/pad, reboot and offline acceptance still belongs to issue #4. Packages are unsigned engineering builds until an authorized signing certificate is provided. No medical configuration approval is implied by successful installation.
