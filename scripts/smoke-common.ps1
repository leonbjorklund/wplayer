function Uninstall-ExistingWPlayer {
    param([string]$UpdateExe)

    Get-Process WPlayer -ErrorAction SilentlyContinue | Stop-Process -Force

    if (Test-Path -LiteralPath $UpdateExe) {
        $uninstall = Start-Process -FilePath $UpdateExe -ArgumentList "--uninstall", "--silent" -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) {
            throw "WPlayer uninstall failed with exit code $($uninstall.ExitCode)."
        }

        Start-Sleep -Seconds 2
    }
}

function Wait-WPlayer {
    param([string]$FailureMessage = "WPlayer did not launch.")

    $deadline = (Get-Date).AddSeconds(45)
    do {
        $process = Get-Process WPlayer -ErrorAction SilentlyContinue
        if ($process) {
            return $process
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    throw $FailureMessage
}
