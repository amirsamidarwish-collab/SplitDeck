$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $tracked = @(& git ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect tracked release inputs.' }
    foreach ($relative in $tracked) {
        if ($relative -match '(^|/)(\.superpowers|artifacts|node_modules|sessions|requests|logs|reports)/|(^|/)(owner|restart-required|profile)\.json$|(^|/)vdd_settings\.xml$|(^|/)\.env($|\.)|\.(pfx|p12|key|pdb|log)$|^docs/task-(core|ui)\.txt$') { throw "Local/private build or runtime input is tracked: $relative" }
        if ($relative -notmatch '\.(cs|csproj|props|ps1|py|html|xaml|ya?ml|json|md|txt)$|(^|/)\.gitignore$') { continue }
        $full = Join-Path $root $relative
        if (!(Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        $text = [IO.File]::ReadAllText($full)
        if ($text -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|sk-[A-Za-z0-9_-]{40,}') { throw "Potential credential material in tracked input: $relative" }
    }
    Write-Host "PASS tracked-input hygiene ($($tracked.Count) files; credential values are never printed)."
} finally { Pop-Location }
