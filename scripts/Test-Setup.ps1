param([Parameter(Mandatory)][string]$Version, [switch]$SmokeOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $logs = Join-Path $root 'artifacts/setup-logs'
    New-Item $logs -ItemType Directory -Force | Out-Null
    $setup = (Resolve-Path "artifacts/installer/FluentControl-$Version-x64-Setup.exe").Path
    $target = Join-Path $env:LOCALAPPDATA 'FC setup checks/自定义 安装/FluentControl'
    $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'FluentControl.lnk'
    $sentinel = Join-Path $env:LOCALAPPDATA 'FluentControl/setup-retention.txt'
    $marker = [Guid]::NewGuid().ToString()
    Set-Content $sentinel $marker
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $framework = Split-Path $compiler -Parent
    $fixture = Join-Path $logs 'SetupWizardFixture.exe'
    $source = (Resolve-Path 'tests/installer/SetupWizardFixture.cs').Path
    & $compiler /nologo /target:exe /platform:x64 "/out:$fixture" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" "/reference:$framework\WPF\WindowsBase.dll" $source
    if ($LASTEXITCODE -ne 0) { throw 'Setup UI fixture compilation failed.' }
    function RunQuiet([string]$exe, [string]$action, [string]$name) {
        $p = Start-Process $exe -ArgumentList "/quiet /norestart $action /log `"$logs/$name.log`"" -PassThru
        if (-not $p.WaitForExit(120000) -or $p.ExitCode -ne 0) { throw "Setup failed: $name, exit $($p.ExitCode)." }
        if (Get-Process FluentControl -ErrorAction SilentlyContinue) { throw 'Quiet setup launched the app.' }
    }
    function Wizard([string]$mode, [string]$before, [string]$after, [string]$launch) {
        & $fixture $setup $target "$logs/$mode" $mode $before $after $launch
        if ($LASTEXITCODE -ne 0) { throw "Fluent setup UI failed: $mode." }
    }
    function VerifyInstall([bool]$startupEnabled) {
        if ((Get-Content $sentinel -Raw).Trim() -ne $marker) { throw 'Setup changed personal configuration.' }
        if ((Test-Path $startup) -ne $startupEnabled) { throw 'Startup selection was not applied.' }
        $actual = (Get-ItemProperty 'HKCU:/Software/FluentControl/Installer').InstallDirectory
        if ($actual.TrimEnd('\') -ne $target.TrimEnd('\')) { throw 'Setup changed installation directory.' }
        $manifest = Get-Content "artifacts/obj/msi-$Version/payload.json" -Raw | ConvertFrom-Json
        foreach ($file in $manifest) {
            if ((Get-FileHash (Join-Path $target $file.path) -Algorithm SHA256).Hash -ne $file.sha256) { throw "Installed payload differs: $($file.path)" }
        }
    }
    function VerifyRemoved {
        if ((Test-Path "$target/FluentControl.exe") -or (Test-Path $startup)) { throw 'Uninstall left application/startup files.' }
        if ((Get-Content $sentinel -Raw).Trim() -ne $marker) { throw 'Uninstall changed personal configuration.' }
    }
    Wizard 'cancel' '0' '1' '0'
    if (Test-Path "$target/FluentControl.exe") { throw 'Cancelling the options page installed the app.' }
    if ($SmokeOnly) { Write-Host 'PASS: actual offline bundle opens its Fluent UI and cancels without installing.'; return }
    Wizard 'fresh' '0' '1' '1'
    VerifyInstall $true
    # Stop only the fixture-launched app in this disposable CI profile.
    Get-Process FluentControl | Stop-Process
    Remove-Item $startup
    RunQuiet $setup '/repair' 'repair-after-app-opt-out'
    VerifyInstall $false
    $lockReady = Join-Path $logs 'lock.ready'
    $lockExe = (Resolve-Path 'artifacts/msi-logs/lock-fixture/InstallerLockFixture.exe').Path
    $lock = Start-Process $lockExe -ArgumentList "hold `"$target/coreclr.dll`" `"$lockReady`"" -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(15)
        while (-not (Test-Path $lockReady) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (-not (Test-Path $lockReady)) { throw 'Setup lock fixture did not start.' }
        Wizard 'locked-uninstall' '0' '0' '0'
        if ($lock.HasExited) { throw 'Setup terminated the locked process.' }
        VerifyInstall $false
    }
    finally { if ($lock -and -not $lock.HasExited) { Stop-Process -Id $lock.Id } }
    Wizard 'maintenance' '0' '0' '0'
    VerifyRemoved

    # Actual formal MSI -> EXE migration, with a non-default Chinese path.
    $release = Get-Content '.github/release.json' -Raw | ConvertFrom-Json
    $baseline = (Resolve-Path "artifacts/msi-logs/$($release.fileName)").Path
    if ((Get-FileHash $baseline -Algorithm SHA256).Hash -ne $release.sha256) { throw 'Baseline digest mismatch.' }
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$baseline`" /qn /norestart INSTALLFOLDER=`"$target`" START_WITH_WINDOWS=1 /l*v `"$logs/baseline.log`"" -PassThru
    if (-not $p.WaitForExit(120000) -or $p.ExitCode -ne 0) { throw 'Baseline installation failed.' }
    Wizard 'upgrade' '1' '0' '0'
    VerifyInstall $false
    RunQuiet $setup '/uninstall' 'uninstall-migrated-msi'
    VerifyRemoved

    # Rebuild the previously verified self-contained .NET 8 frontend at its
    # immutable source commit. This tests migration across the runtime change,
    # including removal of the old cached BA, not just new -> new packaging.
    $legacyCommit = '3c4fd557c308e1a2c4394090ba09a51c77e76077'
    $legacySource = Join-Path $root 'artifacts/setup-legacy-source'
    git fetch --no-tags --depth=1 https://github.com/chuanjianghe1024-cmd/FluentControl.git $legacyCommit
    if ($LASTEXITCODE -ne 0) { throw 'Cannot fetch the immutable legacy setup fixture.' }
    git worktree add --detach $legacySource $legacyCommit
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the legacy setup fixture checkout.' }
    try {
        $fixtureMsi = (Resolve-Path 'artifacts/upgrade-fixture/FluentControl-0.0.1-x64.msi').Path
        & "$legacySource/scripts/Build-Setup.ps1" -Version '0.0.1' -MsiPath $fixtureMsi -OutputDirectory "$root/artifacts/setup-fixture"
    }
    finally {
        git worktree remove --force $legacySource
        if ($LASTEXITCODE -ne 0) { throw 'Cannot clean the legacy setup fixture checkout.' }
    }
    $old = (Resolve-Path 'artifacts/setup-fixture/FluentControl-0.0.1-x64-Setup.exe').Path
    RunQuiet $old '' 'old-bundle-install'
    $target = Join-Path $env:LOCALAPPDATA 'Programs/FluentControl'
    RunQuiet $setup '' 'bundle-upgrade'
    VerifyInstall $false
    $downgrade = Start-Process $old -ArgumentList "/quiet /norestart /log `"$logs/downgrade.log`"" -PassThru
    if (-not $downgrade.WaitForExit(60000) -or $downgrade.ExitCode -ne 1638) { throw 'An older bundle did not reject downgrade.' }
    VerifyInstall $false
    RunQuiet $setup '/uninstall' 'bundle-upgrade-uninstall'
    VerifyRemoved
    $registeredBundles = Get-ChildItem 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall' -ErrorAction SilentlyContinue |
        Get-ItemProperty | Where-Object { $_.BundleUpgradeCode -contains '{FECCF0F0-0D94-4C6A-8D7B-78D861E03CC4}' }
    if ($registeredBundles) { throw 'A removed setup bundle remains registered.' }
    Write-Host 'PASS: Fluent UI, themes, language persistence, cancellation, launch, repair, MSI migration, .NET 8-to-Framework bundle upgrade, startup choice, payload hashes and uninstall.'
}
finally {
    if ($sentinel -and (Test-Path $sentinel)) { Remove-Item $sentinel }
    Pop-Location
}
