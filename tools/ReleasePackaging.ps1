Set-StrictMode -Version Latest
function Assert-ReleasePackage {
    param([Parameter(Mandatory)][string]$Path)
    $required = @('SplitDeck.exe','SplitDeck.dll','SplitDeck.Core.dll','SplitDeck.Windows.dll','SplitDeck.deps.json','SplitDeck.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','PresentationFramework.dll','README.md','LICENSE','THIRD-PARTY-NOTICES.md','assets/driver/LICENSE.txt','licenses/Microsoft.NETCore.App.Runtime.win-x64-LICENSE.TXT','licenses/Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.TXT','licenses/Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE')
    foreach ($relative in $required) { if (!(Test-Path -LiteralPath (Join-Path $Path $relative) -PathType Leaf)) { throw "Release is missing required content: $relative" } }
    $deps = Get-Content -LiteralPath (Join-Path $Path 'SplitDeck.deps.json') -Raw | ConvertFrom-Json
    if (!$deps.runtimeTarget.name.EndsWith('/win-x64',[StringComparison]::OrdinalIgnoreCase)) { throw 'Release dependencies do not target win-x64.' }
    $runtime = (Get-Content -LiteralPath (Join-Path $Path 'SplitDeck.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions
    if ($null -eq $runtime.PSObject.Properties['includedFrameworks']) { throw 'Release requires an installed .NET runtime instead of including its own.' }
    $frameworks = @($runtime.includedFrameworks | ForEach-Object name)
    if ('Microsoft.NETCore.App' -notin $frameworks -or 'Microsoft.WindowsDesktop.App' -notin $frameworks -or $null -ne $runtime.PSObject.Properties['framework'] -or $null -ne $runtime.PSObject.Properties['frameworks']) { throw 'Release must include both .NET and Windows Desktop runtimes.' }
    foreach ($relative in @('SplitDeck.exe','coreclr.dll','assets/driver/MttVDD.dll')) {
        $stream = [IO.File]::OpenRead((Join-Path $Path $relative)); $reader = [IO.BinaryReader]::new($stream)
        try {
            if ($stream.Length -lt 70 -or $reader.ReadUInt16() -ne 0x5a4d) { throw "Invalid Windows executable: $relative" }
            $stream.Position = 0x3c; $offset = $reader.ReadInt32()
            if ($offset -lt 64 -or $offset -gt $stream.Length - 6) { throw "Invalid Windows PE header: $relative" }
            $stream.Position = $offset
            if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Release contains a non-AMD64 executable: $relative" }
        } finally { $reader.Dispose() }
    }
    $hashes = @{
        'MttVDD.inf' = '550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D'
        'MttVDD.dll' = 'C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05'
        'mttvdd.cat' = '08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6'
    }
    foreach ($file in $hashes.Keys) { if ((Get-FileHash -LiteralPath (Join-Path $Path "assets/driver/$file") -Algorithm SHA256).Hash -ne $hashes[$file]) { throw "Release driver integrity check failed: $file" } }
    foreach ($file in @('MttVDD.dll','mttvdd.cat')) { if ((Get-AuthenticodeSignature -LiteralPath (Join-Path $Path "assets/driver/$file")).Status -ne 'Valid') { throw "Windows does not trust the bundled driver signature: $file" } }
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -File) {
        if ($file.Name -match '^(owner|restart-required|profile)\.json$|^vdd_settings\.xml$|^\.env($|\.)|\.(pdb|pfx|p12|key|log)$|\.error\.txt$|\.(heartbeat|performance|topology|window-move|window-restore|ready)$' -or $file.DirectoryName -match '[\\/](sessions|requests|logs|reports|\.superpowers)[\\/]?') { throw "Local/private data entered the portable release: $($file.Name)" }
    }
}
