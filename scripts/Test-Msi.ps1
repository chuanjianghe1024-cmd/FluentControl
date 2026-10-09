param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$installed = $null
$unrelated = $null
$locked = $null
$probe = $null
try {
    $logs = Join-Path $root 'artifacts/msi-logs'
    New-Item $logs -ItemType Directory -Force | Out-Null
    $target = Join-Path $env:LOCALAPPDATA 'Programs/FluentControl'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'FluentControl.lnk'
    $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'FluentControl.lnk'
    $data = Join-Path $env:LOCALAPPDATA 'FluentControl'
    $sentinel = Join-Path $data 'installer-retention-check.txt'
    $marker = "Retain offline monitor profiles: $([Guid]::NewGuid())"
    New-Item $data -ItemType Directory -Force | Out-Null
    Set-Content $sentinel $marker -Encoding utf8
    function RunMsi([string]$arguments, [string]$name) {
        $log = Join-Path $logs "$name.log"
        $process = Start-Process msiexec.exe -ArgumentList "$arguments /qn /norestart /l*v `"$log`"" -PassThru
        if (-not $process.WaitForExit(120000)) { throw "MSI timed out: $name. See $log" }
        # No real payload lock is present in successful test operations; a
        # deferred reboot would hide an incomplete upgrade from the hash checks.
        if ($process.ExitCode -ne 0) {
            Get-Content $log -Tail 100
            throw "MSI failed: $name, exit $($process.ExitCode)."
        }
        if ($unrelated) {
            $unrelated.Refresh()
            if ($unrelated.HasExited) { throw "Installer closed the unrelated runtime fixture during $name." }
        }
    }
    function MsiProperty([string]$path, [string]$property) {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.OpenDatabase($path, 0)
        $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$property'")
        [void]$view.Execute()
        $record = $view.Fetch()
        $value = $record.StringData(1)
        [void]$view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
        return [string]$value
    }
    function AssertRetained {
        if ((Get-Content $sentinel -Raw).Trim() -ne $marker) { throw 'User data was removed/changed.' }
    }
    function AssertProduct([string]$code, [bool]$present, [string]$expectedVersion = '') {
        # Windows Installer owns registration context/registry-view details.
        # Query its API instead of assuming a particular ARP registry location.
        $installer = New-Object -ComObject WindowsInstaller.Installer
        try {
            $state = $installer.ProductState($code)
            if (-not $present) {
                if ($state -ne -1) { throw "Product remains registered: $code (state $state)." }
                return
            }
            if ($state -ne 5) { throw "Product is not fully installed: $code (state $state)." }
            if ($installer.ProductInfo($code, 'VersionString') -ne $expectedVersion) { throw 'Installed version is incorrect.' }
            if ($installer.ProductInfo($code, 'AssignmentType') -ne '0') { throw 'Installation must be per-user.' }
        }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) }
    }
    function StartLockFixture([string]$module, [string]$name) {
        $ready = Join-Path $fixtureDirectory "$name.ready"
        Remove-Item $ready -ErrorAction SilentlyContinue
        $process = Start-Process $fixture -ArgumentList "hold `"$module`" `"$ready`"" -PassThru
        $deadline = (Get-Date).AddSeconds(15)
        while ((Get-Date) -lt $deadline) {
            $process.Refresh()
            if ($process.HasExited) { throw "Lock fixture failed: $name, exit $($process.ExitCode)." }
            if (Test-Path $ready) { return $process }
            Start-Sleep -Milliseconds 100
        }
        Stop-Process -Id $process.Id -Force
        throw "Lock fixture did not open its window: $name."
    }
    function StopLockFixture($process) {
        if ($process -and -not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id -Force
                [void]$process.WaitForExit(5000)
            }
        }
    }
    # The earlier package has the same payload, but a lower MSI version and a
    # different ProductCode, exercising Windows Installer's actual major upgrade.
    & "$PSScriptRoot/Build-Msi.ps1" -Version '0.0.1' -OutputDirectory 'artifacts/upgrade-fixture'
    $old = (Resolve-Path 'artifacts/upgrade-fixture/FluentControl-0.0.1-x64.msi').Path
    $current = (Resolve-Path "artifacts/installer/FluentControl-$Version-x64.msi").Path
    if ((MsiProperty $current 'MSIRESTARTMANAGERCONTROL') -ne 'Disable') {
        throw 'The product MSI must not let Restart Manager close unrelated applications.'
    }
    # Reproduce the previous package policy in the unsigned, disposable old MSI.
    # Upgrading only from a fixture with the new property would miss old-product
    # removal behavior. Never mutate a published or signed installation package.
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($old, 1)
    $view = $database.OpenView("DELETE FROM ``Property`` WHERE ``Property``='MSIRESTARTMANAGERCONTROL'")
    [void]$view.Execute()
    [void]$view.Close()
    [void]$database.Commit()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    $fixtureDirectory = Join-Path $logs 'lock-fixture'
    New-Item $fixtureDirectory -ItemType Directory -Force | Out-Null
    $fixture = Join-Path $fixtureDirectory 'InstallerLockFixture.exe'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $fixtureSource = (Resolve-Path 'tests/installer/InstallerLockFixture.cs').Path
    & $compiler /nologo /target:winexe /platform:x64 "/out:$fixture" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $fixtureSource
    if ($LASTEXITCODE -ne 0) { throw 'Installer lock fixture compilation failed.' }
    # Same native DLL bytes/name, separate directory and file identity. Other
    # applications can load their own runtime throughout all MSI operations.
    $unrelatedModule = Join-Path $fixtureDirectory 'coreclr.dll'
    Copy-Item 'publish/coreclr.dll' $unrelatedModule -Force
    $unrelated = StartLockFixture $unrelatedModule 'unrelated'
    $oldCode = [Guid]::Parse((MsiProperty $old 'ProductCode')).ToString('B').ToUpperInvariant()
    $currentCode = [Guid]::Parse((MsiProperty $current 'ProductCode')).ToString('B').ToUpperInvariant()
    if ($oldCode -eq $currentCode) { throw 'Major upgrade packages must have different ProductCodes.' }
    RunMsi "/i `"$old`"" 'install'
    $installed = $oldCode
    AssertProduct $oldCode $true '0.0.1'
    if (-not (Test-Path "$target/FluentControl.exe")) { throw 'The per-user installation path is incorrect.' }
    if (-not (Test-Path $shortcut)) { throw 'Start-menu shortcut is missing.' }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    if ($link.TargetPath -ne (Join-Path $target 'FluentControl.exe')) { throw 'Start-menu shortcut target is incorrect.' }
    # Simulate the application's opt-in login shortcut; upgrade must keep it.
    $loginLink = $shell.CreateShortcut($startup)
    $loginLink.TargetPath = Join-Path $target 'FluentControl.exe'
    $loginLink.Arguments = '--background'
    $loginLink.Save()
    AssertRetained
    Write-Host 'PASS: MSI installs per-user and registers the correct start-menu shortcut.'
    $beforeLockedUpgrade = (Get-FileHash "$target/coreclr.dll" -Algorithm SHA256).Hash
    $locked = StartLockFixture "$target/coreclr.dll" 'installed'
    $probeResult = Join-Path $logs 'locked-upgrade-result.txt'
    $probeLog = Join-Path $logs 'locked-upgrade.log'
    $probe = Start-Process $fixture -ArgumentList "probe `"$current`" `"$probeResult`" `"$probeLog`"" -PassThru
    if (-not $probe.WaitForExit(120000)) { throw 'Locked upgrade failed to finish/cancel.' }
    if ($probe.ExitCode -ne 0 -or -not (Test-Path $probeResult)) { throw 'Locked upgrade probe failed.' }
    $result = Get-Content $probeResult -Raw
    Write-Host $result
    if ($result -notmatch '(?m)^Result=1602\r?$' -or $result -notmatch '(?m)^FilesInUse=' -or $result -match 'RMFilesInUse=') {
        throw 'A genuine loaded payload must trigger standard FilesInUse and cancel safely.'
    }
    AssertProduct $oldCode $true '0.0.1'
    AssertProduct $currentCode $false
    AssertRetained
    if ((Get-FileHash "$target/coreclr.dll" -Algorithm SHA256).Hash -ne $beforeLockedUpgrade) {
        throw 'Canceled locked upgrade changed the installed runtime.'
    }
    if ($locked.HasExited -or $unrelated.HasExited) { throw 'Canceled upgrade closed a lock fixture.' }
    StopLockFixture $locked
    $locked = $null
    Write-Host 'PASS: real payload lock prompts FilesInUse; cancel preserves the old product, files and running processes.'
    RunMsi "/i `"$current`"" 'upgrade'
    $installed = $currentCode
    AssertProduct $oldCode $false
    AssertProduct $currentCode $true $Version
    if (-not (Test-Path $startup)) { throw 'Upgrade removed the opt-in startup shortcut.' }
    AssertRetained
    $manifest = Get-Content "artifacts/obj/msi-$Version/payload.json" -Raw | ConvertFrom-Json
    foreach ($file in $manifest) {
        $path = Join-Path $target $file.path
        if (-not (Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "Installed payload is missing or differs: $($file.path)"
        }
    }
    Write-Host 'PASS: major upgrade replaces the old product, preserves data/startup and installs the complete payload.'
    & "$PSScriptRoot/Test-Startup.ps1" -PublishDirectory $target
    & "$PSScriptRoot/Test-Startup.ps1" -PublishDirectory $target -UiTest
    & "$PSScriptRoot/Test-Startup.ps1" -PublishDirectory $target -Background
    # Confirm real application settings also survive uninstall byte-for-byte.
    $saved = @(Get-ChildItem $data -File -Filter '*.json' | ForEach-Object { @{ Path = $_.FullName; Hash = (Get-FileHash $_.FullName).Hash } })
    RunMsi "/x $currentCode" 'uninstall'
    $installed = $null
    AssertProduct $currentCode $false
    if ((Test-Path "$target/FluentControl.exe") -or (Test-Path $shortcut) -or (Test-Path $startup)) {
        throw 'Uninstall left an executable, shortcut or registered product behind.'
    }
    AssertRetained
    foreach ($file in $saved) {
        if (-not (Test-Path $file.Path) -or (Get-FileHash $file.Path).Hash -ne $file.Hash) { throw 'Uninstall changed personal settings.' }
    }
    Write-Host 'PASS: uninstall removes application/startup shortcuts and retains personal configurations.'
    Write-Host 'PASS: unrelated application with its own runtime survives install, major upgrade and uninstall.'
}
finally {
    StopLockFixture $locked
    StopLockFixture $probe
    if ($installed) { RunMsi "/x $installed" 'cleanup' }
    StopLockFixture $unrelated
    if ($sentinel -and (Test-Path $sentinel)) { Remove-Item $sentinel }
    Pop-Location
}
