<#
.SYNOPSIS
    Automatically check, update, and clean Python dependencies for RE_Ecosystem.

.DESCRIPTION
    This script will:
    1. Automatically check and display a summary table of all Python packages with status.
    2. Display an action menu:
       [1] Update         : Update packages to the latest version
       [2] Update & Clean : Update + Clean cache/old distributions + Verify
       [3] Exit           : Exit the program

.NOTES
    Only handles Python Runtime in the project, does not affect system Python.
    Python version: 3.11.9 embeddable (kept unchanged, interpreter not updated).
#>

param (
    [string]$Choice = "",
    [string]$PythonRuntimeDir = "Python_Runtime"
)

$ErrorActionPreference = "Stop"

# Determine the root directory of the Repository
$RepoRoot = if (Test-Path (Join-Path $PSScriptRoot "../..")) {
    (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
} else {
    (Get-Location).Path
}

$PythonDir = Join-Path $RepoRoot $PythonRuntimeDir
$PythonExe = Join-Path $PythonDir "python.exe"
$RequirementsFile = Join-Path $RepoRoot "mcp_server\requirements.txt"

if (-not (Test-Path $PythonExe)) {
    throw "Python Runtime not found at $PythonExe. Run scripts/dev/Build-Native-Deps.ps1 first."
}

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "   RE_ECOSYSTEM - PYTHON DEPENDENCY MANAGEMENT TOOL      " -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "Repository : $RepoRoot"
Write-Host "Python Dir : $PythonDir"
Write-Host ""

# -------------------------------------------------------------
# 1. CHECK PYTHON VERSION
# -------------------------------------------------------------
$pythonVersion = & $PythonExe --version 2>&1 | Out-String
$pythonVersion = $pythonVersion.Trim()
Write-Host "[*] Python Version: $pythonVersion" -ForegroundColor Gray
Write-Host ""

# -------------------------------------------------------------
# 2. CHECK UPDATES AND DISPLAY SUMMARY TABLE
# -------------------------------------------------------------
Write-Host "Checking and displaying summary of all packages..." -ForegroundColor Yellow

# Get outdated list to create a quick lookup hashtable
$outdatedOutput = & $PythonExe -m pip list --outdated 2>&1 | Out-String
$outdatedPackages = @()
try {
    $lines = $outdatedOutput -split "`r?`n" | Where-Object {
        $_ -notmatch '^#<' -and $_ -notmatch '^Package' -and $_ -notmatch '^-+' -and -not [string]::IsNullOrWhiteSpace($_.Trim())
    }
    foreach ($line in $lines) {
        if ($line -match '^\s*(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s*$') {
            $outdatedPackages += [PSCustomObject]@{
                Name         = $matches[1]
                InstalledVer = $matches[2]
                LatestVer    = $matches[3]
                Type         = $matches[4]
            }
        }
    }
} catch {
    Write-Warning "      Error parsing Python packages check results: $_"
}

$outdatedHash = @{}
foreach ($pkg in $outdatedPackages) {
    $outdatedHash[$pkg.Name.ToLower()] = $pkg
}

# Get packages pinned with == in requirements.txt to skip during auto-update
$pinnedPackages = @{}
if (Test-Path $RequirementsFile) {
    $reqLines = Get-Content $RequirementsFile
    foreach ($line in $reqLines) {
        if ($line -match '^([a-zA-Z0-9_-]+)==') {
            $pinnedPackages[$matches[1].ToLower()] = $true
        }
    }
}

$allPackagesJson = & $PythonExe -m pip list --format=json 2>$null | Out-String
$allPackages = @()
try {
    $allPackages = $allPackagesJson | ConvertFrom-Json
} catch {
    Write-Warning "      Error parsing package list: $_"
}

if ($allPackages.Count -gt 0) {
    # Create list of objects with status for each package
    $packageTable = @()
    foreach ($pkg in $allPackages) {
        $pkgNameLower = $pkg.name.ToLower()
        $notes = ""

        if ($outdatedHash.ContainsKey($pkgNameLower)) {
            $latest = $outdatedHash[$pkgNameLower]
            $status = "⚠️ $($pkg.version) → $($latest.LatestVer)"
            # Add notes for critical packages
            if ($pkg.name -eq 'cryptography') { $notes = "Security" }
            elseif ($pkg.name -in @('fastmcp', 'fastmcp-slim')) { $notes = "Beta -> Stable" }
            elseif ($pkg.name -in @('mcp', 'pydantic_core', 'starlette')) { $notes = "Major update" }
        } else {
            $status = "✅ Up-to-date"
        }

        $packageTable += [PSCustomObject]@{
            Package = $pkg.name
            Version = $pkg.version
            Status  = $status
            Notes   = $notes
        }
    }

    # Sort: outdated first, then up-to-date
    $sortedTable = $packageTable | Sort-Object { $_.Status -notlike "⚠️*" }

    Write-Host ""
    Write-Host "Package summary ($($sortedTable.Count) packages):" -ForegroundColor Cyan
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    $sortedTable | Format-Table -AutoSize -Property Package, Version, Status, Notes | Out-String | Write-Host
} else {
    Write-Host "      [!] Cannot retrieve package list." -ForegroundColor Yellow
}
Write-Host ""

# -------------------------------------------------------------
# 3. ACTION MENU
# -------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Choice)) {
    Write-Host "=========================================================" -ForegroundColor Cyan
    Write-Host "                       ACTION MENU                     " -ForegroundColor Cyan
    Write-Host "=========================================================" -ForegroundColor Cyan
    Write-Host "  [1] Update         : Update packages to the latest version" -ForegroundColor White
    Write-Host "  [2] Update & Clean : Update + Clean cache/old versions + Verify" -ForegroundColor White
    Write-Host "  [3] Exit           : Exit" -ForegroundColor White
    Write-Host "=========================================================" -ForegroundColor Cyan
    $userChoice = Read-Host "Enter your choice (1, 2, or 3) [Default: 3]"
    $Choice = if ([string]::IsNullOrWhiteSpace($userChoice)) { "3" } else { $userChoice.Trim() }
}

Write-Host ""

# -------------------------------------------------------------
# 4. UPDATE AND CLEAN FUNCTION
# -------------------------------------------------------------
function Invoke-PythonUpdateProcess([bool]$FullClean) {
    # 1. Update pip first
    Write-Host "[1/4] Updating pip..." -ForegroundColor Cyan
    & $PythonExe -m pip install --upgrade pip
    if ($LASTEXITCODE -eq 0) {
        Write-Host "      [OK] Pip updated successfully." -ForegroundColor Green
    } else {
        Write-Warning "      Cannot update pip."
    }
    Write-Host ""

    # 2. Update all packages (skip packages pinned with ==)
    Write-Host "[2/4] Updating Python packages..." -ForegroundColor Cyan
    if ($outdatedPackages.Count -gt 0) {
        foreach ($pkg in $outdatedPackages) {
            if ($pinnedPackages.ContainsKey($pkg.Name.ToLower())) {
                Write-Host "      -> SKIPPING $($pkg.Name) ($($pkg.InstalledVer)) - pinned in requirements.txt" -ForegroundColor Gray
                continue
            }
            Write-Host "      -> Updating $($pkg.Name) ($($pkg.InstalledVer) -> $($pkg.LatestVer))..." -ForegroundColor Cyan
            & $PythonExe -m pip install --upgrade "$($pkg.Name)"
            if ($LASTEXITCODE -eq 0) {
                Write-Host "        [OK] $($pkg.Name) updated." -ForegroundColor Green
            } else {
                Write-Warning "        Cannot update $($pkg.Name)."
            }
        }
    } else {
        Write-Host "      [OK] No packages need updating." -ForegroundColor Green
    }
    Write-Host ""

    # 3. Update from requirements.txt
    Write-Host "[3/4] Updating from requirements.txt..." -ForegroundColor Cyan
    if (Test-Path $RequirementsFile) {
        & $PythonExe -m pip install --upgrade -r $RequirementsFile
        if ($LASTEXITCODE -eq 0) {
            Write-Host "      [OK] requirements.txt updated." -ForegroundColor Green
        } else {
            Write-Warning "      Cannot update requirements.txt."
        }
    } else {
        Write-Host "      [!] requirements.txt not found, skipping." -ForegroundColor Yellow
    }
    Write-Host ""

    # 4. Clean up if requested
    if ($FullClean) {
        Write-Host "[4/4] Cleaning up cache and old versions..." -ForegroundColor Cyan

        # Delete pip cache
        & $PythonExe -m pip cache purge 2>$null | Out-Null
        Write-Host "      [OK] Pip cache cleaned." -ForegroundColor Green

        # Delete __pycache__ in site-packages
        $sitePackagesDir = & $PythonExe -c "import site; print(site.getsitepackages()[0])" 2>$null | Out-String
        $sitePackagesDir = $sitePackagesDir.Trim()
        if (Test-Path $sitePackagesDir) {
            $pycacheFolders = Get-ChildItem -Path $sitePackagesDir -Directory -Recurse -Force -Filter "__pycache__" -ErrorAction SilentlyContinue
            $cacheCount = 0
            foreach ($folder in $pycacheFolders) {
                try {
                    Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction SilentlyContinue
                    $cacheCount++
                } catch { }
            }
            Write-Host "      [OK] Deleted $cacheCount __pycache__ directories in site-packages." -ForegroundColor Green

            # Delete old .dist-info if exists (backup before update)
            $distInfoBackups = Get-ChildItem -Path $sitePackagesDir -Directory -Filter "*.dist-info.backup" -ErrorAction SilentlyContinue
            if ($distInfoBackups.Count -gt 0) {
                foreach ($backup in $distInfoBackups) {
                    try {
                        Remove-Item -Path $backup.FullName -Recurse -Force -ErrorAction SilentlyContinue
                    } catch { }
                }
                Write-Host "      [OK] Deleted old .dist-info backups." -ForegroundColor Green
            }
        }

        # Delete old egg-info
        $eggInfoFolders = Get-ChildItem -Path $sitePackagesDir -Directory -Filter "*.egg-info" -ErrorAction SilentlyContinue
        foreach ($egg in $eggInfoFolders) {
            # Only delete if newer version exists (dist-info)
            $packageName = $egg.Name -replace '\.egg-info$', ''
            $distInfo = Get-ChildItem -Path $sitePackagesDir -Directory -Filter "$packageName*.dist-info" -ErrorAction SilentlyContinue
            if ($distInfo.Count -gt 0) {
                Write-Host "      -> Deleting old egg-info: $($egg.Name) (newer dist-info exists)" -ForegroundColor DarkGray
                Remove-Item -Path $egg.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        # Delete easy-install.pth if exists (legacy)
        $easyInstallPth = Join-Path $sitePackagesDir "easy-install.pth"
        if (Test-Path $easyInstallPth) {
            Write-Host "      -> Deleting easy-install.pth (legacy)" -ForegroundColor DarkGray
            Remove-Item -Path $easyInstallPth -Force -ErrorAction SilentlyContinue
        }

        Write-Host "      [OK] All cache and old versions cleaned." -ForegroundColor Green
    } else {
        Write-Host "[4/4] Skipping cleanup step (standard Update mode)." -ForegroundColor Gray
    }
    Write-Host ""

    # 5. Verify dependencies
    Write-Host "[*] Checking dependencies..." -ForegroundColor Cyan
    $verifyOutput = & $PythonExe -c "import lief; from fastmcp import FastMCP; import yara" 2>&1 | Out-String

    if ($LASTEXITCODE -eq 0) {
        Write-Host "=========================================================" -ForegroundColor Green
        Write-Host "   [SUCCESS] PYTHON DEPENDENCIES READY!                   " -ForegroundColor Green
        Write-Host "   All packages updated and verified successfully." -ForegroundColor Green
        Write-Host "=========================================================" -ForegroundColor Green
    } else {
        Write-Host $verifyOutput
        Write-Host "=========================================================" -ForegroundColor Red
        Write-Host "   [ERROR] VERIFICATION FAILED!                           " -ForegroundColor Red
        Write-Host "   Please check the detailed log above." -ForegroundColor Red
        Write-Host "=========================================================" -ForegroundColor Red
        throw "Python dependencies verification failed."
    }

    # Display post-update summary table
    Write-Host ""
    $updatedCount = 0
    $summaryTable = @()
    foreach ($pkg in $outdatedPackages) {
        if ($pinnedPackages.ContainsKey($pkg.Name.ToLower())) {
            continue
        }
        $updatedCount++
        $summaryTable += [PSCustomObject]@{
            Package      = $pkg.Name
            OldVersion   = $pkg.InstalledVer
            NewVersion   = $pkg.LatestVer
            Status       = "✅ Updated"
        }
    }
    if ($summaryTable.Count -eq 0) {
        $summaryTable += [PSCustomObject]@{
            Package      = "—"
            OldVersion   = "—"
            NewVersion   = "—"
            Status       = "✅ All up-to-date"
        }
    }
    Write-Host "Update summary ($updatedCount packages updated):" -ForegroundColor Cyan
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    $summaryTable | Format-Table -AutoSize -Property Package, OldVersion, NewVersion, Status | Out-String | Write-Host
}

# -------------------------------------------------------------
# 5. ROUTING BASED ON CHOICE
# -------------------------------------------------------------
switch ($Choice) {
    "1" {
        Write-Host ">>> EXECUTING: [1] UPDATE (Update Python Packages) <<<" -ForegroundColor Green
        Invoke-PythonUpdateProcess -FullClean $false
    }
    "2" {
        Write-Host ">>> EXECUTING: [2] UPDATE & CLEAN (Update, Clean & Verify) <<<" -ForegroundColor Green
        Invoke-PythonUpdateProcess -FullClean $true
    }
    default {
        Write-Host "Selected [3] Exit. Goodbye!" -ForegroundColor Yellow
    }
}
