param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$Publish,
    [switch]$Headless,
    [string]$DotnetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & ./tools/Test-RepositoryInputs.ps1
    & ./tools/Test-ReleasePackaging.ps1
    & $DotnetPath run --project tests/SplitDeck.Core.Tests -c $Configuration -p:TreatWarningsAsErrors=true
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    $testArguments = if ($Headless) { @('--','--headless') } else { @() }
    & $DotnetPath run --project tests/SplitDeck.Windows.Tests -c $Configuration -p:TreatWarningsAsErrors=true @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Windows component tests failed.' }
    & $DotnetPath build src/SplitDeck.App -c $Configuration --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Publish) {
        $project = [xml](Get-Content -LiteralPath src/SplitDeck.App/SplitDeck.App.csproj -Raw)
        $version = [string]$project.Project.PropertyGroup.Version
        if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$') { throw 'The application version cannot be used as a release filename.' }
        # A unique empty destination prevents files from an old RID or build entering this ZIP.
        $output = Join-Path $PSScriptRoot ('artifacts/publish-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        & $DotnetPath publish src/SplitDeck.App -c $Configuration -r win-x64 --self-contained true -o $output -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:TreatWarningsAsErrors=true
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item -LiteralPath README.md,LICENSE,THIRD-PARTY-NOTICES.md -Destination $output
        New-Item -ItemType Directory -Path (Join-Path $output 'docs'),(Join-Path $output 'licenses') -Force | Out-Null
        Copy-Item -LiteralPath docs/installation.md,docs/troubleshooting.md,docs/performance-v0.2.2.md,docs/performance-v0.2.4.md,docs/restoration-v0.2.5.md,docs/editor-preview.png -Destination (Join-Path $output 'docs')
        $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
        $deps = Get-Content -LiteralPath (Join-Path $output 'SplitDeck.deps.json') -Raw | ConvertFrom-Json
        foreach ($library in $deps.libraries.PSObject.Properties.Name) {
            if ($library -match '^runtimepack\.(Microsoft\.(?:NETCore|WindowsDesktop)\.App\.Runtime\.win-x64)/(.+)$') {
                $pack = Join-Path (Join-Path $packageRoot $Matches[1].ToLowerInvariant()) $Matches[2]
                $prefix = $Matches[1]
                foreach ($notice in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
                    $noticePath = Join-Path $pack $notice
                    if (Test-Path -LiteralPath $noticePath) { Copy-Item -LiteralPath $noticePath -Destination (Join-Path $output "licenses/$prefix-$notice") }
                }
            }
        }
        . ./tools/ReleasePackaging.ps1
        Assert-ReleasePackage $output
        $archive = Join-Path $PSScriptRoot "artifacts/SplitDeck-$version-windows-x64.zip"
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
        [IO.Compression.ZipFile]::CreateFromDirectory($output,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
        $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText($archive + '.sha256', "$hash  $([IO.Path]::GetFileName($archive))`n",[Text.Encoding]::ASCII)
        Write-Host "Verified portable release: $archive"
        Write-Host "SHA-256: $hash"
    }
} finally { Pop-Location }
