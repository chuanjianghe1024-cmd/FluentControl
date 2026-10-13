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
    dotnet publish installer/FluentControl.Setup/FluentControl.Setup.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Setup frontend publication failed.' }
    $runtime = Get-Content "$publish/FluentControl.Setup.runtimeconfig.json" -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) { throw 'Setup must include its own offline runtime.' }
    foreach ($name in @('FluentControl.Setup.exe', 'mbanative.dll', 'coreclr.dll', 'PresentationFramework.dll', 'Wpf.Ui.dll')) {
        if (-not (Test-Path "$publish/$name")) { throw "Missing offline setup dependency: $name" }
    }
    if ($CertificateThumbprint) { & "$PSScriptRoot/Sign-Artifacts.ps1" -Path @("$publish/FluentControl.Setup.exe", "$publish/FluentControl.Setup.dll") @signing }
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
    dotnet tool run wix build installer/Bundle.wxs $payload -arch x64 -d "ProductVersion=$Version" -d "SetupDir=$publish" -d "MsiPath=$msi" -pdbtype none -intermediatefolder "$work/wix" -o $setup
    if ($LASTEXITCODE -ne 0) { throw 'Offline bundle compilation failed.' }
    if ($CertificateThumbprint) { & "$PSScriptRoot/Sign-Artifacts.ps1" -Path $setup @signing }
    if ((Get-FileHash $msi -Algorithm SHA256).Hash -ne $msiHash) { throw 'Bundling changed the MSI.' }
    $report = [ordered]@{ version = $Version; setup = [IO.Path]::GetFileName($setup); bytes = (Get-Item $setup).Length; sha256 = (Get-FileHash $setup -Algorithm SHA256).Hash; embeddedMsiSha256 = $msiHash; signature = [string](Get-AuthenticodeSignature $setup).Status }
    $report | ConvertTo-Json | Set-Content "$work/size-report.json" -Encoding utf8
    "$($report.sha256.ToLowerInvariant())  $($report.setup)" | Set-Content "$setup.sha256" -Encoding utf8NoBOM
    Write-Host ('SETUP_RESULT ' + ($report | ConvertTo-Json -Compress))
    if ($env:GITHUB_STEP_SUMMARY) {
        "### Fluent setup $Version`n`n- Offline EXE: $([Math]::Round($report.bytes / 1MB, 2)) MiB`n- SHA-256: ``$($report.sha256)```n- Embedded MSI unchanged; Authenticode: $($report.signature)`n" | Add-Content $env:GITHUB_STEP_SUMMARY
    }
}
finally { Pop-Location }
