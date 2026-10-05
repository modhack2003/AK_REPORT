# A K Diagnostic Reporting

Offline-first reporting software for **A K Diagnostic Centre & Polyclinic**, Barrackpore, West Bengal.

## Delivery status

This repository is an incremental engineering implementation, **not a clinically approved or production-certified release**. The first implementation proves the structured report/revision/rendering architecture using CBC, LFT, Urine Routine, Histopathology and ECG. No clinical ranges, default units, critical values, automatic interpretation, medical credentials or real patient records are seeded.

Start with [architecture](docs/architecture.md), [clinical review](docs/medical-review.md), [compatibility](docs/windows-compatibility.md), and [release gates](docs/testing.md).

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
