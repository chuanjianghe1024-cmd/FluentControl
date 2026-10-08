param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$installed = $null
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
        if ($process.ExitCode -notin @(0, 3010)) {
            Get-Content $log -Tail 100
            throw "MSI failed: $name, exit $($process.ExitCode)."
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
    # The earlier package has the same payload, but a lower MSI version and a
    # different ProductCode, exercising Windows Installer's actual major upgrade.
    & "$PSScriptRoot/Build-Msi.ps1" -Version '0.0.1' -OutputDirectory 'artifacts/upgrade-fixture'
    $old = (Resolve-Path 'artifacts/upgrade-fixture/FluentControl-0.0.1-x64.msi').Path
    $current = (Resolve-Path "artifacts/installer/FluentControl-$Version-x64.msi").Path
    $oldCode = [Guid]::Parse((MsiProperty $old 'ProductCode')).ToString('B').ToUpperInvariant()
    $currentCode = [Guid]::Parse((MsiProperty $current 'ProductCode')).ToString('B').ToUpperInvariant()
    if ($oldCode -eq $currentCode) { throw 'Major upgrade packages must have different ProductCodes.' }
    RunMsi "/i `"$old`"" 'install'
    $installed = $oldCode
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
    RunMsi "/i `"$current`"" 'upgrade'
    $installed = $currentCode
    $uninstall = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    if (Test-Path "$uninstall\$oldCode") { throw 'Major upgrade left the old product installed.' }
    if ((Get-ItemProperty "$uninstall\$currentCode").DisplayVersion -ne $Version) { throw 'Installed version is incorrect.' }
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
    if ((Test-Path "$target/FluentControl.exe") -or (Test-Path $shortcut) -or (Test-Path $startup) -or (Test-Path "$uninstall\$currentCode")) {
        throw 'Uninstall left an executable, shortcut or registered product behind.'
    }
    AssertRetained
    foreach ($file in $saved) {
        if (-not (Test-Path $file.Path) -or (Get-FileHash $file.Path).Hash -ne $file.Hash) { throw 'Uninstall changed personal settings.' }
    }
    Write-Host 'PASS: uninstall removes application/startup shortcuts and retains personal configurations.'
}
finally {
    if ($installed) { RunMsi "/x $installed" 'cleanup' }
    if ($sentinel -and (Test-Path $sentinel)) { Remove-Item $sentinel }
    Pop-Location
}
