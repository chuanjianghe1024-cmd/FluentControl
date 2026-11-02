$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('fc-wix-policy-' + [Guid]::NewGuid())
try {
    New-Item "$fixture/scripts", "$fixture/.config", "$fixture/publish", "$fixture/installer/FluentControl.Setup" -ItemType Directory -Force | Out-Null
    Copy-Item "$root/scripts/Build-Msi.ps1" "$fixture/scripts/Build-Msi.ps1"
    Copy-Item "$root/scripts/Build-Setup.ps1" "$fixture/scripts/Build-Setup.ps1"
    # No publish payload exists: approved versions must reach payload validation;
    # unapproved versions must stop at the policy check, before any dotnet call.
    function dotnet { throw 'Unexpected tool execution during policy test.' }
    foreach ($version in @('5.0.2', '6.0.2', '8.0.0', '7.0.1-rc.1', '', '7.*', '7.0.0', '7.0.1')) {
        @{ tools = @{ wix = @{ version = $version } } } | ConvertTo-Json -Depth 5 |
            Set-Content "$fixture/.config/dotnet-tools.json"
        $failure = $null
        try { & "$fixture/scripts/Build-Msi.ps1" -PublishDirectory "$fixture/publish" }
        catch { $failure = $_.Exception.Message }
        $expected = if ($version -in @('7.0.0', '7.0.1')) { 'Missing required offline payload:' } else { 'Unsupported WiX version' }
        if (-not $failure -or -not $failure.StartsWith($expected)) {
            throw "Unexpected result for '$version': $failure"
        }
        if (Test-Path "$fixture/artifacts") { throw 'Policy test generated build outputs.' }
    }
    # Exercise both independently bumped PR combinations. Matching versions
    # must pass the version guard and reach signing validation; no MSI/COM,
    # restore, EULA acceptance or build output is needed for this regression.
    foreach ($case in @(
        @{ Tool = '7.0.1'; Api = '7.0.0' },
        @{ Tool = '7.0.0'; Api = '7.0.1' },
        @{ Tool = '7.0.0'; Api = '7.0.0' },
        @{ Tool = '7.0.1'; Api = '7.0.1' }
    )) {
        @{ tools = @{ wix = @{ version = $case.Tool } } } | ConvertTo-Json -Depth 5 |
            Set-Content "$fixture/.config/dotnet-tools.json"
        @"
<Project><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup><ItemGroup>
<PackageReference Include="WixToolset.BootstrapperApplicationApi" Version="$($case.Api)" />
</ItemGroup></Project>
"@ | Set-Content "$fixture/installer/FluentControl.Setup/FluentControl.Setup.csproj"
        $failure = $null
        try { & "$fixture/scripts/Build-Setup.ps1" -RequireSigned }
        catch { $failure = $_.Exception.Message }
        $expected = if ($case.Tool -eq $case.Api) {
            'A signed release requires -CertificateThumbprint and -TimestampUrl.'
        } else { 'The bootstrapper API and WiX tool must use the same version.' }
        if ($failure -ne $expected) {
            throw "Unexpected result for CLI $($case.Tool) / API $($case.Api): $failure"
        }
        if (Test-Path "$fixture/artifacts") { throw 'Version test generated build outputs.' }
    }
    Write-Host 'PASS: WiX policy and CLI/API alignment stop invalid combinations before tool execution; 7.0.0 and 7.0.1 reach payload/signing validation.'
}
finally { Remove-Item $fixture -Recurse -Force }
