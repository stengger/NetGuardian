param([string]$OutputDirectory, [ValidateSet('win-x64', 'win-arm64')][string]$Runtime,
    [switch]$SelfContained)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'initialize-build.ps1')
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist\NetGuardian' }
$runtimeArguments = if ($Runtime) { @('--runtime', $Runtime) } else { @() }
if ($SelfContained -and -not $Runtime) { throw '自包含发布必须指定 -Runtime。' }
& $dotnet restore (Join-Path $root 'NetGuardian.sln') --configfile (Join-Path $root 'NuGet.Config') @runtimeArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet test (Join-Path $root 'NetGuardian.sln') --no-restore --configuration Release --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$projects = @(
    @{ Path = 'src\NetGuardian.App\NetGuardian.App.csproj'; Output = $out },
    @{ Path = 'src\NetGuardian.Guardian\NetGuardian.Guardian.csproj'; Output = (Join-Path $out 'guardian') }
)
foreach ($item in $projects) {
    $contained = if ($SelfContained) { 'true' } else { 'false' }
    & $dotnet publish (Join-Path $root $item.Path) --configuration Release --self-contained $contained --no-restore --output $item.Output @runtimeArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
. (Join-Path $PSScriptRoot 'package-support.ps1')
Copy-NetGuardianPackageSupport -SourceRoot $root -OutputDirectory $out
$runtimeHint = if ($SelfContained) { '已包含 .NET 运行时' } else { '需 .NET 8 桌面运行时' }
Write-Output "发布完成：$out（界面与无界面守护，$runtimeHint，无需 SDK）"
