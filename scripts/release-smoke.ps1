param([switch]$RequireValidSignature)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "smoke-common.ps1")

$root = Split-Path -Parent $PSScriptRoot
[xml]$projectXml = Get-Content -LiteralPath (Join-Path $root "WPlayer.csproj")
$Version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
$ReleaseDir = Join-Path $root "artifacts\releases"

$installRoot = Join-Path $env:LOCALAPPDATA "WPlayer.Desktop"
$installDir = Join-Path $installRoot "current"
$exe = Join-Path $installDir "WPlayer.exe"
$installedDll = Join-Path $installDir "WPlayer.dll"
$publishedDll = Join-Path $root "artifacts\publish\WPlayer-$Version\WPlayer.dll"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$config = Join-Path $env:LOCALAPPDATA "WPlayer\config.json"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "WPlayer.lnk"
$startShortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "WPlayer.lnk"
$updateExe = Join-Path $installRoot "Update.exe"

function Wait-RunValue {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $value = (Get-ItemProperty -Path $runKey -Name WPlayer -ErrorAction SilentlyContinue).WPlayer
        if ($value) {
            return $value
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    return $null
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class WPlayerDpiAwareness
{
    private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

    public static bool IsPerMonitorV2(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(hwnd), PerMonitorAwareV2);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
}
"@

function Wait-PerMonitorV2 {
    param([int]$ProcessId)

    $deadline = (Get-Date).AddSeconds(15)
    do {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        if ([WPlayerDpiAwareness]::IsPerMonitorV2($process.MainWindowHandle)) {
            return $true
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    return $false
}

Push-Location $root
try {
    & (Join-Path $PSScriptRoot "package-release.ps1") -Version $Version -OutputDir $ReleaseDir
    $setup = Join-Path $ReleaseDir "WPlayer-Setup.exe"
    if (-not (Test-Path -LiteralPath $setup)) {
        throw "Packaged installer was not found at '$setup'."
    }

    $setupSignature = Get-AuthenticodeSignature -LiteralPath $setup
    if ($RequireValidSignature -and $setupSignature.Status -ne "Valid") {
        throw "Installer signature is '$($setupSignature.Status)' instead of 'Valid'."
    }

    Uninstall-ExistingWPlayer -UpdateExe $updateExe

    $installStartedAt = Get-Date
    Start-Process -FilePath $setup

    $processes = @(Wait-WPlayer -FailureMessage "WPlayer did not launch after install.")
    if ($processes.Count -ne 1) {
        throw "Expected one newly installed WPlayer process, found $($processes.Count)."
    }

    $process = $processes[0]
    $path = $process.Path
    $installedProcessIsFresh = $process.StartTime -ge $installStartedAt
    if (-not $installedProcessIsFresh) {
        throw "WPlayer process predates the current installation."
    }

    if (-not [string]::Equals($path, $exe, [StringComparison]::OrdinalIgnoreCase)) {
        throw "WPlayer launched from '$path' instead of '$exe'."
    }

    $runValue = Wait-RunValue
    $file = Get-Item -LiteralPath $exe
    $installedCodeMatchesPackage = (Get-FileHash -LiteralPath $installedDll).Hash -eq
        (Get-FileHash -LiteralPath $publishedDll).Hash
    if (-not $installedCodeMatchesPackage) {
        throw "Installed WPlayer.dll does not match the current packaged build."
    }

    $installedProcessIsPerMonitorV2 = Wait-PerMonitorV2 -ProcessId $process.Id
    if (-not $installedProcessIsPerMonitorV2) {
        throw "Installed WPlayer window is not PerMonitorV2 DPI-aware."
    }

    if ($file.VersionInfo.ProductVersion -ne $Version) {
        throw "Installed product version '$($file.VersionInfo.ProductVersion)' does not match '$Version'."
    }

    $installSize = Get-ChildItem -LiteralPath $installDir -Recurse -File |
        Measure-Object -Sum Length
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    $appsEntry = Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* -ErrorAction SilentlyContinue |
        Where-Object DisplayName -eq "WPlayer" |
        Select-Object -First 1

    if ($null -eq $appsEntry) {
        throw "WPlayer is missing from Apps & Features."
    }

    if (-not (Test-Path -LiteralPath $startShortcut)) {
        throw "WPlayer Start Menu shortcut is missing."
    }

    $installedDesktopShortcutExists = (Test-Path -LiteralPath $desktopShortcut) -and
        [string]::Equals(
            (New-Object -ComObject WScript.Shell).CreateShortcut($desktopShortcut).TargetPath,
            $exe,
            [StringComparison]::OrdinalIgnoreCase)
    if ($installedDesktopShortcutExists) {
        throw "Unexpected WPlayer desktop shortcut exists."
    }

    if ($runValue -ne "`"$exe`"") {
        throw "WPlayer startup registration is missing or incorrect: '$runValue'."
    }

    if (-not (Test-Path -LiteralPath $config)) {
        throw "WPlayer config was not created at '$config'."
    }

    if ($RequireValidSignature -and $signature.Status -ne "Valid") {
        throw "Installed executable signature is '$($signature.Status)' instead of 'Valid'."
    }

    $cpu = $process.CPU
    Start-Sleep -Seconds 10
    $process = Get-Process -Id $process.Id -ErrorAction Stop

    [pscustomobject]@{
        Path = $path
        InstalledProcessIsFresh = $installedProcessIsFresh
        InstalledCodeMatchesPackage = $installedCodeMatchesPackage
        InstalledProcessIsPerMonitorV2 = $installedProcessIsPerMonitorV2
        AppsEntryOk = $null -ne $appsEntry
        StartMenuShortcutOk = Test-Path -LiteralPath $startShortcut
        InstalledDesktopShortcutAbsent = -not $installedDesktopShortcutExists
        RunKeyOk = $runValue -eq "`"$exe`""
        ConfigExists = Test-Path -LiteralPath $config
        RunKey = $runValue
        MainExeMB = [math]::Round($file.Length / 1MB, 1)
        InstalledDirMB = [math]::Round($installSize.Sum / 1MB, 1)
        FileVersion = $file.VersionInfo.FileVersion
        ProductVersion = $file.VersionInfo.ProductVersion
        SetupSignature = $setupSignature.Status
        Signature = $signature.Status
        CpuSecondsDelta = [math]::Round($process.CPU - $cpu, 3)
        WorkingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 1)
        PrivateMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        Handles = $process.HandleCount
    } | Format-List
}
finally {
    Pop-Location
}
