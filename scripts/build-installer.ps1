param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
    [string]$CompilerPath = $env:NETGUARDIAN_ISCC,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $CompilerPath) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compilerCommand) { $CompilerPath = $compilerCommand.Source }
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw '未找到 Inno Setup 7 编译器，请设置 NETGUARDIAN_ISCC 或传入 -CompilerPath。'
}
& (Join-Path $root 'tests\Test-InstallerScripts.ps1')
& (Join-Path $root 'tests\Test-GuardianScripts.ps1')
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist\releases' }
New-Item -ItemType Directory -Path $out -Force | Out-Null
# 每次使用新的发布目录，避免将旧日志、配置或过期文件混入安装包。
$package = Join-Path $root ('.dotnet-home\installer-stage-' + [guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'publish.ps1') -OutputDirectory $package -Runtime win-x64 -SelfContained
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($relative in @('NetGuardian.exe', 'coreclr.dll', 'PresentationFramework.dll', 'guardian\NetGuardian.Guardian.exe',
    'guardian\coreclr.dll', 'scripts\installer-maintenance.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $relative) -PathType Leaf)) { throw "自包含发布缺少文件：$relative" }
}
& $CompilerPath ('/DAppVersion=' + $Version) ('/DPackageDirectory=' + $package) ('/DOutputDirectory=' + $out) (Join-Path $root 'installer\NetGuardian.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$installer = Join-Path $out "NetGuardian-$Version-win-x64-Setup.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw '编译器未生成预期安装包。' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($installer + '.sha256', "$hash  $([IO.Path]::GetFileName($installer))`r`n", [Text.UTF8Encoding]::new($false))
Write-Output "安装包：$installer"
Write-Output "SHA-256：$hash"
