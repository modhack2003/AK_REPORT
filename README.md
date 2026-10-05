# A K Diagnostic Reporting

Offline-first reporting software for **A K Diagnostic Centre & Polyclinic**, Barrackpore, West Bengal.

[![Engineering verification](https://github.com/modhack2003/AK_REPORT/actions/workflows/verification.yml/badge.svg?branch=main)](https://github.com/modhack2003/AK_REPORT/actions/workflows/verification.yml)

## Delivery status

This repository is an incremental engineering implementation, **not a clinically approved or production-certified release**. The first implementation proves the structured report/revision/rendering architecture using CBC, LFT, Urine Routine, Histopathology and ECG. No clinical ranges, default units, critical values, automatic interpretation, medical credentials or real patient records are seeded.

Start with [architecture](docs/architecture.md), [clinical review](docs/medical-review.md), [compatibility](docs/windows-compatibility.md), and [release gates](docs/testing.md).

The foundation is integrated into `main` through [PR #2](https://github.com/modhack2003/AK_REPORT/pull/2). [Main verification](https://github.com/modhack2003/AK_REPORT/actions/runs/37276000783) passed the server/PostgreSQL checks and Windows net48 build. The run contains downloadable **synthetic golden documents** and the **WPF compatibility-spike build**. Actual client-OS, printer and clinical acceptance remain pending.

An **offline Windows 10/11 x64 test installer** is available from [the verified package download](docs/windows-installer-evidence.md). Full installation, trusted HTTPS, desktop launch, repair and uninstall/reinstall retention passed on Windows CI. Follow [Windows test instructions](docs/windows-testing.md). Packaging is published for review in [PR #10](https://github.com/modhack2003/AK_REPORT/pull/10).

## Projects

| Project | Responsibility |
|---|---|
| `AkReporting.Contracts` | Transport contracts usable by the .NET Framework client |
| `AkReporting.Domain` | Structured schema, result validation, revision and attribution models |
| `AkReporting.Application` | Use cases and persistence/rendering boundaries |
| `AkReporting.Infrastructure` | PostgreSQL, authentication and versioned catalog persistence |
| `AkReporting.Rendering` | Shared A4 layout plan, embedded-font PDF, OpenXML DOCX |
| `AkReporting.Api` | Offline/LAN application host and permission enforcement |
| `AkReporting.Desktop` | WPF/.NET Framework 4.8 Windows client and print/preview spike |
| `AkReporting.Tests` | Domain, rendering, security and real-PostgreSQL integration tests |

See [developer guide](docs/developer-guide.md) for build/test commands and [installation](docs/installation.md) for provisioning. Windows hardware, center reports, letterhead measurements and qualified medical reviewers are required to complete acceptance.
