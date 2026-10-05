# Engineering increment 0.1 — 2026-10-05

## Implemented foundation

* Research/specification register, architecture/ERD, module boundaries, compatibility strategy and multi-device collaboration instructions.
* netstandard2.0 transport contracts, .NET 10 API and restricted PostgreSQL persistence.
* Minimal cases with separate reports, atomic configurable public numbers, retry-sensitive operation IDs and concurrency-safe append-only revisions.
* Login, salted PBKDF2 password hashing, opaque hashed sessions, idle expiry, lockout, role checks and sanitized error responses.
* Immutable draft/approved template versions and doctor/signature/stamp versions; clinical approval and issuance are distinct recorded operations.
* Shared measured A4 page plans, embedded-font PDF, OpenXML DOCX and protected document hashes/bytes.
* WPF/net48 workspace: HTTP login, cases/investigations, manual structured editor, patient/specimen details, controlled formatting, revision history, preview/export/print and catalog provisioning.
* Six synthetic golden fixtures and domain/rendering/real-PostgreSQL/API tests. Remaining report families are not implemented.

## Executed checks

* Linux .NET 10.0.401 SDK / 10.0.12 runtime: server Release test run **28 passed, 0 skipped** against real PostgreSQL 17 using a restricted runtime login.
* Independent PDF parser verifies extracted text and page counts; DOCX validates against OpenXML schemas; repeated generation yields equal bytes for the golden fixtures.
* WPF/net48 cross-compilation on Linux succeeded with zero warnings/errors. **A Windows-host build and actual Win7/10/11 runtime/printer checks are still separate requirements.**
* NuGet vulnerability audit found no known vulnerable direct/transitive packages in the server solution or desktop project using the queried advisory source.

CI workflow runs server/PostgreSQL checks and a Windows client build on each pushed checkpoint. Update this section with actual CI outcome rather than assuming success.

## Pending release evidence

* Anonymized center reports, actual units/methods/reference text, qualified clinical review and actual doctor/technician permission/attribution records.
* Real Win7 SP1 x86/x64, center Win10/Win11 builds, TLS trust, inkjet/laser drivers, physical pad offsets and print/preview comparison.
* Complex-script rendering (including Bengali) is not supported by the pinned Latin/Greek/Cyrillic font configuration; unsupported glyphs fail explicitly. A reviewed shaping/font extension and acceptance are needed if these scripts are used in reports.
* Windows service/installer packaging, code signing, center restore drill, power-loss/restart/disk-full/printer-failure evidence, usability acceptance and archived release artifacts.
* Center-approved schema-version migration UI, user lifecycle management beyond provisioning, retention/archive operator UI and printer calibration workflow need hardening before rollout.

This increment is **not production-ready or clinically approved**.
