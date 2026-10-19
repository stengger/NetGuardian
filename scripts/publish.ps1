param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'initialize-build.ps1')
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist\NetGuardian' }
& $dotnet restore (Join-Path $root 'NetGuardian.sln') --configfile (Join-Path $root 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet test (Join-Path $root 'NetGuardian.sln') --no-restore --configuration Release --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$projects = @(
    @{ Path = 'src\NetGuardian.App\NetGuardian.App.csproj'; Output = $out },
    @{ Path = 'src\NetGuardian.Guardian\NetGuardian.Guardian.csproj'; Output = (Join-Path $out 'guardian') }
)
foreach ($item in $projects) {
    & $dotnet publish (Join-Path $root $item.Path) --configuration Release --self-contained false --no-restore --output $item.Output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Output "发布完成：$out（界面与无界面守护，需 .NET 8 桌面运行时，无需 SDK）"
