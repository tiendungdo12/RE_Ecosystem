<#
.SYNOPSIS
Build/run a project using WinApp CLI 0.7+. The app restores its pinned WinUI analyzer from NuGet.
#>
$ErrorActionPreference = 'Stop'
$winapp = Get-Command winapp -ErrorAction SilentlyContinue
if (-not $winapp) { throw 'WinApp CLI 0.7+ is required.' }
$version = (& winapp --version).Trim() -replace '^v', ''
if ([version]($version -split '-')[0] -lt [version]'0.7.0') { throw 'WinApp CLI 0.7+ is required.' }
$runArgs = @($args)
if (-not $runArgs.Count) { $runArgs = @('.') }
& winapp run @runArgs
exit $LASTEXITCODE
