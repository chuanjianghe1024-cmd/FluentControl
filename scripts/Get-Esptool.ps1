$ErrorActionPreference = 'Stop'
$archive = Join-Path ([IO.Path]::GetTempPath()) 'fcs-esptool.zip'
Invoke-WebRequest 'https://github.com/espressif/esptool/releases/download/v5.1.0/esptool-v5.1.0-windows-amd64.zip' -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne 'F68A8F7728ADFC59CD60F9424928199E76EAC66372C7BDC23898AA32753A437A') { throw 'esptool archive hash mismatch' }
$unpack = Join-Path ([IO.Path]::GetTempPath()) ('fcs-esptool-' + [guid]::NewGuid().ToString('N'))
Expand-Archive $archive $unpack
$exe = @(Get-ChildItem $unpack -Recurse -Filter esptool.exe)
if ($exe.Count -ne 1) { throw 'Unexpected esptool archive layout' }
$target = Join-Path $PSScriptRoot '../tools/esptool'
New-Item $target -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $exe[0].DirectoryName '*') $target -Recurse -Force
# Keep the upstream GPL license with the redistributed executable.
Invoke-WebRequest 'https://raw.githubusercontent.com/espressif/esptool/v5.1.0/LICENSE' -OutFile (Join-Path $target 'LICENSE-esptool.txt')
& (Join-Path $target 'esptool.exe') version
if ($LASTEXITCODE -ne 0) { throw 'esptool smoke test failed' }
Remove-Item $unpack -Recurse -Force
