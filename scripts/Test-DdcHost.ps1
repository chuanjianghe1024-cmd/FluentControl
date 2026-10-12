param([string]$PublishDirectory = 'publish')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = (Resolve-Path (Join-Path $root $PublishDirectory)).Path
$parent = Get-Process -Id $PID
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $folder 'FluentControl.DdcHost.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.ArgumentList.Add([string]$PID)
$start.ArgumentList.Add([string]$parent.StartTime.ToUniversalTime().Ticks)
$worker = [Diagnostics.Process]::Start($start)
try {
    # No open command: the shipped worker must reject a stale/unknown handle,
    # without enumerating hardware or writing a physical monitor.
    $worker.StandardInput.WriteLine('{"Operation":"read","Monitor":0,"Code":16}')
    $worker.StandardInput.Flush()
    $reply = $worker.StandardOutput.ReadLineAsync()
    if (-not $reply.Wait(10000)) { throw 'Packaged DDC worker did not respond.' }
    $data = $reply.Result | ConvertFrom-Json
    if ($data.Error -ne -1071241844) { throw "Unexpected invalid-handle result: $($reply.Result)" }
    $worker.StandardInput.Close()
    if (-not $worker.WaitForExit(5000) -or $worker.ExitCode -ne 0) { throw 'DDC worker did not exit after its pipe closed.' }
    Write-Host 'PASS: packaged isolated DDC host starts, rejects unknown handles and exits; no physical writes.'
}
finally {
    if (-not $worker.HasExited) { $worker.Kill($true) }
    $worker.Dispose()
}
