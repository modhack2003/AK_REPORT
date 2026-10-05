[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$install = Join-Path $env:ProgramFiles 'AK Diagnostic Reporting'
$data = Join-Path $env:ProgramData 'AK Diagnostic Reporting'
$setup = Join-Path $install 'setup/AkReporting.WindowsSetup.exe'
$endpoint = 'https://localhost:7043'
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Run-Setup([string[]]$Arguments, [string]$InputJson = '') {
    $start = [Diagnostics.ProcessStartInfo]::new($setup)
    $start.UseShellExecute = $false; $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $output = $process.StandardOutput.ReadToEndAsync(); $errorOutput = $process.StandardError.ReadToEndAsync()
    if ($InputJson) { $process.StandardInput.Write($InputJson) }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(240000)) { $process.Kill($true); throw 'Setup command timed out.' }
    Write-Host ($output.GetAwaiter().GetResult())
    Assert ($process.ExitCode -eq 0) ('Setup failed: ' + $errorOutput.GetAwaiter().GetResult())
    $process.Dispose()
}
function Api([string]$Path, $Body = $null, $Headers = @{}) {
    if ($null -eq $Body) { return Invoke-RestMethod "$endpoint/$Path" -Headers $Headers }
    return Invoke-RestMethod "$endpoint/$Path" -Method Post -Headers $Headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 20)
}
Assert (-not (Test-Path $data)) 'Smoke tests require a clean runner; refusing to use any existing installation data.'
Assert (-not (Test-Path $install)) 'Smoke tests require a clean runner application folder.'
$installer = (Get-ChildItem $PackageRoot -Filter 'AK-Reporting-Setup-*-win-x64.exe' | Select-Object -First 1).FullName
$process = Start-Process $installer -ArgumentList '/S' -Wait -PassThru
Assert ($process.ExitCode -in @(0,3010)) 'Installer returned an error.'
Assert (Test-Path $setup) 'First-run setup utility is missing.'
$adminPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$writerPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$credentials = @{ AdministratorName='install_qa_admin'; AdministratorPassword=$adminPassword; WriterName='install_qa_writer'; WriterPassword=$writerPassword } | ConvertTo-Json
Run-Setup @('--initialize-stdin') $credentials
foreach ($service in @('AKReportingDatabase','AKReportingHost')) {
    Assert ((Get-Service $service).Status -eq 'Running') "$service is not running."
    $details = Get-CimInstance Win32_Service -Filter "Name='$service'"
    Assert ($details.StartName -eq 'NT AUTHORITY\LocalService') 'Service must not run as LocalSystem or the installing user.'
}
Assert ((Api 'health/live').status -eq 'running') 'HTTPS host readiness failed.'
Assert (-not (Test-Path (Join-Path $data 'administration/initialization'))) 'Temporary initialization password folder remained.'
$roots = @(Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -like 'CN=AK Reporting Local *')
Assert ($roots.Count -eq 1 -and -not $roots[0].HasPrivateKey) 'Local trust anchor must have no retained private key.'
$server = @(Get-ChildItem Cert:\LocalMachine\My | Where-Object Issuer -eq $roots[0].Subject)[0]
Assert $server.HasPrivateKey 'Schannel certificate has no persisted private key.'
$rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($server)
try {
    if ($rsa -is [Security.Cryptography.RSACng]) { $keyFile = Join-Path $env:ProgramData ('Microsoft/Crypto/Keys/' + $rsa.Key.UniqueName) }
    else { $keyFile = Join-Path $env:ProgramData ('Microsoft/Crypto/RSA/MachineKeys/' + $rsa.CspKeyContainerInfo.UniqueKeyContainerName) }
    $keyAcl = Get-Acl $keyFile
    Assert $keyAcl.AreAccessRulesProtected 'HTTPS key inherits public ACLs.'
    $keyAllowed = @('S-1-5-18','S-1-5-32-544',([Security.Principal.NTAccount]::new('NT SERVICE\AKReportingHost')).Translate([Security.Principal.SecurityIdentifier]).Value)
    foreach ($rule in $keyAcl.Access) {
        $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        Assert ($rule.AccessControlType -ne 'Allow' -or $sid -in $keyAllowed) 'HTTPS private key is accessible outside its approved identities.'
    }
} finally { $rsa.Dispose(); $server.Dispose() }
$client = Start-Process (Join-Path $install 'client/AkReporting.Desktop.exe') -PassThru
try {
    Start-Sleep -Seconds 3
    $client.Refresh()
    Assert (-not $client.HasExited) 'Installed net48 client exited during startup.'
    Assert ($client.MainWindowTitle -like '*A K Diagnostic Reporting*') 'Installed reporting workspace did not create its main window.'
} finally { if (-not $client.HasExited) { $client.Kill(); $client.WaitForExit() }; $client.Dispose() }
foreach ($path in @('host/settings.dpapi','administration/owner.dpapi')) {
    $file = Join-Path $data $path
    Assert (Test-Path $file) 'Protected configuration missing.'
    $contents = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($file))
    Assert (-not $contents.Contains('Password=')) 'Installation configuration must not contain plaintext connection secrets.'
    $folderAcl = Get-Acl (Split-Path $file)
    Assert $folderAcl.AreAccessRulesProtected 'Secret folders must block inherited public ACLs.'
    $allowed = @('S-1-5-18','S-1-5-32-544')
    if ($path -like 'host/*') { $allowed += ([Security.Principal.NTAccount]::new('NT SERVICE\AKReportingHost')).Translate([Security.Principal.SecurityIdentifier]).Value }
    foreach ($rule in (Get-Acl $file).Access) {
        $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        Assert ($rule.AccessControlType -ne 'Allow' -or $sid -in $allowed) 'Secret ACL grants access outside the administrator/SYSTEM/host-service identities.'
    }
}
$login = Api 'auth/login' @{ Username='install_qa_writer'; Password=$writerPassword }
$headers = @{ Authorization='Bearer ' + $login.token }
$templates = Api 'templates' -Headers $headers
Assert ($templates.Count -eq 5) 'Five candidate draft schemas were not provisioned.'
$patient = @{ Name='SYNTHETIC INSTALLER QA'; LocalId='INSTALL-QA'; Age=365; AgeUnit='days'; Sex='Not recorded' }
$case = Api 'cases' @{ OperationId=[Guid]::NewGuid().ToString(); Patient=$patient } $headers
$template = @($templates | Where-Object reportTypeCode -eq 'CBC')[0]
$draft = @{ TemplateVersionId=$template.versionId; Metadata=@{ Patient=$patient; ReportTime=[DateTimeOffset]::UtcNow.ToString('O') }; Results=@() }
$report = Api 'reports' @{ OperationId=[Guid]::NewGuid().ToString(); CaseId=$case.id; Draft=$draft } $headers
$document = Api "reports/$($report.reportId)/documents/pdf" @{} $headers
$pdf = Invoke-WebRequest "$endpoint/documents/$($document.id)" -Headers $headers
Assert (($pdf.Headers.'Content-Type' -join ',') -eq 'application/pdf') 'Installed report PDF could not be downloaded.'
$ownerHash = (Get-FileHash (Join-Path $data 'administration/owner.dpapi')).Hash
$hostHash = (Get-FileHash (Join-Path $data 'host/settings.dpapi')).Hash
Stop-Service AKReportingHost
Restart-Service AKReportingDatabase
Start-Service AKReportingHost
Start-Sleep -Seconds 3
Run-Setup @('--repair')
Assert ((Api "reports/$($report.reportId)?revision=1" -Headers $headers).id -eq $report.id) 'Repair/restart changed saved revision identity.'
$download = Api "reports/$($report.reportId)/documents/pdf" @{} $headers
Assert ($download.sha256 -eq $document.sha256) 'Repair changed the pinned historical document hash.'
Assert ((Get-FileHash (Join-Path $data 'administration/owner.dpapi')).Hash -eq $ownerHash) 'Repair rotated/deleted database credentials.'
# Tampered ciphertext must not authenticate as valid configuration; never save secrets in artifacts.
$hostFile = Join-Path $data 'host/settings.dpapi'
$original = [IO.File]::ReadAllBytes($hostFile)
try {
    $tampered = $original.Clone(); $tampered[20] = $tampered[20] -bxor 1
    [IO.File]::WriteAllBytes($hostFile,$tampered)
    $start = [Diagnostics.ProcessStartInfo]::new($setup); $start.UseShellExecute=$false
    $start.ArgumentList.Add('--repair')
    $failed = [Diagnostics.Process]::Start($start); $failed.WaitForExit()
    Assert ($failed.ExitCode -ne 0) 'Tampered protected configuration was accepted.'
} finally { [IO.File]::WriteAllBytes($hostFile,$original) }
Run-Setup @('--repair')
$uninstall = Join-Path $install 'Uninstall.exe'
# _?= keeps NSIS uninstall in its own directory so WaitForExit covers the complete uninstall.
$removed = Start-Process $uninstall -ArgumentList @('/S',"_?=$install") -Wait -PassThru
Assert ($removed.ExitCode -eq 0) 'Uninstall failed.'
Assert ($null -eq (Get-Service AKReportingHost -ErrorAction SilentlyContinue)) 'Host service remained after uninstall.'
Assert ($null -eq (Get-Service AKReportingDatabase -ErrorAction SilentlyContinue)) 'Database service remained after uninstall.'
Assert (-not (Test-Path $keyFile)) 'Uninstall retained the machine HTTPS private key.'
Assert (@(Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -eq $roots[0].Subject).Count -eq 0) 'Uninstall retained the local trust anchor.'
Assert (Test-Path (Join-Path $data 'postgres-data/PG_VERSION')) 'Uninstall deleted database data.'
Assert ((Get-FileHash (Join-Path $data 'administration/owner.dpapi')).Hash -eq $ownerHash) 'Uninstall deleted recovery configuration.'
$reinstall = Start-Process $installer -ArgumentList '/S' -Wait -PassThru
Assert ($reinstall.ExitCode -in @(0,3010)) 'Reinstallation failed.'
Run-Setup @('--repair')
$newLogin = Api 'auth/login' @{ Username='install_qa_writer'; Password=$writerPassword }
$newHeaders = @{ Authorization='Bearer ' + $newLogin.token }
Assert ((Api "reports/$($report.reportId)?revision=1" -Headers $newHeaders).id -eq $report.id) 'Uninstall/reinstall did not preserve the saved report.'
Run-Setup @('--remove-services')
$clientInstaller = (Get-ChildItem $PackageRoot -Filter 'AK-Reporting-Client-*-win-x64.exe' | Select-Object -First 1).FullName
$clientInstalled = Start-Process $clientInstaller -ArgumentList '/S' -Wait -PassThru
Assert ($clientInstalled.ExitCode -eq 0) 'Client-only installation failed.'
$clientRoot = Join-Path $env:ProgramFiles 'AK Diagnostic Reporting Client'
Assert (Test-Path (Join-Path $clientRoot 'client/AkReporting.Desktop.exe')) 'Client-only executable missing.'
Assert (-not (Test-Path (Join-Path $clientRoot 'host'))) 'Client-only installer included a database/API host.'
Assert ($null -eq (Get-Service AKReportingHost -ErrorAction SilentlyContinue)) 'Client-only installer recreated a host service.'
$clientRemoved = Start-Process (Join-Path $clientRoot 'Uninstall.exe') -ArgumentList @('/S',"_?=$clientRoot") -Wait -PassThru
Assert ($clientRemoved.ExitCode -eq 0) 'Client-only uninstall failed.'
Assert (Test-Path (Join-Path $data 'postgres-data/PG_VERSION')) 'Client-only uninstall affected retained server data.'
Write-Host 'PASS: offline full/client packages, installed WPF launch, chosen accounts, trusted HTTPS, separate services, protected secrets, report/PDF, restart, repair, tamper rejection, uninstall/reinstall retention.'
