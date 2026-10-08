param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.0',
    [string]$PublishDirectory = 'publish',
    [string]$OutputDirectory = 'artifacts/installer'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $versionParts = $Version.Split('.') | ForEach-Object { [int]$_ }
    if ($versionParts[0] -gt 255 -or $versionParts[1] -gt 255 -or $versionParts[2] -gt 65535) {
        throw 'MSI version must fit major.minor.build (255.255.65535).'
    }
    $publish = (Resolve-Path $PublishDirectory).Path
    foreach ($required in @('FluentControl.exe', 'FluentControl.dll', 'FluentControl.runtimeconfig.json', 'coreclr.dll', 'Microsoft.UI.Xaml.dll', 'Assets/FluentControl.ico')) {
        if (-not (Test-Path (Join-Path $publish $required))) { throw "Missing required offline payload: $required" }
    }
    if (-not ((Test-Path "$publish/resources.pri") -or (Test-Path "$publish/FluentControl.pri"))) { throw 'Application PRI is missing.' }
    $runtime = Get-Content "$publish/FluentControl.runtimeconfig.json" -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) { throw 'The MSI payload must contain its .NET runtime.' }
    if ((Test-Path "$publish/PresentationFramework.dll") -or (Test-Path "$publish/System.Windows.Forms.dll")) {
        throw 'Unused WindowsDesktop runtime detected. Use the WASAPI-only audio package and a clean publish folder.'
    }
    # Never blanket-delete DLLs/resources from publish. Exclude only debug symbols.
    $files = @(Get-ChildItem $publish -File -Recurse | Where-Object { $_.Extension -notin @('.pdb', '.dbg') } | Sort-Object FullName)
    New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
    $output = (Resolve-Path $OutputDirectory).Path
    $work = Join-Path $root "artifacts/obj/msi-$Version"
    New-Item $work -ItemType Directory -Force | Out-Null
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'WiX tool restore failed.' }
    dotnet tool run wix extension add WixToolset.UI.wixext/5.0.2
    if ($LASTEXITCODE -ne 0) { throw 'WiX UI extension restore failed.' }

    function StableId([string]$prefix, [string]$path) {
        $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($path.ToLowerInvariant().Replace('\', '/')))
        return $prefix + [Convert]::ToHexString($hash).Substring(0, 32)
    }
    function StableGuid([string]$path) {
        # Namespaced SHA-256 UUIDv8: the same installed relative path always
        # identifies the same per-user component, on every build machine.
        $name = 'FluentControl:per-user:x64:Programs/FluentControl/' + $path.ToLowerInvariant().Replace('\', '/')
        $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($name))
        [byte[]]$bytes = $hash[0..15]
        $bytes[7] = ($bytes[7] -band 0x0f) -bor 0x80
        $bytes[8] = ($bytes[8] -band 0x3f) -bor 0x80
        return [Guid]::new($bytes).ToString('D')
    }
    $ns = 'http://wixtoolset.org/schemas/v4/wxs'
    $doc = [Xml.XmlDocument]::new()
    function Element($parent, [string]$name, [hashtable]$attributes) {
        $node = $doc.CreateElement($name, $ns)
        foreach ($key in $attributes.Keys) { $node.SetAttribute($key, [string]$attributes[$key]) }
        [void]$parent.AppendChild($node)
        return $node
    }
    $wix = Element $doc 'Wix' @{}
    $fragment = Element $wix 'Fragment' @{}
    $install = Element $fragment 'DirectoryRef' @{ Id = 'INSTALLFOLDER' }
    $group = Element $fragment 'ComponentGroup' @{ Id = 'ApplicationFiles' }
    $directories = @{ '' = $install }
    $directoryIds = @{ '' = 'INSTALLFOLDER' }
    $manifest = @()
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\', '/')
        $parentPath = ''
        $segments = $relative.Split('/')
        for ($i = 0; $i -lt $segments.Length - 1; $i++) {
            $directoryPath = ($parentPath + '/' + $segments[$i]).TrimStart('/')
            if (-not $directories.ContainsKey($directoryPath)) {
                $id = StableId 'D' $directoryPath
                $directories[$directoryPath] = Element $directories[$parentPath] 'Directory' @{ Id = $id; Name = $segments[$i] }
                $directoryIds[$directoryPath] = $id
            }
            $parentPath = $directoryPath
        }
        $componentId = StableId 'C' $relative
        # HKCU keypaths make per-user installation correct; IDs are independent
        # of build paths/version so component identity remains stable on upgrade.
        $component = Element $directories[$parentPath] 'Component' @{ Id = $componentId; Guid = (StableGuid $relative) }
        $fileId = if ($relative -eq 'FluentControl.exe') { 'ApplicationExe' } else { StableId 'F' $relative }
        [void](Element $component 'File' @{ Id = $fileId; Source = $file.FullName; Name = $file.Name; KeyPath = 'no' })
        [void](Element $component 'RegistryValue' @{ Root = 'HKCU'; Key = 'Software\FluentControl\Installer\Files'; Name = $componentId; Type = 'integer'; Value = '1'; KeyPath = 'yes' })
        [void](Element $group 'ComponentRef' @{ Id = $componentId })
        $manifest += [ordered]@{ path = $relative; bytes = $file.Length; sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash }
    }
    # Remove only empty installed subdirectories. The user-data directory is
    # outside INSTALLFOLDER and is never part of the MSI component tree.
    $cleanup = Element $install 'Component' @{ Id = 'PayloadDirectoryCleanup'; Guid = '*' }
    [void](Element $cleanup 'RegistryValue' @{ Root = 'HKCU'; Key = 'Software\FluentControl\Installer'; Name = 'PayloadDirectories'; Type = 'integer'; Value = '1'; KeyPath = 'yes' })
    foreach ($entry in $directoryIds.GetEnumerator() | Where-Object { $_.Key -ne '' } | Sort-Object Key) {
        [void](Element $cleanup 'RemoveFolder' @{ Id = ('R' + $entry.Value); Directory = $entry.Value; On = 'uninstall' })
    }
    [void](Element $group 'ComponentRef' @{ Id = 'PayloadDirectoryCleanup' })
    $payload = Join-Path $work 'Payload.wxs'
    $doc.Save($payload)
    $manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $work 'payload.json') -Encoding utf8
    $msi = Join-Path $output "FluentControl-$Version-x64.msi"
    dotnet tool run wix build installer/Package.wxs installer/Interface.wxs $payload -arch x64 -culture zh-CN -loc installer/Strings.zh-cn.wxl -ext WixToolset.UI.wixext -d "ProductVersion=$Version" -d "PublishDir=$publish" -pdbtype none -intermediatefolder $work -o $msi
    if ($LASTEXITCODE -ne 0) { throw 'MSI compilation/validation failed.' }
    $size = (Get-Item $msi).Length
    $uncompressed = ($files | Measure-Object Length -Sum).Sum
    $report = [ordered]@{ version = $Version; msi = [IO.Path]::GetFileName($msi); bytes = $size; mib = [Math]::Round($size / 1MB, 2); payloadBytes = $uncompressed; files = $files.Count; sha256 = (Get-FileHash $msi -Algorithm SHA256).Hash }
    $report | ConvertTo-Json | Set-Content (Join-Path $work 'size-report.json') -Encoding utf8
    Write-Host ('MSI_RESULT ' + ($report | ConvertTo-Json -Compress))
    $files | Sort-Object Length -Descending | Select-Object -First 15 Name,Length | Format-Table -AutoSize
    if ($env:GITHUB_STEP_SUMMARY) {
        "### FluentControl $Version MSI`n`n- Single offline MSI: **$($report.mib) MiB** ($size bytes)`n- Installed payload: $([Math]::Round($uncompressed / 1MB, 2)) MiB / $($files.Count) files`n- SHA-256: ``$($report.sha256)```n" | Add-Content $env:GITHUB_STEP_SUMMARY
    }
}
finally { Pop-Location }
