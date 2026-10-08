param(
    [Parameter(Mandatory)][string[]]$Path,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [Parameter(Mandatory)][uri]$TimestampUrl,
    [switch]$MachineStore,
    [string]$SignToolPath
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Code signing requires Windows and a code-signing certificate.' }
if (-not $TimestampUrl.IsAbsoluteUri -or $TimestampUrl.Scheme -notin @('http', 'https')) { throw 'Use the RFC 3161 timestamp URL supplied by your certificate provider.' }
$store = if ($MachineStore) { 'LocalMachine' } else { 'CurrentUser' }
$certificate = Get-Item "Cert:\$store\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if (-not $certificate) { throw 'Code-signing certificate not found in the selected Personal certificate store.' }
if (-not $certificate.HasPrivateKey) { throw 'The certificate has no accessible private key. Connect/configure its hardware token or signing provider.' }
if ($certificate.Subject -eq $certificate.Issuer -or $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) { throw 'A currently valid CA-issued code-signing certificate is required.' }
if ('1.3.6.1.5.5.7.3.3' -notin @($certificate.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId.Value })) { throw 'This certificate is not a code-signing certificate. A website HTTPS certificate cannot sign releases.' }
$chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
try {
    $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::Online
    if (-not $chain.Build($certificate)) { throw 'Code-signing certificate chain or revocation check failed.' }
}
finally { $chain.Dispose() }
$targets = @($Path | ForEach-Object {
    $file = Get-Item -LiteralPath $_
    if ($file.PSIsContainer -or $file.Extension -notin @('.exe', '.dll', '.msi')) { throw "Unsupported signing target: $_" }
    $file.FullName
})
if (-not $SignToolPath) {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { $SignToolPath = $command.Source }
    else {
        $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $SignToolPath = Get-ChildItem $sdk -Directory | Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
}
if (-not $SignToolPath -or -not (Test-Path $SignToolPath)) { throw 'Install Windows SDK Signing Tools or supply -SignToolPath.' }
foreach ($target in $targets) {
    $signArguments = @('sign', '/sha1', $CertificateThumbprint, '/s', 'My', '/fd', 'SHA256', '/tr', $TimestampUrl.AbsoluteUri, '/td', 'SHA256', '/d', 'FluentControl', '/du', 'https://fctrl.app')
    if ($MachineStore) { $signArguments += '/sm' }
    & $SignToolPath @signArguments $target
    if ($LASTEXITCODE -ne 0) { throw "Signing or timestamping failed: $target" }
    & $SignToolPath verify /pa /all /tw $target
    if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed: $target" }
    $signature = Get-AuthenticodeSignature -LiteralPath $target
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or -not $signature.TimeStamperCertificate) {
        throw "A valid signature from the selected publisher with a timestamp is required: $target"
    }
    Write-Host "Verified signed artifact: $([IO.Path]::GetFileName($target)) / $($signature.SignerCertificate.Subject)"
}
