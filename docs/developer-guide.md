# Developer guide

Read `AGENTS.md` and claim work in the shared issue board first. The initial foundation is tracked in [issue #1](https://github.com/modhack2003/AK_REPORT/issues/1).

## Build and tests

Install .NET SDK 10 (the `global.json` roll-forward policy accepts supported .NET 10 SDK feature bands). On Linux/macOS build the server solution:

```sh
dotnet build AkReporting.slnx --configuration Release
dotnet test AkReporting.slnx --configuration Release
```

Without `AK_TEST_PG`, integration checks are explicitly skipped. To execute them, supply an **isolated development PostgreSQL administrative connection**, never a patient database:

```sh
export AK_TEST_PG='<isolated PostgreSQL administrative connection>'
export AK_GOLDEN_OUTPUT=artifacts/golden
dotnet test AkReporting.slnx --configuration Release --logger trx
```

The fixture creates random test database/login names, applies the real migration twice, grants the real runtime privileges, tests using that restricted login, then drops its own temporary database/login. It requires administrative create/drop privileges. Passwords are generated at runtime; no credentials are seeded in source. CI's trust-auth PostgreSQL service is an ephemeral synthetic-data test service only.

Golden outputs are synthetic and ignored by Git. PDF fonts are embedded. Tests use an independent PDF parser and the OpenXML schema validator. Review real PDFs visually and compare the printed pad separately.

On a Windows build host with .NET 10 SDK and net48 reference assemblies/developer pack:

```powershell
dotnet build src/AkReporting.Desktop/AkReporting.Desktop.csproj --configuration Release
```

The WPF binary targets net48 and should be tested on Win7 SP1/Win10/Win11. The SDK is a build-host dependency, not a Win7-client dependency. Compilation alone is not compatibility/printer evidence.

## Architecture discipline

Contracts use netstandard2.0 and avoid runtime types unavailable to net48. Domain references contracts only; rendering/infrastructure reference application boundaries. API composes services. WPF has HTTP transport contracts only.

Schema additions must include sources, human review status and compatibility tests. Never change applied SQL migrations. Add a numbered migration and describe impacts. Rendering uses a single `LayoutEngine`, not per-family renderers.

## Dependency review

```sh
dotnet list AkReporting.slnx package --vulnerable --include-transitive
dotnet list src/AkReporting.Desktop/AkReporting.Desktop.csproj package --vulnerable --include-transitive
```

Archive dependency/font manifests and the exact renderer build with each approved deployment. The current renderer uses pinned PDFsharp/OpenXML packages and fixed fonts; reproducibility across a future engine/package update requires a new release decision, while stored historical document bytes remain unchanged.

## Build Windows installation packages

On a Windows build machine with PowerShell 7 and .NET SDK 10:

```powershell
./packaging/windows/Build-Package.ps1
```

This downloads hash-pinned PostgreSQL/NSIS tools, validates the Microsoft redistributable signature, publishes pinned self-contained .NET 10.0.12 host/setup payloads, builds the net48 client, and emits `artifacts/windows-package/installers` with full/client EXEs, manifests and instructions. Build requires internet; the resulting setup does not.

On an **isolated clean administrative Windows VM/runner only**:

```powershell
./packaging/windows/Test-Installation.ps1 -PackageRoot ./artifacts/windows-package/installers
```

The smoke script refuses existing product data, creates only synthetic account/report input and exercises trusted HTTPS, service identities, secret ACLs, restart, repair, DPAPI tamper rejection and uninstall/reinstall retention. Never run it against a center installation. The workflow uploads only installers/docs/manifests, not the generated secrets or PostgreSQL data. `AkReporting.WindowsSetup` stays outside the cross-platform solution but is built/published by the Windows installer workflow; `AkReporting.Deployment` is part of the API dependency graph.
