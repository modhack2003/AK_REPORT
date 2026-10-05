# Installation and provisioning

This is an engineering-validation deployment. Complete the release gates before using it for clinical reports.

## Host prerequisites

Choose a compatible modern Windows 10/11 or Windows Server host for .NET 10 and a current PostgreSQL 17 maintenance release. Verify the exact OS/installer support and center hardware. A Win7 client uses this host over LAN; a supported Win7-only all-in-one host has not been proven.

Install PostgreSQL using an administrative owner and create a separate restricted application login. Use SCRAM authentication, loopback binding for colocated deployment and OS/service ACLs. For LAN clients expose the API only, not PostgreSQL. Do not use the CI trust-auth setting in an installation.

## Database

Supply secrets through a protected administrative process/environment; do not put them in the repository or WPF configuration.

```powershell
# AK_MIGRATION_CONNECTION: administrative connection supplied externally
dotnet run --project src/AkReporting.Api -- --migrate
# Runtime login must already exist; supply owner authentication externally to psql
psql -d ak_reporting -v runtime_role=ak_reporting_app -f database/grant-runtime.sql
```

Run migrations as the owner, never the service login. Applied migrations are hashed and repeatable migration invocation is a no-op. Do not edit migration 001 after deployment.

## Application host

```powershell
dotnet publish src/AkReporting.Api/AkReporting.Api.csproj -c Release -r win-x64 --self-contained false -o artifacts/host
# AK_DB_CONNECTION: restricted service-login connection supplied externally
dotnet run --project src/AkReporting.Api -- --bootstrap-admin
```

Bootstrap reads the first administrator password interactively; it refuses to create another bootstrap administrator once a user exists. Provision Writer/Receptionist/MedicalReviewer through the authenticated administration workspace/API. Assign MedicalReviewer only to the center's actual qualified reviewer.

Run the published host under a dedicated Windows service account or supervised process. Configure ASP.NET Core Kestrel HTTPS with a trusted certificate through protected service configuration; bind only the local address/LAN interface required. No certificate/private key is included. The client default URL is `https://localhost:7043`; explicitly configure the host URL/port accordingly. Restrict firewall access to center client PCs.

For **loopback development only**, set `ASPNETCORE_ENVIRONMENT=Development` and a loopback HTTP URL. The API rejects HTTP elsewhere. Never suppress TLS certificate verification on Win7; deploy a trusted CA/root and confirm TLS 1.2 prerequisites.

## Desktop

Build on Windows and copy the complete `src/AkReporting.Desktop/bin/Release/net48` output to a protected application folder. Install .NET Framework **4.8** on Win7 SP1, or use the compatible in-place 4.x runtime on Win10/11. Set `AK_API_URL` to the HTTPS local/LAN host URL. No database password belongs on client machines.

Noto Sans and Noto Serif fonts are embedded in the client/PDF. Install the pinned font files from `assets/fonts` for applications viewing DOCX; otherwise Word may substitute a font and reflow. Keep the SIL license with redistributed fonts.

## Center acceptance

1. Inventory OS versions/architectures, actual printer models/drivers and service PC.
2. Import the five candidate drafts using administrator controls. They cannot be clinically issued.
3. Compare anonymized center samples and SOPs; developers publish center-specific draft definitions including documented units/ranges only after proper research.
4. Qualified reviewer records actual approval evidence and publishes a new immutable approved version.
5. Configure real authorized doctor/signature/stamp versions and actual technician attribution.
6. Measure A4 pad header/footer offsets, update template layout margins, regenerate goldens and accept physical print accuracy.
7. Execute restart/crash, two-writer, restore and Win7/10/11 checks. Record evidence in release notes before clinical deployment.

Installer packaging, code signing, Windows service installer, unattended upgrades and certified printer calibration remain later production-hardening work.
