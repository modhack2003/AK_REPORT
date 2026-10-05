[CmdletBinding()]
param([string]$Version = '0.1.1', [string]$OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Installer build requires a Windows build host.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'artifacts/windows-package' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part package version.' }
$payload = Join-Path $OutputRoot 'payload'
$downloads = Join-Path $OutputRoot 'downloads'
$release = Join-Path $OutputRoot 'installers'
New-Item -ItemType Directory -Force -Path $payload,$downloads,$release | Out-Null
$dependencies = Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
function Download-Pinned($Dependency, [string]$Name) {
    $path = Join-Path $downloads $Name
    # SourceForge returns a browser landing page for some PowerShell user agents.
    # Request the binary download as a CLI client and still require the pinned hash.
    if (-not (Test-Path $path)) { Invoke-WebRequest $Dependency.Url -UserAgent 'curl/8.0' -OutFile $path }
    $actual = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Dependency.Sha256) { throw "Checksum mismatch: $Name; actual $actual; bytes $((Get-Item $path).Length)" }
    return $path
}
function DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build/publish failed.' }
}
$pgArchive = Download-Pinned $dependencies.PostgreSql 'postgresql.zip'
$nsisArchive = Download-Pinned $dependencies.Nsis 'nsis.zip'
$pgExtract = Join-Path $downloads 'postgres'
$nsisExtract = Join-Path $downloads 'nsis'
if (-not (Test-Path (Join-Path $pgExtract 'pgsql/bin/postgres.exe'))) { Expand-Archive $pgArchive $pgExtract -Force }
if (-not (Test-Path (Join-Path $nsisExtract 'nsis-3.11/makensis.exe'))) { Expand-Archive $nsisArchive $nsisExtract -Force }
$postgres = Join-Path $payload 'postgres'
New-Item -ItemType Directory -Force -Path $postgres | Out-Null
foreach ($directory in @('bin','lib','share','doc')) {
    Copy-Item (Join-Path $pgExtract "pgsql/$directory") $postgres -Recurse -Force
}
# Preserve root-level vendor notices alongside the server binaries; exclude pgAdmin/StackBuilder.
Get-ChildItem (Join-Path $pgExtract 'pgsql') -File | Copy-Item -Destination $postgres -Force
$prerequisites = Join-Path $payload 'prerequisites'
New-Item -ItemType Directory -Force -Path $prerequisites | Out-Null
$vc = Join-Path $prerequisites 'VC_redist.x64.exe'
Invoke-WebRequest $dependencies.VisualCpp.Url -OutFile $vc
$signature = Get-AuthenticodeSignature $vc
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Microsoft Corporation*') { throw 'Microsoft redistributable signature validation failed.' }
DotNet -Arguments @('build',(Join-Path $repo 'src/AkReporting.Desktop/AkReporting.Desktop.csproj'),'-c','Release')
$client = Join-Path $payload 'client'
New-Item -ItemType Directory -Force -Path $client | Out-Null
Copy-Item (Join-Path $repo 'src/AkReporting.Desktop/bin/Release/net48/*') $client -Recurse -Force
foreach ($item in @(@('AkReporting.Api','host'),@('AkReporting.WindowsSetup','setup'))) {
    DotNet -Arguments @('publish',(Join-Path $repo "src/$($item[0])/$($item[0]).csproj"),'-c','Release','-r','win-x64','--self-contained','true',
        "-p:RuntimeFrameworkVersion=$($dependencies.DotNetRuntime)",'-p:PublishSingleFile=false','-p:PublishTrimmed=false','-o',(Join-Path $payload $item[1]))
}
$documentation = Join-Path $payload 'docs'
New-Item -ItemType Directory -Force -Path $documentation | Out-Null
Copy-Item (Join-Path $repo 'docs/windows-testing.md'),(Join-Path $repo 'docs/backup-restore.md'),(Join-Path $repo 'THIRD_PARTY_NOTICES.md') $documentation
Copy-Item (Join-Path $PSScriptRoot 'README-FIRST.txt') $release
Copy-Item (Join-Path $PSScriptRoot 'README-FIRST.txt') $documentation
Copy-Item (Join-Path $nsisExtract 'nsis-3.11/COPYING') (Join-Path $documentation 'NSIS-License.txt')
$files = @(Get-ChildItem $payload -Recurse -File | Sort-Object FullName)
$payloadManifest = @($files | ForEach-Object { [ordered]@{ Path = [IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/'); Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
$commit = (& git -C $repo rev-parse HEAD).Trim()
[ordered]@{ Version=$Version; Commit=$commit; Architecture='win-x64'; Dependencies=$dependencies;
    VisualCppSha256=(Get-FileHash $vc -Algorithm SHA256).Hash.ToLowerInvariant(); Files=$payloadManifest } |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $payload 'package-manifest.json') -Encoding utf8

# Uninstall only packaged files. An operator's unexpected files in the install folder survive.
function Make-Uninstall([string]$Path, [bool]$ClientOnly) {
    $commands = [Collections.Generic.List[string]]::new()
    $selected = @(Get-ChildItem $payload -Recurse -File | Where-Object { -not $ClientOnly -or $_.FullName.StartsWith($client + '\') -or $_.FullName.StartsWith($documentation + '\') })
    foreach ($file in $selected) {
        $relative = [IO.Path]::GetRelativePath($payload,$file.FullName)
        $commands.Add('Delete "$INSTDIR\' + $relative + '"')
    }
    $directories = @(Get-ChildItem $payload -Recurse -Directory | Where-Object { -not $ClientOnly -or $_.FullName.StartsWith($client) -or $_.FullName.StartsWith($documentation) } | Sort-Object { $_.FullName.Length } -Descending)
    foreach ($directory in $directories) { $commands.Add('RMDir "$INSTDIR\' + [IO.Path]::GetRelativePath($payload,$directory.FullName) + '"') }
    if (-not $ClientOnly) { $commands.Add('Delete "$INSTDIR\package-manifest.json"') }
    $commands | Set-Content $Path -Encoding utf8
}
Make-Uninstall (Join-Path $OutputRoot 'uninstall-full.nsh') $false
Make-Uninstall (Join-Path $OutputRoot 'uninstall-client.nsh') $true
$compiler = Join-Path $nsisExtract 'nsis-3.11/makensis.exe'
foreach ($kind in @('full','client')) {
    $arguments = @('/V2',"/DPAYLOAD=$payload","/DOUTPUT=$release","/DVERSION=$Version","/DUNINSTALL_LIST=$(Join-Path $OutputRoot "uninstall-$kind.nsh")")
    if ($kind -eq 'client') { $arguments += '/DCLIENT_ONLY' }
    & $compiler @arguments (Join-Path $PSScriptRoot 'Installer.nsi')
    if ($LASTEXITCODE -ne 0) { throw 'NSIS installer compilation failed.' }
}
Copy-Item (Join-Path $payload 'package-manifest.json') $release
Get-ChildItem $release -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
} | Set-Content (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Installers generated in $release"
