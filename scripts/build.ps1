$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'initialize-build.ps1')
& $dotnet restore (Join-Path $root 'NetGuardian.sln') --configfile (Join-Path $root 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build (Join-Path $root 'NetGuardian.sln') --no-restore
exit $LASTEXITCODE
