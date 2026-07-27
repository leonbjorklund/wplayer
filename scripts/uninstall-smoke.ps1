param([switch]$ConfirmDestructive)

$ErrorActionPreference = "Stop"

if (-not $ConfirmDestructive) {
    throw "Run only in a clean account or VM with -ConfirmDestructive. This uninstalls WPlayer and deletes its local data."
}

$installRoot = Join-Path $env:LOCALAPPDATA "WPlayer.Desktop"
$updateExe = Join-Path $installRoot "Update.exe"
$exe = Join-Path $installRoot "current\WPlayer.exe"
$dataRoot = Join-Path $env:LOCALAPPDATA "WPlayer"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "WPlayer.lnk"
$startShortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "WPlayer.lnk"

if (-not (Test-Path -LiteralPath $updateExe)) {
    throw "Installed WPlayer updater was not found at '$updateExe'."
}

if (-not (Test-Path -LiteralPath $dataRoot)) {
    throw "WPlayer local data was not found at '$dataRoot'; config cleanup cannot be verified."
}

$processes = @(Get-Process WPlayer -ErrorAction SilentlyContinue)
if ($processes.Count -ne 1 -or -not [string]::Equals($processes[0].Path, $exe, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected one installed WPlayer process at '$exe'."
}

$appsEntry = Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* -ErrorAction SilentlyContinue |
    Where-Object DisplayName -eq "WPlayer" |
    Select-Object -First 1
$runValue = (Get-ItemProperty -Path $runKey -Name WPlayer -ErrorAction SilentlyContinue).WPlayer
if (-not (Test-Path -LiteralPath $startShortcut) -or $null -eq $appsEntry -or $null -eq $runValue) {
    throw "The Start Menu shortcut, Apps & Features entry, and startup Run value must exist before testing uninstall."
}

Get-Process WPlayer -ErrorAction SilentlyContinue | Stop-Process -Force
$uninstall = Start-Process -FilePath $updateExe -ArgumentList "--uninstall", "--silent" -Wait -PassThru
if ($uninstall.ExitCode -ne 0) {
    throw "WPlayer uninstall failed with exit code $($uninstall.ExitCode)."
}

$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Seconds 1
    $appsEntry = Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* -ErrorAction SilentlyContinue |
        Where-Object DisplayName -eq "WPlayer" |
        Select-Object -First 1
    $runValue = (Get-ItemProperty -Path $runKey -Name WPlayer -ErrorAction SilentlyContinue).WPlayer
    $remaining = @(
        Get-Process WPlayer -ErrorAction SilentlyContinue
        Test-Path -LiteralPath $installRoot
        Test-Path -LiteralPath $dataRoot
        Test-Path -LiteralPath $desktopShortcut
        Test-Path -LiteralPath $startShortcut
        $null -ne $appsEntry
        $null -ne $runValue
    ) | Where-Object { $_ }
} while ($remaining -and (Get-Date) -lt $deadline)

$result = [pscustomobject]@{
    ProcessAbsent = -not [bool](Get-Process WPlayer -ErrorAction SilentlyContinue)
    InstallDirectoryAbsent = -not (Test-Path -LiteralPath $installRoot)
    LocalDataAbsent = -not (Test-Path -LiteralPath $dataRoot)
    DesktopShortcutAbsent = -not (Test-Path -LiteralPath $desktopShortcut)
    StartMenuShortcutAbsent = -not (Test-Path -LiteralPath $startShortcut)
    AppsEntryAbsent = $null -eq $appsEntry
    RunValueAbsent = $null -eq $runValue
}
$result | Format-List

if ($result.PSObject.Properties.Value -contains $false) {
    throw "WPlayer uninstall left one or more installed artifacts behind."
}
