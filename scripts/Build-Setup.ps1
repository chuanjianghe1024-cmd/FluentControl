param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.0',
    [string]$MsiPath,
    [string]$OutputDirectory = 'artifacts/installer',
    [string]$CertificateThumbprint,
    [uri]$TimestampUrl,
    [switch]$MachineStore,
    [string]$SignToolPath,
    [switch]$RequireSigned
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $wixVersion = [string](Get-Content '.config/dotnet-tools.json' -Raw | ConvertFrom-Json).tools.wix.version
    if ($wixVersion -notmatch '^7\.\d+\.\d+$') { throw "Unsupported WiX version '$wixVersion'. Only WiX 7 is approved." }
    [xml]$project = Get-Content 'installer/FluentControl.Setup/FluentControl.Setup.csproj'
    if ($project.Project.PropertyGroup.TargetFramework -ne 'net48') { throw 'Setup must use the Windows-provided .NET Framework 4.8.' }
    $apiVersion = ($project.Project.ItemGroup.PackageReference | Where-Object Include -eq 'WixToolset.BootstrapperApplicationApi').Version
    if ($apiVersion -ne $wixVersion) { throw 'The bootstrapper API and WiX tool must use the same version.' }
    if ($RequireSigned -and -not $CertificateThumbprint) { throw 'A signed release requires -CertificateThumbprint and -TimestampUrl.' }
    if ($CertificateThumbprint -and -not $TimestampUrl) { throw 'Provide a RFC 3161 -TimestampUrl.' }
    $signing = @{ CertificateThumbprint = $CertificateThumbprint; TimestampUrl = $TimestampUrl; MachineStore = $MachineStore; SignToolPath = $SignToolPath }
    if (-not $MsiPath) { $MsiPath = "artifacts/installer/FluentControl-$Version-x64.msi" }
    $msi = (Resolve-Path $MsiPath).Path
    # Do not wrap a different version or silently mix signed/unsigned inputs.
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($msi, 0)
    $view = $database.OpenView('SELECT `Value` FROM `Property` WHERE `Property`=''ProductVersion''')
    [void]$view.Execute(); $record = $view.Fetch()
    $msiVersion = $record.StringData(1)
    [void]$view.Close()
    foreach ($com in @($record, $view, $database, $installer)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) }
    if ($msiVersion -ne $Version) { throw 'Setup and MSI versions differ.' }
    if ($RequireSigned -and (Get-AuthenticodeSignature $msi).Status -ne 'Valid') { throw 'The embedded MSI must already be signed.' }
    $msiHash = (Get-FileHash $msi -Algorithm SHA256).Hash
    $work = Join-Path $root "artifacts/obj/setup-$Version"
    $publish = Join-Path $work 'publish'
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    New-Item $publish, $OutputDirectory -ItemType Directory -Force | Out-Null
    $output = (Resolve-Path $OutputDirectory).Path
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'WiX restore failed.' }
    dotnet tool run wix eula accept wix7
    if ($LASTEXITCODE -ne 0) { throw 'WiX 7 EULA acceptance failed.' }
    dotnet publish installer/FluentControl.Setup/FluentControl.Setup.csproj -c Release -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Setup frontend publication failed.' }
    # The framework is an OS prerequisite, never a second runtime in the EXE.
    foreach ($name in @('FluentControl.Setup.exe', 'FluentControl.Setup.exe.config', 'mbanative.dll', 'Wpf.Ui.dll')) {
        if (-not (Test-Path "$publish/$name")) { throw "Missing setup dependency: $name" }
    }
    $forbidden = @(Get-ChildItem $publish -File -Recurse | Where-Object {
        $_.Name -in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'mscorlib.dll') -or
        $_.Name -like '*.runtimeconfig.json'
    })
    if ($forbidden) { throw "Setup unexpectedly bundles a runtime: $($forbidden.Name -join ', ')" }
    [xml]$runtimeConfig = Get-Content "$publish/FluentControl.Setup.exe.config"
    if ($runtimeConfig.configuration.startup.supportedRuntime.sku -ne '.NETFramework,Version=v4.8') { throw 'Setup runtime configuration is not .NET Framework 4.8.' }
    $frontendFiles = @(Get-ChildItem $publish -File -Recurse | Where-Object Extension -ne '.pdb')
    $frontendBytes = ($frontendFiles | Measure-Object Length -Sum).Sum
    if ($frontendBytes -gt 20MB) { throw "Setup frontend exceeds the 20 MiB uncompressed budget: $frontendBytes bytes." }
    if ($CertificateThumbprint) { & "$PSScriptRoot/Sign-Artifacts.ps1" -Path "$publish/FluentControl.Setup.exe" @signing }
    $ns = 'http://wixtoolset.org/schemas/v4/wxs'
    $doc = [Xml.XmlDocument]::new()
    $wix = $doc.CreateElement('Wix', $ns); [void]$doc.AppendChild($wix)
    $fragment = $doc.CreateElement('Fragment', $ns); [void]$wix.AppendChild($fragment)
    $group = $doc.CreateElement('PayloadGroup', $ns); $group.SetAttribute('Id', 'SetupPayload'); [void]$fragment.AppendChild($group)
    $files = @(Get-ChildItem $publish -File -Recurse | Where-Object { $_.Extension -ne '.pdb' -and $_.FullName -ne "$publish\FluentControl.Setup.exe" } | Sort-Object FullName)
    foreach ($file in $files) {
        $node = $doc.CreateElement('Payload', $ns)
        $node.SetAttribute('SourceFile', $file.FullName)
        $node.SetAttribute('Name', [IO.Path]::GetRelativePath($publish, $file.FullName))
        [void]$group.AppendChild($node)
    }
    $payload = Join-Path $work 'SetupPayload.wxs'; $doc.Save($payload)
    $setup = Join-Path $output "FluentControl-$Version-x64-Setup.exe"
    dotnet tool run wix build installer/Bundle.wxs $payload -arch x64 -d "ProductVersion=$Version" -d "SetupDir=$publish" -d "MsiPath=$msi" -defaultcompressionlevel high -pdbtype none -intermediatefolder "$work/wix" -o $setup
    if ($LASTEXITCODE -ne 0) { throw 'Offline bundle compilation failed.' }
    if ($CertificateThumbprint) {
        # Burn caches its detached engine for maintenance. Sign both the engine
        # and the final attached bundle, not just the outer download file.
        $engine = Join-Path $work 'engine.exe'
        $reattached = Join-Path $work 'reattached.exe'
        dotnet tool run wix burn detach $setup -engine $engine
        if ($LASTEXITCODE -ne 0) { throw 'Burn engine detach failed.' }
        & "$PSScriptRoot/Sign-Artifacts.ps1" -Path $engine @signing
        dotnet tool run wix burn reattach $setup -engine $engine -o $reattached
        if ($LASTEXITCODE -ne 0) { throw 'Signed Burn engine reattachment failed.' }
        Move-Item $reattached $setup -Force
        & "$PSScriptRoot/Sign-Artifacts.ps1" -Path $setup @signing
    }
    if ((Get-FileHash $msi -Algorithm SHA256).Hash -ne $msiHash) { throw 'Bundling changed the MSI.' }
    $msiBytes = (Get-Item $msi).Length
    $setupBytes = (Get-Item $setup).Length
    $overheadBytes = $setupBytes - $msiBytes
    if ($overheadBytes -gt 12MB) { throw "Setup overhead exceeds the 12 MiB budget: $overheadBytes bytes." }
    $frontendFiles | Sort-Object Length -Descending | Select-Object @{Name='path';Expression={[IO.Path]::GetRelativePath($publish, $_.FullName)}}, Length |
        ConvertTo-Json | Set-Content "$work/frontend-payload.json" -Encoding utf8
    $report = [ordered]@{ version = $Version; setup = [IO.Path]::GetFileName($setup); bytes = (Get-Item $setup).Length; sha256 = (Get-FileHash $setup -Algorithm SHA256).Hash; embeddedMsiSha256 = $msiHash; msiBytes = $msiBytes; overheadBytes = $overheadBytes; frontendBytes = $frontendBytes; frontendRuntime = 'net48 (Windows)'; compression = 'high'; signature = [string](Get-AuthenticodeSignature $setup).Status }
    $report | ConvertTo-Json | Set-Content "$work/size-report.json" -Encoding utf8
    "$($report.sha256.ToLowerInvariant())  $($report.setup)" | Set-Content "$setup.sha256" -Encoding utf8NoBOM
    Write-Host ('SETUP_RESULT ' + ($report | ConvertTo-Json -Compress))
    if ($env:GITHUB_STEP_SUMMARY) {
        "### Fluent setup $Version`n`n- Offline EXE: $([Math]::Round($report.bytes / 1MB, 2)) MiB`n- MSI: $([Math]::Round($msiBytes / 1MB, 2)) MiB; wrapper overhead: $([Math]::Round($overheadBytes / 1MB, 2)) MiB`n- Frontend: .NET Framework 4.8 from Windows; high compression`n- SHA-256: ``$($report.sha256)```n- Embedded MSI unchanged; Authenticode: $($report.signature)`n" | Add-Content $env:GITHUB_STEP_SUMMARY
    }
}
finally { Pop-Location }
