#Requires -Version 7.4
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repo "src/NetGuardian.Core/bin/$Configuration/net8.0/NetGuardian.Core.dll"
$client = [Reflection.Assembly]::LoadFrom($assembly)
$verify = $client.GetType('NetGuardian.Core.Updates.SignedManifest', $true).GetMethod('Verify', [Reflection.BindingFlags]'Static,NonPublic')
$version = ([Diagnostics.FileVersionInfo]::GetVersionInfo($assembly).ProductVersion -split '\+')[0]
$folder = Join-Path $repo ('.dotnet-home/sign-test-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($folder)
$installer = Join-Path $folder "NetGuardian-$version-win-x64-Setup.exe"
$output = Join-Path $folder 'stable.json'
$key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
try {
    Copy-Item -LiteralPath $assembly -Destination $installer
    $parameters = @{Installer=$installer; Sequence=8; Notes='中文说明'; ExpiresUtc=[DateTimeOffset]::UtcNow.AddDays(7); SigningKey=$key; Output=$output}
    & (Join-Path $repo 'scripts/new-signed-update-manifest.ps1') @parameters
    $bytes = [IO.File]::ReadAllBytes($output)
    $manifest = $verify.Invoke($null, [object[]]@($bytes, $key.ExportSubjectPublicKeyInfo()))
    if ($manifest.Sequence -ne 8 -or $manifest.Notes -ne '中文说明' -or $manifest.Hash -ine (Get-FileHash -LiteralPath $installer).Hash) { throw '客户端解析的清单与安装包不符。' }
    $rejected = $false
    try { & (Join-Path $repo 'scripts/new-signed-update-manifest.ps1') @parameters } catch [IO.IOException] { $rejected = $true }
    if (-not $rejected -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($output)) -ne [Convert]::ToBase64String($bytes)) { throw '已有清单被覆盖。' }
    Write-Output '签名清单通过生产客户端验签、摘要核对和防覆盖检查。'
} finally {
    if ($null -ne $key) { $key.Dispose() }
    foreach ($file in @($installer,$output)) { if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file } }
    if ([IO.Directory]::Exists($folder)) { [IO.Directory]::Delete($folder) }
}
