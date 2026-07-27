param(
    [ValidateRange(0, [int]::MaxValue)]
    [int]$WarmupSeconds = 120,
    [ValidateRange(10, [int]::MaxValue)]
    [int]$DurationSeconds = 480
)

$ErrorActionPreference = "Stop"

$expectedPath = Join-Path $env:LOCALAPPDATA "WPlayer.Desktop\current\WPlayer.exe"
$processes = @(Get-Process WPlayer -ErrorAction SilentlyContinue)
if ($processes.Count -ne 1) {
    throw "Expected one installed WPlayer process, found $($processes.Count)."
}

$process = $processes[0]
if (-not [string]::Equals($process.Path, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "WPlayer is running from '$($process.Path)' instead of '$expectedPath'."
}

Start-Sleep -Seconds $WarmupSeconds
$startedAt = Get-Date
$samples = [Collections.Generic.List[object]]::new()

while ($true) {
    $process = Get-Process -Id $process.Id -ErrorAction Stop
    $elapsedSeconds = ((Get-Date) - $startedAt).TotalSeconds
    $samples.Add([pscustomobject]@{
        ElapsedSeconds = $elapsedSeconds
        CpuSeconds = $process.CPU
        Handles = $process.HandleCount
        PrivateMB = $process.PrivateMemorySize64 / 1MB
    })

    if ($elapsedSeconds -ge $DurationSeconds) {
        break
    }

    Start-Sleep -Seconds ([math]::Min(5, $DurationSeconds - $elapsedSeconds))
}

$first = $samples[0]
$last = $samples[$samples.Count - 1]
$windowSize = [math]::Max(1, [math]::Ceiling($samples.Count * 0.2))
$early = @($samples | Select-Object -First $windowSize)
$final = @($samples | Select-Object -Last $windowSize)
$finalHalfSize = [math]::Max(1, [math]::Floor($final.Count / 2))
$finalEarly = @($final | Select-Object -First $finalHalfSize)
$finalLate = @($final | Select-Object -Last $finalHalfSize)
$elapsedSeconds = $last.ElapsedSeconds - $first.ElapsedSeconds
$cpuPercent = (($last.CpuSeconds - $first.CpuSeconds) / $elapsedSeconds) * 100
$earlyHandles = ($early | Measure-Object Handles -Average).Average
$finalHandles = ($final | Measure-Object Handles -Average).Average
$overallHandleDelta = $finalHandles - $earlyHandles
$finalEarlyHandles = ($finalEarly | Measure-Object Handles -Average).Average
$finalLateHandles = ($finalLate | Measure-Object Handles -Average).Average
$earlyPrivate = ($early | Measure-Object PrivateMB -Average).Average
$finalPrivate = ($final | Measure-Object PrivateMB -Average).Average
$handleWindowDelta = $finalLateHandles - $finalEarlyHandles
$privateWindowDeltaMb = $finalPrivate - $earlyPrivate
$passed = $cpuPercent -lt 1 -and $handleWindowDelta -le 10 -and $privateWindowDeltaMb -le 20

[pscustomobject]@{
    Passed = $passed
    Path = $process.Path
    ProcessId = $process.Id
    WarmupSeconds = $WarmupSeconds
    DurationSeconds = [math]::Round($elapsedSeconds)
    TotalSeconds = [math]::Round($WarmupSeconds + $elapsedSeconds)
    SampleCount = $samples.Count
    SampleIntervalSeconds = 5
    CpuPercent = [math]::Round($cpuPercent, 3)
    MinHandles = ($samples | Measure-Object Handles -Minimum).Minimum
    MaxHandles = ($samples | Measure-Object Handles -Maximum).Maximum
    EarlyHandles = [math]::Round($earlyHandles, 1)
    FinalHandles = [math]::Round($finalHandles, 1)
    OverallHandleDelta = [math]::Round($overallHandleDelta, 1)
    HandleWindowDelta = [math]::Round($handleWindowDelta, 1)
    MinPrivateMB = [math]::Round(($samples | Measure-Object PrivateMB -Minimum).Minimum, 1)
    MaxPrivateMB = [math]::Round(($samples | Measure-Object PrivateMB -Maximum).Maximum, 1)
    EarlyPrivateMB = [math]::Round($earlyPrivate, 1)
    FinalPrivateMB = [math]::Round($finalPrivate, 1)
    PrivateWindowDeltaMB = [math]::Round($privateWindowDeltaMb, 1)
} | Format-List

if (-not $passed) {
    throw "WPlayer exceeded the local resource budget."
}
