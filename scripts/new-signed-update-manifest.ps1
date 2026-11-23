#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][ValidateRange(1, [long]::MaxValue)][long]$Sequence,
    [Parameter(Mandatory)][DateTimeOffset]$ExpiresUtc,
    [Parameter(Mandatory)][string]$Notes,
    [Parameter(Mandatory)][Security.Cryptography.ECDsa]$SigningKey,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
$package = Get-Item -LiteralPath $Installer
if ($package.Name -cnotmatch '^NetGuardian-(\d+\.\d+\.\d+)-win-x64-Setup\.exe$') { throw '请选择带正式版本号的 win-x64 安装包。' }
$version = $Matches[1]
if (([version]$version).ToString() -cne $version -or ($package.VersionInfo.ProductVersion -split '\+')[0].TrimEnd(' ') -cne $version) { throw '文件名和安装包内的版本号需要一致。' }
if ($ExpiresUtc -le [DateTimeOffset]::UtcNow) { throw '失效时间须晚于当前时间。' }
if ($SigningKey.ExportParameters($false).Curve.Oid.Value -ne '1.2.840.10045.3.1.7') { throw '需要 ECDSA P-256 发布密钥。' }
$inputStream = $package.OpenRead()
try {
    $length = $inputStream.Length
    if ($length -lt 1 -or $length -gt 1GB) { throw '包长超出客户端支持范围。' }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($inputStream)).ToLowerInvariant()
} finally { $inputStream.Dispose() }
$payload = $encoding.GetBytes(([ordered]@{schema=1; channel='stable'; sequence=$Sequence; expiresUtc=$ExpiresUtc.UtcDateTime.ToString('o'); version=$version; arch='win-x64'; file=$package.Name; length=$length; sha256=$hash; notes=$Notes} | ConvertTo-Json -Compress))
$signature = $SigningKey.SignData($payload, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
$envelope = $encoding.GetBytes((@{payload=[Convert]::ToBase64String($payload); signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json -Compress))
if ($envelope.Length -gt 128KB) { throw '签名清单超过客户端大小限制，请精简发布说明。' }
$stream = [IO.File]::Open($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $stream.Write($envelope); $stream.Flush($true) } finally { $stream.Dispose() }
Write-Output "已签名 v$version，发行序号 $Sequence，安装包 SHA-256 为 $hash"
