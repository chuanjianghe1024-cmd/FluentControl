$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('fc-wix-policy-' + [Guid]::NewGuid())
try {
    New-Item "$fixture/scripts", "$fixture/.config", "$fixture/publish" -ItemType Directory -Force | Out-Null
    Copy-Item "$root/scripts/Build-Msi.ps1" "$fixture/scripts/Build-Msi.ps1"
    # No publish payload exists: approved versions must reach payload validation;
    # unapproved versions must stop at the policy check, before any dotnet call.
    function dotnet { throw 'Unexpected tool execution during policy test.' }
    foreach ($version in @('5.0.2', '6.0.2', '8.0.0', '7.0.1-rc.1', '', '7.*', '7.0.0')) {
        @{ tools = @{ wix = @{ version = $version } } } | ConvertTo-Json -Depth 5 |
            Set-Content "$fixture/.config/dotnet-tools.json"
        $failure = $null
        try { & "$fixture/scripts/Build-Msi.ps1" -PublishDirectory "$fixture/publish" }
        catch { $failure = $_.Exception.Message }
        $expected = if ($version -eq '7.0.0') { 'Missing required offline payload:' } else { 'Unsupported WiX version' }
        if (-not $failure -or -not $failure.StartsWith($expected)) {
            throw "Unexpected result for '$version': $failure"
        }
        if (Test-Path "$fixture/artifacts") { throw 'Policy test generated build outputs.' }
    }
    Write-Host 'PASS: unreviewed WiX versions stop before tool execution; WiX 7.0.0 reaches payload validation.'
}
finally { Remove-Item $fixture -Recurse -Force }
