$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleasePackaging.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SplitDeck-package-test-' + [Guid]::NewGuid().ToString('N'))
function Assert-Rejected([scriptblock]$Check, [string]$Reason) {
    $rejected = $false
    try { & $Check } catch { $rejected = $true }
    if (!$rejected) { throw "Expected rejection: $Reason" }
}
function Write-Amd64Fixture([string]$Path) {
    $bytes = [byte[]]::new(128)
    $bytes[0] = 0x4d; $bytes[1] = 0x5a; $bytes[0x3c] = 64
    $bytes[64] = 0x50; $bytes[65] = 0x45; $bytes[68] = 0x64; $bytes[69] = 0x86
    [IO.File]::WriteAllBytes($Path, $bytes)
}
try {
    New-Item -ItemType Directory -Path $fixture | Out-Null
    foreach ($name in @('SplitDeck.exe','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll','SplitDeck.dll','SplitDeck.Core.dll','SplitDeck.Windows.dll','README.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Write-Amd64Fixture (Join-Path $fixture $name) }
    '{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0/win-x64"}}' | Set-Content (Join-Path $fixture 'SplitDeck.deps.json')
    '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}]}}' | Set-Content (Join-Path $fixture 'SplitDeck.runtimeconfig.json')
    New-Item -ItemType Directory -Path (Join-Path $fixture 'assets/driver'),(Join-Path $fixture 'licenses') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../assets/driver/MttVDD.inf'),(Join-Path $PSScriptRoot '../assets/driver/MttVDD.dll'),(Join-Path $PSScriptRoot '../assets/driver/mttvdd.cat'),(Join-Path $PSScriptRoot '../assets/driver/LICENSE.txt') -Destination (Join-Path $fixture 'assets/driver')
    foreach ($name in @('Microsoft.NETCore.App.Runtime.win-x64-LICENSE.TXT','Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.TXT','Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE')) { 'notice fixture' | Set-Content (Join-Path $fixture "licenses/$name") }
    Assert-ReleasePackage $fixture
    Write-Host 'PASS complete self-contained AMD64 fixture with the real signed driver bundle'
    $runtime = Join-Path $fixture 'coreclr.dll'; $saved = [IO.File]::ReadAllBytes($runtime); Remove-Item -LiteralPath $runtime
    Assert-Rejected { Assert-ReleasePackage $fixture } 'missing bundled runtime'
    [IO.File]::WriteAllBytes($runtime, $saved)
    $config = Join-Path $fixture 'SplitDeck.runtimeconfig.json'; $savedConfig = [IO.File]::ReadAllText($config)
    '{"runtimeOptions":{"framework":{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}}}' | Set-Content $config
    Assert-Rejected { Assert-ReleasePackage $fixture } 'framework-dependent publication'
    [IO.File]::WriteAllText($config, $savedConfig)
    $driver = Join-Path $fixture 'assets/driver/MttVDD.inf'; Add-Content -LiteralPath $driver -Value 'tampered'
    Assert-Rejected { Assert-ReleasePackage $fixture } 'tampered pinned driver'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../assets/driver/MttVDD.inf') -Destination $driver -Force
    $exe = Join-Path $fixture 'SplitDeck.exe'; $bytes = [IO.File]::ReadAllBytes($exe); $bytes[68] = 0x4c; $bytes[69] = 1; [IO.File]::WriteAllBytes($exe, $bytes)
    Assert-Rejected { Assert-ReleasePackage $fixture } 'wrong architecture app host'
    Write-Amd64Fixture $exe
    'personal' | Set-Content (Join-Path $fixture 'owner.json')
    Assert-Rejected { Assert-ReleasePackage $fixture } 'personal runtime ownership metadata'
    Write-Host 'PASS missing runtime, framework dependency, tampered driver, wrong RID and personal metadata rejection'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'SplitDeck-package-test-*') { throw 'Refusing fixture cleanup outside its temporary directory.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
