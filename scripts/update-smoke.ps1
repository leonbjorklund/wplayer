param([Parameter(Mandatory)][string]$SourceVersion)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "smoke-common.ps1")

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "WPlayer.csproj"
[xml]$projectXml = Get-Content -LiteralPath $project
$targetVersion = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
$releaseDir = Join-Path $root "artifacts\update-smoke\releases"
$installRoot = Join-Path $env:LOCALAPPDATA "WPlayer.Desktop"
$exe = Join-Path $installRoot "current\WPlayer.exe"
$installedDll = Join-Path $installRoot "current\WPlayer.dll"
$versionFile = Join-Path $installRoot "current\sq.version"
$updateExe = Join-Path $installRoot "Update.exe"

function Wait-WPlayerVersion {
    param([string]$VersionFile, [string]$Name, [string]$TargetVersion)

    $deadline = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Seconds 2
        $version = if (Test-Path -LiteralPath $VersionFile) {
            ([xml](Get-Content -Raw -LiteralPath $VersionFile)).package.metadata.version
        }
        else {
            ""
        }
    } while ($version -ne $TargetVersion -and (Get-Date) -lt $deadline)

    if ($version -ne $TargetVersion) {
        throw "$Name did not update to $TargetVersion within 90 seconds."
    }
}

Push-Location $root
try {
    Uninstall-ExistingWPlayer -UpdateExe $updateExe

    if (Test-Path -LiteralPath $releaseDir) {
        Remove-Item -LiteralPath $releaseDir -Recurse -Force
    }

    & (Join-Path $PSScriptRoot "package-release.ps1") -Version $SourceVersion -OutputDir $releaseDir
    $setup = Join-Path $releaseDir "WPlayer-Setup.exe"
    if (-not (Test-Path -LiteralPath $setup)) {
        throw "The $SourceVersion installer is missing."
    }

    Start-Process -FilePath $setup
    Wait-WPlayer | Out-Null
    Get-Process WPlayer -ErrorAction SilentlyContinue | Stop-Process -Force

    & (Join-Path $PSScriptRoot "package-release.ps1") -Version $targetVersion -OutputDir $releaseDir

    $previousSource = $env:WPLAYER_UPDATE_SOURCE
    $env:WPLAYER_UPDATE_SOURCE = $releaseDir
    try {
        $publishedDll = Join-Path $root "artifacts\publish\WPlayer-$targetVersion\WPlayer.dll"
        Start-Process -FilePath $exe
        Wait-WPlayerVersion -VersionFile $versionFile -Name "WPlayer" -TargetVersion $targetVersion

        $installedProcess = Wait-WPlayer
        $installedPath = $installedProcess.Path
        if (-not [string]::Equals($installedPath, $exe, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installed update launched from '$installedPath' instead of '$exe'."
        }

        if ((Get-FileHash -LiteralPath $installedDll).Hash -ne (Get-FileHash -LiteralPath $publishedDll).Hash) {
            throw "Installed update does not match the packaged $targetVersion code."
        }

        [pscustomobject]@{
            InstalledUpdatedTo = $targetVersion
            InstalledPath = $installedPath
        } | Format-List
    }
    finally {
        $env:WPLAYER_UPDATE_SOURCE = $previousSource
        Get-Process WPlayer -ErrorAction SilentlyContinue | Stop-Process -Force
        if (Test-Path -LiteralPath $exe) {
            Start-Process -FilePath $exe
        }
    }
}
finally {
    Pop-Location
}
