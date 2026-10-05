# Runs under Windows PowerShell 5.1 / STA against the actual installed net48 UI.
# Credentials arrive only through stdin and are never saved or printed.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientPath, [string]$EvidenceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class UiCapture { [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags); }'
$inputData = [Console]::In.ReadToEnd() | ConvertFrom-Json
$endpoint = 'https://localhost:7043'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Wait-Until([scriptblock]$Check, [string]$Failure) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do { if (& $Check) { return }; Start-Sleep -Milliseconds 250 } while ([DateTime]::UtcNow -lt $deadline)
    throw $Failure
}
function Element([string]$Id) {
    return $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Id))
}
function Fill([string]$Id, [string]$Value) {
    $element = Element $Id
    $element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Click([string]$Id) {
    $element = Element $Id
    Wait-Until { $element.Current.IsEnabled } "UI control did not become enabled: $Id"
    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Api([string]$Path, $Body = $null, $Headers = @{}) {
    # Windows PowerShell 5.1 emits REST arrays as a single pipeline object.
    # Materialize first so the function return enumerates collections normally.
    if ($null -eq $Body) { $result = Invoke-RestMethod "$endpoint/$Path" -Headers $Headers }
    else { $result = Invoke-RestMethod "$endpoint/$Path" -Method Post -Headers $Headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 20) }
    return $result
}
function Capture([string]$Name) {
    if (-not $EvidenceRoot) { return }
    New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
    Start-Sleep -Milliseconds 500
    $bounds = $script:window.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap); $dc = $graphics.GetHdc()
    try { [UiCapture]::PrintWindow($client.MainWindowHandle,$dc,2) | Out-Null }
    finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    try { $bitmap.Save((Join-Path $EvidenceRoot $Name),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}
$client = Start-Process $ClientPath -PassThru
try {
    Wait-Until {
        $script:window = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$client.Id))
        $null -ne $script:window -and $null -ne (Element 'Username')
    } 'Installed desktop did not show its shared login page.'
    Wait-Until { $null -eq (Element 'SplashLogo') -or (Element 'SplashLogo').Current.IsOffscreen } 'One-second splash did not transition to login.'
    Capture '01-login.png'
    Fill 'Username' 'install_qa_admin'; Fill 'Password' $inputData.AdministratorPassword; Click 'LoginButton'
    Wait-Until { $null -ne (Element 'CreateButton') -and -not (Element 'CreateButton').Current.IsOffscreen -and (Element 'CreateButton').Current.IsEnabled } 'Administrator could not enter the reporting workspace.'
    Capture '02-patient-and-test.png'
    Click 'CreateButton'
    Wait-Until { (Element 'Status').Current.Name -like '*patient name*' } 'Missing patient name did not provide an actionable validation message.'
    $patientName = 'SYNTHETIC DESKTOP UX QA'
    Fill 'PatientName' $patientName; Click 'CreateButton'
    Wait-Until { (Element 'Status').Current.Name -like '*at least one test*' } 'Missing test selection did not stop report creation.'
    $login = Api 'auth/login' @{ Username='install_qa_admin'; Password=$inputData.AdministratorPassword }
    $headers = @{ Authorization='Bearer ' + $login.token }
    $cbc = @(Api 'templates' -Headers $headers | Where-Object reportTypeCode -eq 'CBC')[0]
    $checkbox = (Element 'Investigations').FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,[string]$cbc.title))
    Assert ($null -ne $checkbox) 'CBC test checkbox was not accessible.'
    $checkbox.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Click 'CreateButton'
    Wait-Until { (Element 'Status').Current.Name -like 'Report created.*' -and (Element 'SavePreviewButton').Current.IsEnabled } 'Minimal report creation did not continue directly to results.'
    Capture '03-results.png'
    Click 'SaveButton'
    Wait-Until { (Element 'Status').Current.Name -like 'Revision * saved.*' } 'Draft saving unexpectedly required optional input or a save note.'
    $section = @($cbc.sections | Where-Object { @($_.fields | Where-Object kind -eq 0).Count -gt 0 })[0]
    $numeric = @($section.fields | Where-Object kind -eq 0)[0]
    Fill ('Result_' + $section.code + '_' + $numeric.code + '_0') '1.25'
    Click 'SavePreviewButton'
    Wait-Until { (Element 'Status').Current.Name -like 'Saved revision preview:*' } 'Save and preview did not open the saved report.'
    Capture '04-preview.png'
    $cases = @(Api 'cases/search' @{ Query=$patientName } $headers)
    Assert ($cases.Count -eq 1) 'Desktop operation did not create exactly one synthetic case.'
    $reports = @(Api "cases/$($cases[0].id)/reports" -Headers $headers)
    Assert ($reports.Count -eq 1) 'Desktop operation did not create exactly one selected test report.'
    $revision = Api "reports/$($reports[0].id)" -Headers $headers
    Assert ($null -eq $revision.data.metadata.patient.age -and $revision.data.metadata.patient.ageUnit -eq '') 'Blank age details were not preserved.'
    Assert ($revision.data.metadata.clinicalHistory -eq '' -and $null -eq $revision.data.metadata.collectionTime) 'Optional notes/collection date unexpectedly became required or populated.'
    Assert ($revision.data.results.Count -eq 1 -and $revision.number -eq 3 -and $revision.data.results[0].numericValue -eq 1.25) 'Entered numeric result was not saved before preview.'
    $blankRevision = Api "reports/$($reports[0].id)?revision=2" -Headers $headers
    Assert ($blankRevision.data.results.Count -eq 0) 'Optional empty results were not preserved in the earlier saved revision.'
    Click 'ManageButton'
    Wait-Until { $null -ne (Element 'Doctor_name') -and (Element 'Doctor_name').Current.IsEnabled } 'Administrator doctor editor did not open.'
    Fill 'Doctor_name' 'SYNTHETIC UI DOCTOR'; Fill 'Doctor_evidence' 'Synthetic UI permission fixture; not an actual clinician.'
    Click 'SaveDoctorButton'
    Wait-Until { @(Api 'doctors' -Headers $headers | Where-Object name -eq 'SYNTHETIC UI DOCTOR').Count -eq 1 } 'Doctor UI did not save a reusable version without requiring UUID entry.'
    Capture '05-doctor-library.png'
    Write-Host 'PASS: installed desktop login, administrator reporting, name/test validation, minimal-input create, optional blanks, draft save/preview and administrator doctor library.'
} finally {
    if (-not $client.HasExited) { $client.Kill(); $client.WaitForExit() }
    $client.Dispose()
    $inputData = $null
}
