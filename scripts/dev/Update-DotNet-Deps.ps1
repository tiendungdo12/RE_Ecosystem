<#
.SYNOPSIS
    Automatically check, update, and clean Windows App SDK, .NET Templates and NuGet packages for RE_Ecosystem.

.DESCRIPTION
    Just run the script (no parameters needed), it will:
    1. Automatically check for all updates (.NET Templates & NuGet packages).
    2. Display a detailed report of outdated packages/templates.
    3. Display an action menu:
       [1] Update         : Update packages & templates to the latest version
       [2] Update & Clean : Update + Clean bin/obj/cache/old versions + Build verify
       [3] Exit           : Exit the program
#>

param (
    [string]$Choice = "",
    [string]$SolutionPath = "RE_Ecosystem.slnx"
)

$ErrorActionPreference = "Stop"

# Determine the root directory of the Repository
$RepoRoot = if (Test-Path (Join-Path $PSScriptRoot "../../$SolutionPath")) {
    (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
} else {
    (Get-Location).Path
}

$FullSolutionPath = Join-Path $RepoRoot $SolutionPath
if (-not (Test-Path $FullSolutionPath)) {
    $foundSln = Get-ChildItem -Path $RepoRoot -Filter "*.slnx" -File | Select-Object -First 1
    if (-not $foundSln) {
        $foundSln = Get-ChildItem -Path $RepoRoot -Filter "*.sln" -File | Select-Object -First 1
    }
    if ($foundSln) {
        $FullSolutionPath = $foundSln.FullName
    } else {
        throw "Solution file ($SolutionPath) not found at $RepoRoot"
    }
}

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "   RE_ECOSYSTEM - DEPENDENCY & SDK MANAGEMENT TOOL       " -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "Repository : $RepoRoot"
Write-Host "Solution   : $FullSolutionPath"
Write-Host ""

# -------------------------------------------------------------
# 1. AUTO CHECK UPDATES (.NET TEMPLATES & NUGET PACKAGES)
# -------------------------------------------------------------
Write-Host "Checking and displaying summary of all build items..." -ForegroundColor Yellow

# Check .NET Templates
$hasTemplateUpdate = $false
try {
    $templateCheck = & dotnet new update --check-only 2>&1 | Out-String
    if ($templateCheck -match "An update for template packages is available" -or $templateCheck -match "Microsoft\.WindowsAppSDK") {
        $hasTemplateUpdate = $true
    }
} catch {
    Write-Warning "      Cannot check templates: $_"
}

# Check NuGet packages outdated
$jsonRaw = & dotnet list "$FullSolutionPath" package --outdated --format json 2>&1 | Out-String
$outdatedPackages = @()
try {
    $jsonObj = $jsonRaw | ConvertFrom-Json
    if ($jsonObj.projects) {
        foreach ($proj in $jsonObj.projects) {
            $projPath = $proj.path
            $projName = [System.IO.Path]::GetFileNameWithoutExtension($projPath)
            if ($proj.frameworks) {
                foreach ($fw in $proj.frameworks) {
                    if ($fw.topLevelPackages) {
                        foreach ($pkg in $fw.topLevelPackages) {
                            $outdatedPackages += [PSCustomObject]@{
                                ProjectPath      = $projPath
                                ProjectName      = $projName
                                PackageId        = $pkg.id
                                RequestedVersion = $pkg.requestedVersion
                                ResolvedVersion  = $pkg.resolvedVersion
                                LatestVersion    = $pkg.latestVersion
                            }
                        }
                    }
                }
            }
        }
    }
} catch {
    Write-Warning "      Error parsing NuGet check results: $_"
}

# Create hashtable from outdated packages for quick lookup
$outdatedHash = @{}
foreach ($pkg in $outdatedPackages) {
    $key = "$($pkg.ProjectName.ToLower())|$($pkg.PackageId.ToLower())"
    $outdatedHash[$key] = $pkg
}

# Get list of ALL installed NuGet packages
$allPackagesJson = & dotnet list "$FullSolutionPath" package --format json 2>$null | Out-String
$allPackages = @()
try {
    $allJsonObj = $allPackagesJson | ConvertFrom-Json
    if ($allJsonObj.projects) {
        foreach ($proj in $allJsonObj.projects) {
            $projPath = $proj.path
            $projName = [System.IO.Path]::GetFileNameWithoutExtension($projPath)
            if ($proj.frameworks) {
                foreach ($fw in $proj.frameworks) {
                    if ($fw.topLevelPackages) {
                        foreach ($pkg in $fw.topLevelPackages) {
                            $allPackages += [PSCustomObject]@{
                                ProjectPath   = $projPath
                                ProjectName   = $projName
                                PackageId     = $pkg.id
                                ResolvedVersion = $pkg.resolvedVersion
                            }
                        }
                    }
                }
            }
        }
    }
} catch {
    Write-Warning "      Error parsing NuGet package list: $_"
}

# Display summary table
Write-Host ""
if ($hasTemplateUpdate) {
    Write-Host "      âš ï¸  .NET Templates: Update available" -ForegroundColor Yellow
    Write-Host ($templateCheck.Trim()) -ForegroundColor Gray
} else {
    Write-Host "      âœ… .NET Templates: Already up-to-date" -ForegroundColor Green
}

if ($allPackages.Count -gt 0) {
    # Create list of objects with status for each package
    $packageTable = @()
    foreach ($pkg in $allPackages) {
        $key = "$($pkg.ProjectName.ToLower())|$($pkg.PackageId.ToLower())"
        $notes = ""

        if ($outdatedHash.ContainsKey($key)) {
            $latest = $outdatedHash[$key]
            $status = "âš ï¸ $($pkg.ResolvedVersion) â†’ $($latest.LatestVersion)"
            # Add notes for critical packages
            if ($pkg.PackageId -match 'WindowsAppSDK') { $notes = "Framework" }
        } else {
            $status = "âœ… Up-to-date"
        }

        $packageTable += [PSCustomObject]@{
            Project = $pkg.ProjectName
            Package = $pkg.PackageId
            Version = $pkg.ResolvedVersion
            Status  = $status
            Notes   = $notes
        }
    }

    # Sort: outdated first, then up-to-date
    $sortedTable = $packageTable | Sort-Object { $_.Status -notlike "âš ï¸*" }

    Write-Host ""
    Write-Host "NuGet packages summary ($($sortedTable.Count) packages):" -ForegroundColor Cyan
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    $sortedTable | Format-Table -AutoSize -Property Project, Package, Version, Status, Notes | Out-String | Write-Host
} else {
    Write-Host "      âœ… NuGet Packages: All already at the latest version" -ForegroundColor Green
}
Write-Host ""

# -------------------------------------------------------------
# 2. ACTION MENU
# -------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Choice)) {
    Write-Host "=========================================================" -ForegroundColor Cyan
    Write-Host "                       ACTION MENU                     " -ForegroundColor Cyan
    Write-Host "=========================================================" -ForegroundColor Cyan
    Write-Host "  [1] Update         : Update packages & templates to the latest version" -ForegroundColor White
    Write-Host "  [2] Update & Clean : Update + Clean bin/obj/cache/old versions + Build verify" -ForegroundColor White
    Write-Host "  [3] Exit           : Exit" -ForegroundColor White
    Write-Host "=========================================================" -ForegroundColor Cyan
    $userChoice = Read-Host "Enter your choice (1, 2, or 3) [Default: 3]"
    $Choice = if ([string]::IsNullOrWhiteSpace($userChoice)) { "3" } else { $userChoice.Trim() }
}

Write-Host ""

# -------------------------------------------------------------
# 3. UPDATE AND CLEAN FUNCTION
# -------------------------------------------------------------
function Invoke-UpdateProcess([bool]$FullClean) {
    # 1. Update templates
    if ($hasTemplateUpdate) {
        Write-Host "[1/4] Updating Template Pack (dotnet new update)..." -ForegroundColor Cyan
        & dotnet new update
        if ($LASTEXITCODE -eq 0) {
            Write-Host "      [OK] Template Pack updated successfully." -ForegroundColor Green
        }
    } else {
        Write-Host "[1/4] Template Pack already up-to-date." -ForegroundColor Green
    }
    Write-Host ""

    # 2. Update NuGet Packages
    Write-Host "[2/4] Updating NuGet packages..." -ForegroundColor Cyan
    $updatedSummary = @{}
    if ($outdatedPackages.Count -gt 0) {
        # Update non-WindowsAppSDK packages first
        foreach ($item in $outdatedPackages) {
            Write-Host "      -> [$($item.ProjectName)] Updating $($item.PackageId) ($($item.ResolvedVersion) -> $($item.LatestVersion))..." -ForegroundColor Cyan
            & dotnet add "$($item.ProjectPath)" package "$($item.PackageId)"
            if ($LASTEXITCODE -eq 0) {
                $updatedSummary[$item.PackageId.ToLowerInvariant()] = @{
                    OldVersion = $item.ResolvedVersion
                    NewVersion = $item.LatestVersion
                }
            }
        }
        Write-Host "      [OK] All .csproj files updated." -ForegroundColor Green
    } else {
        Write-Host "      [OK] No packages need updating." -ForegroundColor Green
    }
    Write-Host ""

    # 3. Clean up if requested
    if ($FullClean) {
        Write-Host "[3/4] Cleaning up bin, obj, cache and old versions..." -ForegroundColor Cyan
        $binObjFolders = Get-ChildItem -Path $RepoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -in @('bin', 'obj') -and $_.FullName -notmatch '\\(Python_Runtime|\.git|tmp_)\\' }

        foreach ($folder in $binObjFolders) {
            try {
                Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction SilentlyContinue
            } catch { }
        }
        Write-Host "      [OK] Deleted all bin and obj directories." -ForegroundColor Green

        # Delete WinApp staging layout if exists
        $winappLayout = Join-Path $env:LOCALAPPDATA "winapp-layout"
        if (Test-Path $winappLayout) {
            try {
                Remove-Item -Path $winappLayout -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "      [OK] Cleaned WinApp layout cache." -ForegroundColor Green
            } catch { }
        }

        # Clean NuGet http-cache and temp cache
        & dotnet nuget locals http-cache --clear 2>$null | Out-Null
        & dotnet nuget locals temp --clear 2>$null | Out-Null
        Write-Host "      [OK] Cleaned NuGet http/temp cache." -ForegroundColor Green

        # Delete old versions in global cache
        $globalCacheDir = Join-Path $env:USERPROFILE ".nuget\packages"
        if (Test-Path $globalCacheDir) {
            $packagesToPurge = if ($updatedSummary.Keys.Count -gt 0) {
                $updatedSummary.Keys
            } else {
                @("microsoft.windowsappsdk", "microsoft.windows.sdk.buildtools", "microsoft.windows.sdk.buildtools.winapp")
            }

            foreach ($pkgName in $packagesToPurge) {
                $pkgDir = Join-Path $globalCacheDir $pkgName
                if (Test-Path $pkgDir) {
                    $versionDirs = Get-ChildItem -Path $pkgDir -Directory
                    if ($versionDirs.Count -gt 1) {
                        $keepVersion = if ($updatedSummary.ContainsKey($pkgName)) {
                            $updatedSummary[$pkgName].NewVersion
                        } else {
                            $null
                        }

                        $keepDir = if ($keepVersion -and (Test-Path (Join-Path $pkgDir $keepVersion))) {
                            Join-Path $pkgDir $keepVersion
                        } else {
                            ($versionDirs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
                        }

                        foreach ($dir in $versionDirs) {
                            if ($dir.FullName -ne $keepDir) {
                                Write-Host "      -> Deleting old version: $($dir.FullName)" -ForegroundColor DarkGray
                                Remove-Item -Path $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
                            }
                        }
                    }
                }
            }
            Write-Host "      [OK] Deleted old versions in ~/.nuget/packages." -ForegroundColor Green
        }
    } else {
        Write-Host "[3/4] Skipping cleanup step (standard Update mode)." -ForegroundColor Gray
    }
    Write-Host ""

    # 4. Restore & Build Verify
    Write-Host "[4/4] Checking compilation (Verification Build)..." -ForegroundColor Cyan
    Write-Host "      Restoring packages..." -ForegroundColor Gray
    & dotnet restore "$FullSolutionPath"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }

    Write-Host "      Building solution ($SolutionPath)..." -ForegroundColor Gray
    $buildOutput = & dotnet build "$FullSolutionPath" -c Debug --no-restore 2>&1 | Out-String

    if ($LASTEXITCODE -eq 0) {
        Write-Host "=========================================================" -ForegroundColor Green
        Write-Host "   [SUCCESS] BUILD SUCCESSFUL WITH 0 ERRORS!             " -ForegroundColor Green
        Write-Host "   Project is ready for development and testing.          " -ForegroundColor Green
        Write-Host "=========================================================" -ForegroundColor Green

        # Display post-update summary table
        Write-Host ""
        $summaryTable = @()
        if ($hasTemplateUpdate) {
            $summaryTable += [PSCustomObject]@{
                Component = ".NET Templates"
                Details   = "—"
                Status    = "✅ Updated"
                Notes     = "dotnet new"
            }
        }
        foreach ($pkg in $outdatedPackages) {
            $newVer = if ($updatedSummary.ContainsKey($pkg.PackageId.ToLowerInvariant())) {
                $updatedSummary[$pkg.PackageId.ToLowerInvariant()].NewVersion
            } else { $pkg.ResolvedVersion }
            $summaryTable += [PSCustomObject]@{
                Component = "$($pkg.PackageId) ($($pkg.ProjectName))"
                Details   = "$(.ResolvedVersion) → $newVer"
                Status    = "✅ Updated"
                Notes     = "NuGet"
            }
        }
        if ($summaryTable.Count -eq 0) {
            $summaryTable += [PSCustomObject]@{
                Component = "—"
                Details   = "—"
                Status    = "✅ All up-to-date"
                Notes     = "—"
            }
        }
        Write-Host "Update summary ($($summaryTable.Count) items):" -ForegroundColor Cyan
        Write-Host ("-" * 80) -ForegroundColor DarkGray
        $summaryTable | Format-Table -AutoSize -Property Component, Details, Status, Notes | Out-String | Write-Host
    } else {
        Write-Host $buildOutput
        throw "Build failed after update. Please check the detailed log."
    }
}

# -------------------------------------------------------------
# 4. ROUTING BASED ON CHOICE
# -------------------------------------------------------------
switch ($Choice) {
    "1" {
        Write-Host ">>> EXECUTING: [1] UPDATE (Update Package & Template) <<<" -ForegroundColor Green
        Invoke-UpdateProcess -FullClean $false
    }
    "2" {
        Write-Host ">>> EXECUTING: [2] UPDATE & CLEAN (Update, Clean & Build verify) <<<" -ForegroundColor Green
        Invoke-UpdateProcess -FullClean $true
    }
    default {
        Write-Host "Selected [3] Exit. Goodbye!" -ForegroundColor Yellow
    }
}
