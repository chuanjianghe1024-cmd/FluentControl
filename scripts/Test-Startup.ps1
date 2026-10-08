param([string]$PublishDirectory = "publish", [switch]$UiTest)
$ErrorActionPreference = "Stop"
$directory = (Resolve-Path $PublishDirectory).Path
$log = Join-Path $env:LOCALAPPDATA "FluentControl/Logs/startup.log"
$process = $null
try {
    $launch = @{ FilePath = (Join-Path $directory "FluentControl.exe"); WorkingDirectory = $directory; PassThru = $true }
    if ($UiTest) { $launch.ArgumentList = '--ui-test' }
    $process = Start-Process @launch
    $deadline = (Get-Date).AddSeconds(30)
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
    if (-not $ready) { throw "FluentControl did not display its main window within 30 seconds." }
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) { throw "FluentControl crashed after showing the window." }
    Write-Host "PASS: published FluentControl.exe displayed its main window and loaded XAML content. UI fixtures: $UiTest"
}
finally {
    if (Test-Path $log) { Get-Content $log }
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
