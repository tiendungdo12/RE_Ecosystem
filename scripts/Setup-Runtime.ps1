<#
.SYNOPSIS
Provision the pinned Windows x64 Python runtime and DIE signature database without rebuilding native DLLs.
#>
[CmdletBinding()]
param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath (Join-Path $repo 'RE_Ecosystem.slnx'))) { throw 'Select an RE_Ecosystem repository root.' }
$lockPath = Join-Path $repo 'scripts/runtime-lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$requirements = Join-Path $repo 'mcp_server/requirements-win-x64.lock'
$fingerprint = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash + ':' + (Get-FileHash -LiteralPath $requirements -Algorithm SHA256).Hash
$runtime = Join-Path $repo 'Python_Runtime'
$database = Join-Path $repo 'Rules/die_db'
$marker = Join-Path $runtime '.re-runtime-lock'

function Invoke-Python([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Python provisioning failed (exit $LASTEXITCODE)." }
}
function Assert-WorkspacePath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing filesystem operation outside the selected repository.'
    }
}
function Download-Pinned([string]$Url, [string]$Hash, [string]$Target) {
    if ($Hash -notmatch '^[0-9a-fA-F]{64}$') { throw 'Archive SHA256 missing from runtime lock.' }
    Invoke-WebRequest -Uri $Url -OutFile $Target
    if ((Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash -ine $Hash) { throw 'Downloaded archive hash does not match the lock.' }
}

if ((Test-Path -LiteralPath $marker) -and (Test-Path -LiteralPath (Join-Path $database '.re-database-commit')) -and
    (Get-Content -LiteralPath (Join-Path $database '.re-database-commit') -Raw).Trim() -eq $lock.die_database_commit -and
    (Get-Content -LiteralPath $marker -Raw).Trim() -eq $fingerprint) {
    Invoke-Python (Join-Path $runtime 'python.exe') @('-m', 'pip', 'check')
    Invoke-Python (Join-Path $runtime 'python.exe') @('-c', 'import lief,yara; from fastmcp import FastMCP')
    Write-Host 'Pinned runtime is already provisioned.'
    exit 0
}

$stage = Join-Path $repo ('.runtime-stage-' + [guid]::NewGuid().ToString('N'))
Assert-WorkspacePath $stage
New-Item -ItemType Directory -Path $stage | Out-Null
$pythonStage = Join-Path $stage 'Python_Runtime'
$dbStage = Join-Path $stage 'die_db'
$runtimeBackup = Join-Path $stage 'previous-runtime'
$dbBackup = Join-Path $stage 'previous-db'
$pythonInstalled = $false; $dbInstalled = $false; $success = $false
try {
    $pythonZip = Join-Path $stage 'python.zip'; $dbZip = Join-Path $stage 'database.zip'
    Download-Pinned $lock.python_url $lock.python_sha256 $pythonZip
    Download-Pinned $lock.die_database_url $lock.die_database_sha256 $dbZip
    Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonStage
    $pth = Join-Path $pythonStage 'python311._pth'
    $pthLines = @('python311.zip', '.', 'Lib/site-packages', 'import site')
    Set-Content -LiteralPath $pth -Value $pthLines -Encoding ascii
    # pip is supplied by the exact wheel in the lock. No mutable get-pip.py download.
    $pipLine = Get-Content -LiteralPath $requirements | Where-Object { $_ -match '^pip==' }
    if (-not $pipLine) { throw 'The lock must include pip.' }
    $pipVersion = ($pipLine -split '\s+')[0] -replace '^pip==', ''
    $pipHash = ($pipLine -split '--hash=sha256:')[1].Trim()
    $pipMetadata = Invoke-RestMethod -Uri "https://pypi.org/pypi/pip/$pipVersion/json"
    $pipWheel = $pipMetadata.urls | Where-Object { $_.filename -like '*.whl' -and $_.digests.sha256 -eq $pipHash } | Select-Object -First 1
    if (-not $pipWheel) { throw 'Pinned pip wheel is unavailable.' }
    $wheel = Join-Path $stage 'pip.whl'
    Download-Pinned $pipWheel.url $pipHash $wheel
    $python = Join-Path $pythonStage 'python.exe'
    Invoke-Python $python @('-c', 'import sys,zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])', $wheel, (Join-Path $pythonStage 'Lib/site-packages'))
    Invoke-Python $python @('-m','pip','install','--disable-pip-version-check','--no-warn-script-location','--only-binary=:all:','--require-hashes','-r',$requirements)
    Invoke-Python $python @('-m','pip','check')
    Invoke-Python $python @('-c','import lief,yara; from fastmcp import FastMCP')
    $expanded = Join-Path $stage 'database-source'
    Expand-Archive -LiteralPath $dbZip -DestinationPath $expanded
    $sourceDb = Join-Path $expanded ("Detect-It-Easy-$($lock.die_database_commit)/db")
    if (-not (Test-Path -LiteralPath $sourceDb)) { throw 'Pinned DIE archive has no signature database.' }
    Copy-Item -LiteralPath $sourceDb -Destination $dbStage -Recurse
    Set-Content -LiteralPath (Join-Path $pythonStage '.re-runtime-lock') -Value $fingerprint -Encoding ascii
    Set-Content -LiteralPath (Join-Path $dbStage '.re-database-commit') -Value $lock.die_database_commit -Encoding ascii
    foreach ($path in @($runtime,$database,$runtimeBackup,$dbBackup,$pythonStage,$dbStage)) { Assert-WorkspacePath $path }
    New-Item -ItemType Directory -Path (Split-Path -Parent $database) -Force | Out-Null
    if (Test-Path -LiteralPath $runtime) { Move-Item -LiteralPath $runtime -Destination $runtimeBackup }
    if (Test-Path -LiteralPath $database) { Move-Item -LiteralPath $database -Destination $dbBackup }
    Move-Item -LiteralPath $pythonStage -Destination $runtime; $pythonInstalled = $true
    Move-Item -LiteralPath $dbStage -Destination $database; $dbInstalled = $true
    $success = $true
    Write-Host 'Pinned runtime and DIE database installed successfully.'
} catch {
    foreach ($path in @($runtime,$database,$runtimeBackup,$dbBackup)) { Assert-WorkspacePath $path }
    if ($dbInstalled) { Remove-Item -LiteralPath $database -Recurse -Force }
    if ($pythonInstalled) { Remove-Item -LiteralPath $runtime -Recurse -Force }
    if (Test-Path -LiteralPath $runtimeBackup) { Move-Item -LiteralPath $runtimeBackup -Destination $runtime }
    if (Test-Path -LiteralPath $dbBackup) { Move-Item -LiteralPath $dbBackup -Destination $database }
    throw
} finally {
    Assert-WorkspacePath $stage
    if ($success -or (-not (Test-Path -LiteralPath $runtimeBackup) -and -not (Test-Path -LiteralPath $dbBackup))) {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    } else {
        Write-Warning "Recovery data preserved at $stage; previous runtime could not be restored automatically."
    }
}
