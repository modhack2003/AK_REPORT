# Windows compatibility strategy and spike

Research accessed 2026-10-05:

1. [Microsoft .NET Framework requirements](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements): .NET Framework **4.8** installable on Windows 7 SP1 (x86/x64). 4.8.1 is not the Win7 target.
2. [Modern .NET support](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): .NET 10 LTS server selected rather than near-EOL .NET 8. Modern .NET is not a Win7 client target.
3. [PostgreSQL Windows platform table](https://www.postgresql.org/download/windows/): current EDB-tested server platforms do not include Win7. Desktop installation on Windows 10/11 must be validated against the exact installer, OS and maintenance patch.

**Installation compatibility is not vendor security support.** Windows 7 is out of support. Windows 10 edition/build/ESU status must be inventoried. No claim of certified support follows from a successful compile.

## Spike before runtime lock

`AkReporting.Desktop` is the client compatibility spike and initial workspace. Compile with VS 2022/MSBuild and the net48 developer pack on a build machine. Ship only the runtime dependency to client PCs. Reference assemblies are build-only. Avoid WebView2/Electron and newer WinRT APIs.

Executed engineering evidence: [merged-main CI](https://github.com/modhack2003/AK_REPORT/actions/runs/37276000783) successfully built the net48 WPF client on a Windows-hosted runner. The same project also cross-compiled on Linux. Neither build executes the actual Win7/Win10/Win11 client/printer matrix below.

Run the same artifact on:

| Target | Required evidence | Current status |
|---|---|---|
| Win7 SP1 x86 and x64, fully patched prerequisites | Launch, TLS 1.2 login, long-name editor, preview glyphs, PDF/DOCX export, A4 inkjet/laser spool submission, idle logout | Not executed |
| Win10 actual center build/architecture | Same plus single-PC API/PostgreSQL service restart/recovery | Not executed |
| Win11 x64 | Same plus current printer drivers | Not executed |

Record hashes, runtime versions, driver names, imageable areas, screenshots without PHI and measured output. API certificate trust must work on Win7 without disabling certificate validation. Browser/PDF viewer shell printing is not an acceptable direct-print implementation.

The shared page plan is drawn in WPF at fixed physical A4 dimensions and sent through `PrintDialog.PrintDocument`. PDF/preview font metrics and driver offsets require comparison. The client must reject non-A4/insufficient imageable-area settings rather than silently scale a report. Per-printer calibration is pending acceptance.

Decision pending center confirmation: Is the first all-in-one machine Windows 10/11? If only Win7 is available, provide a modern local host or revisit the server compatibility design before deployment.
