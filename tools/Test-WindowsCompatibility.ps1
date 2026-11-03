param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [Parameter(Mandatory)][string]$ReportDirectory,
    [switch]$RequireWindows11
)
$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$hostOS = Get-CimInstance Win32_OperatingSystem
if ($RequireWindows11 -and ($hostOS.Caption -notlike '*Windows 11*' -or $hostOS.ProductType -ne 1 -or [int]$hostOS.BuildNumber -lt 22000)) {
    throw "This check requires a Windows 11 client; found $($hostOS.Caption)."
}
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$report = Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) 'compatibility.json'
if (Test-Path -LiteralPath $report) { throw 'Use a fresh report directory so an earlier result cannot pass this check.' }
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.WorkingDirectory = [IO.Path]::GetDirectoryName($executable)
$start.ArgumentList.Add('--compatibility-smoke')
$start.ArgumentList.Add($report)
$process = [Diagnostics.Process]::Start($start)
try {
    if (!$process.WaitForExit(30000)) { $process.Kill(); $process.WaitForExit(); throw 'Preview startup exceeded 30 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Preview exited with code $($process.ExitCode)." }
} finally { $process.Dispose() }
if (!(Test-Path -LiteralPath $report)) { throw 'Preview did not produce a compatibility report.' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if ($result.Result -ne 'PASS' -or !$result.TaskbarControlsVerified -or !$result.PreviewOnly -or !$result.DriverActionsDisabled -or $result.MutatedDisplaySettings -or $result.WindowsSettingsOpened -or $result.TaskbarSettingsUri -ne 'ms-settings:taskbar') { throw 'Preview safety or taskbar-control verification failed.' }
if ($result.ProcessArchitecture -ne 'X64') { throw 'The release startup check must run the packaged x64 application.' }
if (!(Test-Path -LiteralPath ($report + '.png'))) { throw 'The editor did not render a preview image.' }
$result | Add-Member NoteProperty HostCaption $hostOS.Caption
$result | Add-Member NoteProperty HostBuild $hostOS.BuildNumber
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding utf8
Write-Host "PASS x64 preview startup on $($hostOS.Caption), build $($hostOS.BuildNumber), OS architecture $($result.OSArchitecture)."
Write-Host 'Scope: editor startup/rendering and native taskbar settings control only; no driver installation or live split.'
