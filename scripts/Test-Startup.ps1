param([string]$PublishDirectory = "publish", [switch]$UiTest, [switch]$Background)
$ErrorActionPreference = "Stop"
$directory = (Resolve-Path $PublishDirectory).Path
$log = Join-Path $env:LOCALAPPDATA "FluentControl/Logs/startup.log"
$process = $null
$recovery = $null
try {
    $launch = @{ FilePath = (Join-Path $directory "FluentControl.exe"); WorkingDirectory = $directory; PassThru = $true }
    if ($UiTest) { $launch.ArgumentList = '--ui-test' }
    if ($Background) { $launch.ArgumentList = '--background' }
    $process = Start-Process @launch
    if ($Background) {
        $deadline = (Get-Date).AddSeconds(30)
        $inTray = $false
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 500
            $process.Refresh()
            if ($process.HasExited) { throw 'Background launch exited unexpectedly.' }
            if ((Test-Path $log) -and (Get-Content $log -Raw).Contains("[PID $($process.Id)] Main window started in tray")) {
                $inTray = $true
                break
            }
        }
        if (-not $inTray) { throw 'Background launch did not initialize the tray.' }
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) { throw 'Background launch unexpectedly displayed a window.' }
        $recovery = Start-Process -FilePath (Join-Path $directory 'FluentControl.exe') -WorkingDirectory $directory -PassThru
        if (-not $recovery.WaitForExit(10000)) { throw 'Second launch did not exit after signaling the existing instance.' }
    }
    # UI mode runs the complete hardware-fixture, audio, real-pointer and shell
    # regression suite after startup. Keep the normal startup gate at 30 seconds.
    $timeoutSeconds = if ($UiTest) { 60 } else { 30 }
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "FluentControl exited with code $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne 0 -and (Test-Path $log)) {
            $content = Get-Content $log -Raw
            if ($content.Contains("[PID $($process.Id)] Main window content loaded") -and
                $content.Contains("[PID $($process.Id)] Main window activated") -and
                (-not $UiTest -or $content.Contains("[PID $($process.Id)] UI smoke checks passed"))) {
                $ready = $true
                break
            }
        }
    }
    if (-not $ready) { throw "FluentControl did not complete startup$(if ($UiTest) { ' and UI regression checks' }) within $timeoutSeconds seconds." }
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) { throw "FluentControl crashed after showing the window." }
    if ($Background) {
        if (-not $process.CloseMainWindow()) { throw 'Could not send the close-window request.' }
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited -or $process.MainWindowHandle -ne 0) { throw 'Closing the window did not keep the application hidden in the tray.' }
        Write-Host 'PASS: background tray launch, single-instance recovery and close-to-tray.'
    }
    Write-Host "PASS: published FluentControl.exe displayed its main window and loaded XAML content. UI fixtures: $UiTest"
}
finally {
    if (Test-Path $log) { Get-Content $log }
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if ($null -ne $recovery -and -not $recovery.HasExited) { Stop-Process -Id $recovery.Id -Force }
}
