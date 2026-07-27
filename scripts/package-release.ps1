param(
    [string]$Version,
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "WPlayer.csproj"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$projectXml = Get-Content -LiteralPath $project
    $Version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root "artifacts\releases"
}

$publishDir = Join-Path $root "artifacts\publish\WPlayer-$Version"
$icon = Join-Path $root "logo.ico"

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE."
    }

    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    if (Test-Path -LiteralPath $OutputDir) {
        Get-ChildItem -LiteralPath $OutputDir -File |
            Where-Object {
                $_.Name -in @(
                    "assets.win.json",
                    "releases.win.json",
                    "RELEASES",
                    "WPlayer-Setup.exe",
                    "WPlayer-Portable.zip",
                    "WPlayer.Desktop-win-Setup.exe",
                    "WPlayer.Desktop-win-Portable.zip"
                ) `
                    -or $_.Name -like "WPlayer.Desktop-*-full.nupkg" `
                    -or $_.Name -like "WPlayer.Desktop-*-delta.nupkg"
            } |
            Remove-Item -Force
    }

    dotnet publish $project -c Release -r win-x64 --self-contained false -o $publishDir `
        -p:Version=$Version
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    dotnet vpk pack `
        --packId WPlayer.Desktop `
        --packTitle WPlayer `
        --packAuthors "Leon Björklund" `
        --packVersion $Version `
        --packDir $publishDir `
        --mainExe WPlayer.exe `
        --icon $icon `
        --framework net10-x64-desktop `
        --delta None `
        --noPortable true `
        --shortcuts StartMenuRoot `
        --outputDir $OutputDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet vpk pack failed with exit code $LASTEXITCODE."
    }

    $legacyFeed = Join-Path $OutputDir "RELEASES"
    if (Test-Path -LiteralPath $legacyFeed) {
        Remove-Item -LiteralPath $legacyFeed -Force
    }

    $generatedSetup = Join-Path $OutputDir "WPlayer.Desktop-win-Setup.exe"
    if (-not (Test-Path -LiteralPath $generatedSetup)) {
        throw "Velopack did not produce an installer."
    }

    Move-Item -LiteralPath $generatedSetup -Destination (Join-Path $OutputDir "WPlayer-Setup.exe")
    Remove-Item -LiteralPath (Join-Path $OutputDir "assets.win.json") -Force
}
finally {
    Pop-Location
}
