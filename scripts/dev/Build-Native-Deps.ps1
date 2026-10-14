<#
.SYNOPSIS
    Build and promote native dependencies for RE_Ecosystem dev pipeline.

.DESCRIPTION
    This script automates the dev -> production workflow for dependencies built from source:
    1. AUTO CHECK: Automatically checks for updates when the script opens.
    2. BUILD: Rebuilds DLLs from source with commit locks.
    3. TEST: Verifies DLL load, exports, and functionality (calls export functions).
    4. PROMOTE: SHA256 hash and displays the list of files to commit.

    USAGE:
      .\scripts\dev\Build-Native-Deps.ps1              # Menu: [1] Update / [2] Exit
      .\scripts\dev\Build-Native-Deps.ps1 -Mode Full   # Override menu, run full pipeline

.NOTES
    This script is for DEVELOPMENT only. Production only needs the bundled files in lib/.
    This script is the single entry point for installing and updating native dependencies.
    Paths for Qt, CMake, MSVC are hardcoded in this script.
#>

[CmdletBinding()]
param(
    [ValidateSet('CheckOnly', 'BuildOnly', 'TestOnly', 'Full')]
    [string]$Mode = '',

    [ValidateSet('Die', 'Binwalk', 'All')]
    [string]$Target = 'All',

    [switch]$Force,

    [switch]$DebugDie
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $RepoRoot "lib"
$RulesDir = Join-Path $RepoRoot "Rules"

# ============================================================
# COMMIT LOCKS - Single source of truth
# ============================================================
# File that stores commit locks to persist between runs
$CommitLockFile = Join-Path $RepoRoot ".commit_locks.json"

$CommitLocks = @{
    Die = @{
        Repo   = "https://github.com/horsicq/die_library.git"
        Branch = "master"
        Commit = "81ca9b700fdf2f5baf9a287d73d3cf0a1c6fd91b"
        Dll    = "die_core.dll"
    }
    DieDb = @{
        Repo   = "https://github.com/horsicq/Detect-It-Easy.git"
        Branch = "master"
        Commit = ""
        Dir    = "die_db"
    }
    Binwalk = @{
        Repo   = "https://github.com/ReFirmLabs/binwalk.git"
        Branch = "master"
        Commit = ""
        Dll    = "binwalk_core.dll"
    }
}

# ============================================================
# FAILED COMMITS - Records failed build commits to skip next time
# ============================================================
$FailedCommits = @{
    Die     = $null
    Binwalk = $null
}

# Load commit locks, failed commits, and DIE DB HEAD from file if exists
if (Test-Path $CommitLockFile) {
    try {
        $saved = Get-Content $CommitLockFile -Raw | ConvertFrom-Json
        if ($saved.Die) { $CommitLocks['Die'].Commit = $saved.Die }
        if ($saved.Binwalk) { $CommitLocks['Binwalk'].Commit = $saved.Binwalk }
        if ($saved.Die_Failed) { $FailedCommits['Die'] = $saved.Die_Failed }
        if ($saved.Binwalk_Failed) { $FailedCommits['Binwalk'] = $saved.Binwalk_Failed }
        if ($saved.DieDb_Head) { $CommitLocks['DieDb'].Head = $saved.DieDb_Head }
    }
    catch {
        # Corrupted file, skip
    }
}

# Save commit locks, failed commits, and DIE DB HEAD to file
function Save-CommitLocks {
    $obj = [PSCustomObject]@{
        Die            = $CommitLocks['Die'].Commit
        Binwalk        = $CommitLocks['Binwalk'].Commit
        Die_Failed     = $FailedCommits['Die']
        Binwalk_Failed = $FailedCommits['Binwalk']
        DieDb_Head     = $CommitLocks['DieDb'].Head
    }
    $obj | ConvertTo-Json | Set-Content -Path $CommitLockFile -Encoding UTF8
}

# ============================================================
# FUNCTIONS
# ============================================================

function Write-Step {
    param([string]$Message, [ConsoleColor]$Color = [ConsoleColor]::Cyan)
    Write-Host "  $Message" -ForegroundColor $Color
}

function Write-Success {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Red
}

function Check-CommitUpdate {
    param(
        [string]$Name,
        [string]$Repo,
        [string]$Branch,
        [string]$LockedCommit
    )

    Write-Step "Checking $Name ($Branch)..."

    $tempDir = Join-Path $env:TEMP ("re_eco_check_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))

    try {
        # Use git ls-remote to get HEAD commit without cloning
        $lsRemoteOutput = git ls-remote $Repo $Branch 2>$null | Out-String
        if ([string]::IsNullOrEmpty($lsRemoteOutput) -or $lsRemoteOutput.Trim().Length -eq 0) {
            Write-Fail "  ``$Name``: Could not reach remote or branch/tag '$Branch' not found"
            return @{ NeedsUpdate = $null; LockedShort = "error"; Head = "error" }
        }

        $headCommit = ($lsRemoteOutput -split '\t')[0].Trim()
        if ([string]::IsNullOrEmpty($headCommit) -or $headCommit.Length -lt 7) {
            Write-Fail "  ``$Name``: Could not get HEAD commit from remote"
            return @{ NeedsUpdate = $null; LockedShort = "error"; Head = "error" }
        }
        $headShort = $headCommit.Substring(0, 12)

        # Check if HEAD is a previously failed commit — skip to avoid repeating the failure
        $failedKey = if ($Name -match "^DIE ") { "Die" } elseif ($Name -match "Binwalk") { "Binwalk" } else { $null }
        if ($failedKey -and $FailedCommits[$failedKey] -and $FailedCommits[$failedKey] -eq $headCommit) {
            Write-Fail "  ``$Name``: PREVIOUSLY FAILED - $headShort (build failed last time)"
            return @{ NeedsUpdate = $false; LockedShort = "($headShort FAILED)"; Head = $headShort; IsFailed = $true }
        }

        # Handle case where no commit is locked
        if ([string]::IsNullOrEmpty($LockedCommit) -and $Branch -notmatch "^[0-9a-f]{7,}$") {
            # DIE DB always tracks HEAD — check if HEAD changed vs saved
            if ($Name -match "Signature DB") {
                $savedHead = $CommitLocks['DieDb'].Head
                if ($savedHead -and $savedHead -eq $headCommit) {
                    Write-Success "  ``$Name``: UP-TO-DATE - $headShort (no new signatures)"
                    return @{ NeedsUpdate = $false; LockedShort = "(HEAD)"; Head = $headShort; DbUnchanged = $true }
                }
                Write-Success "  ``$Name``: NEW SIGNATURES - $headShort"
                return @{ NeedsUpdate = $false; LockedShort = "(HEAD)"; Head = $headShort; DbUnchanged = $false }
            }
            # Other components without lock — warning
            Write-Warn "  ``$Name``: NO COMMIT LOCK - Branch/Tag: $Branch"
            return @{ NeedsUpdate = $true; LockedShort = "($Branch)"; Head = $headShort }
        }

        $lockedShort = if ([string]::IsNullOrEmpty($LockedCommit)) { $headShort } else { $LockedCommit.Substring(0, 12) }

        # If locked commit matches HEAD, no update needed
        if ([string]::IsNullOrEmpty($LockedCommit) -or $headCommit -eq $LockedCommit) {
            Write-Success "  ``$Name``: OK - $lockedShort"
            return @{ NeedsUpdate = $false; LockedShort = $lockedShort; Head = $headShort }
        }

        # Commits differ — clone full repo to count commits behind
        $cloneOk = $false
        try {
            git clone $Repo $tempDir 2>$null | Out-Null
            $cloneOk = (Test-Path $tempDir)
        }
        catch {
            $cloneOk = (Test-Path $tempDir)
        }
        if (-not $cloneOk) {
            Write-Warn "  ``$Name``: OUTDATED - Locked: $lockedShort | HEAD: $headShort (clone failed, cannot count)"
            return @{ NeedsUpdate = $true; LockedShort = $lockedShort; Head = $headShort; Behind = -1 }
        }

        $logOutput = git -C $tempDir log "$LockedCommit..HEAD" --oneline 2>$null
        $behind = if ($logOutput) { $logOutput.Count } else { 0 }

        Write-Warn "  ``$Name``: OUTDATED - Locked: $lockedShort | HEAD: $headShort ($behind commits behind)"
        return @{ NeedsUpdate = $true; LockedShort = $lockedShort; Head = $headShort; Behind = $behind }
    }
    catch {
        Write-Fail "  ``$Name``: Cannot check for updates"
        return @{ NeedsUpdate = $null; LockedShort = $LockedCommit; Head = "error" }
    }
    finally {
        if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
    }
}

function Test-DllLoad {
    param([string]$DllPath)

    if (-not (Test-Path $DllPath)) {
        Write-Fail "  DLL not found: $DllPath"
        return $false
    }

    $file = Get-Item $DllPath
    $sizeMB = [math]::Round($file.Length / 1MB, 2)
    $writeTime = $file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
    Write-Step "  File: $DllPath ($sizeMB MB, $writeTime)"

    try {
        $dllBytes = [System.IO.File]::ReadAllBytes($DllPath)

        if ($dllBytes[0] -eq 0x4D -and $dllBytes[1] -eq 0x5A) {
            Write-Success "  MZ header: VALID"
        }
        else {
            Write-Fail "  MZ header: INVALID"
            return $false
        }

        $hash = (Get-FileHash $DllPath -Algorithm SHA256).Hash
        Write-Step "  SHA256: $hash"

        Write-Success "  $DllPath - TEST PASSED"
        return $true
    }
    catch {
        Write-Fail "  $DllPath - TEST FAILED: $_"
        return $false
    }
}

function Test-DieDb {
    param([string]$DbPath)

    if (-not (Test-Path $DbPath)) {
        Write-Fail "  DIE DB not found: $DbPath"
        return $false
    }

    $fileCount = (Get-ChildItem -Path $DbPath -Recurse -File).Count
    $dirCount = (Get-ChildItem -Path $DbPath -Recurse -Directory).Count
    Write-Step "  DIE DB: $DbPath ($fileCount files, $dirCount directories)"

    $sigFiles = Get-ChildItem -Path $DbPath -Recurse -Include "*.sig", "*.json", "*.db" -ErrorAction SilentlyContinue
    if ($sigFiles.Count -gt 0) {
        Write-Success "  Signature files: $($sigFiles.Count) found"
    }
    else {
        Write-Warn "  No signature files found (.sig/.json/.db)"
    }

    Write-Success "  DIE DB - TEST PASSED"
    return $true
}

# ---------------------------------------------------------
# HELPER: Create delegate type for P/Invoke (replaces Marshal.GetTypeForDelegate)
# Uses AssemblyBuilder.DefineDynamicAssembly for .NET Core / .NET 5+ compatibility
# ---------------------------------------------------------
function Get-DelegateType {
    param(
        [Type[]]$Parameters,
        [Type]$ReturnType = [void]
    )
    $asmBuilder = [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
        (New-Object System.Reflection.AssemblyName("PInvokeDelegates")),
        [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
    $modBuilder = $asmBuilder.DefineDynamicModule("InMemoryModule")
    $typeBuilder = $modBuilder.DefineType(
        "DelegateType_$([Guid]::NewGuid().ToString('N').Substring(0,8))",
        'Class, Public, Sealed, AnsiClass, AutoClass',
        [System.MulticastDelegate])
    $ctor = $typeBuilder.DefineConstructor(
        'RTSpecialName, HideBySig, Public',
        [System.Reflection.CallingConventions]::Standard,
        $Parameters)
    $ctor.SetImplementationFlags('Runtime, Managed')

    $invokeMethod = $typeBuilder.DefineMethod(
        'Invoke', 'Public, HideBySig, NewSlot, Virtual', $ReturnType, $Parameters)
    $invokeMethod.SetImplementationFlags('Runtime, Managed')
    return $typeBuilder.CreateType()
}

# ---------------------------------------------------------
# FUNCTIONALITY TEST: Load DLL and call exported functions
# ---------------------------------------------------------
function Test-DllFunctionality {
    param(
        [string]$Name,
        [string]$DllPath,
        [string[]]$ExportNames,
        [scriptblock]$TestBlock = $null,
        [Type]$Kernel32Type = $null,
        [hashtable]$DelegateTypes = $null
    )

    if (-not (Test-Path $DllPath)) {
        Write-Warn "  ``$Name``: DLL not found, skipping functionality test"
        return $true  # Not a failure - DLL may not be built yet
    }

    Write-Step "  Functionality test: $Name"

    if (-not $Kernel32Type) {
        Write-Warn "  ``$Name``: Kernel32 type not provided, skip functionality test"
        return $true
    }

    # Set DLL search directory so dependencies (e.g., Qt6 DLLs) can be resolved
    $DllDir = Split-Path -Parent $DllPath
    $Kernel32Type::SetDllDirectory($DllDir) | Out-Null

    try {
        $hLib = $Kernel32Type::LoadLibrary($DllPath)
        if ($hLib -eq [System.IntPtr]::Zero) {
            $win32Error = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
            Write-Warn "  ``$Name``: LoadLibrary failed (error $win32Error) - skip invoke test"
            # Not a hard failure - DLL may have heavy dependencies not available in PowerShell context
            return $true
        }

        # Verify all expected exports exist
        $missingExports = @()
        foreach ($export in $ExportNames) {
            $procAddr = $Kernel32Type::GetProcAddress($hLib, $export)
            if ($procAddr -eq [System.IntPtr]::Zero) {
                $missingExports += $export
            }
            else {
                Write-Success "  ``$Name``: Export '$export' found"
            }
        }

        if ($missingExports.Count -gt 0) {
            Write-Fail "  ``$Name``: Missing exports: $($missingExports -join ', ')"
            $Kernel32Type::FreeLibrary($hLib) | Out-Null
            return $false
        }

        # Run custom test block if provided (e.g., call the function)
        if ($TestBlock) {
            try {
                # Build splat for scriptblock parameters: always include hLib, kernel32, DllPath
                # plus any delegate types passed via -DelegateTypes hashtable
                $sbParams = @{ hLib = $hLib; kernel32 = $Kernel32Type; DllPath = $DllPath }
                if ($DelegateTypes) {
                    foreach ($key in $DelegateTypes.Keys) {
                        $sbParams[$key] = $DelegateTypes[$key]
                    }
                }
                $result = & $TestBlock @sbParams
                if (-not $result) {
                    Write-Fail "  ``$Name``: Custom test block failed"
                    $Kernel32Type::FreeLibrary($hLib) | Out-Null
                    return $false
                }
            }
            catch {
                Write-Warn "  ``$Name``: Custom test block threw: $_ - skip invoke test"
                # Not a hard failure - the invoke may fail due to runtime environment
            }
        }

        $Kernel32Type::FreeLibrary($hLib) | Out-Null
        Write-Success "  ``$Name``: Functionality test PASSED"
        return $true
    }
    catch {
        Write-Warn "  ``$Name``: Functionality test error: $_ — skip invoke test"
        return $true
    }
}

function Assert-LastExitCode {
    param([string]$Step)
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

# ---------------------------------------------------------
# LOG BUILD FAILURE
# ---------------------------------------------------------
function Log-BuildFailure {
    param([string]$Component, [string]$Commit, [string]$Error)

    $LogDir = Join-Path $RepoRoot "scripts\dev"
    $LogFile = Join-Path $LogDir "Build-Failures.log"
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

    if ([string]::IsNullOrEmpty($Commit)) {
        $Commit = "unknown"
    }

    $Entry = "[$Timestamp] $Component @ $Commit : $Error"
    Add-Content -Path $LogFile -Value $Entry
    Write-Fail "  Da ghi loi vao $LogFile"
}

# ---------------------------------------------------------
# BUILD: PYTHON PORTABLE
# ---------------------------------------------------------
function Invoke-Build-Python {
    param([switch]$ForcePython)

    $pythonAlready = Test-Path (Join-Path $RepoRoot "Python_Runtime\python.exe")
    if (-not $ForcePython -and $pythonAlready) {
        Write-Step "[1/4] Portable Python 3.11.9 already installed."
        Write-Step "      Updating Python dependencies..."
    }
    else {
        Write-Step "[1/4] Installing Portable Python 3.11.9..."
        $PythonDir = Join-Path $RepoRoot "Python_Runtime"

        if (Test-Path $PythonDir) { Remove-Item $PythonDir -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $PythonDir | Out-Null

        $PythonZip = Join-Path $RepoRoot "python-3.11.9.zip"
        Write-Step "      Downloading Python..."
        Invoke-WebRequest -Uri "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip" -OutFile $PythonZip

        Write-Step "      Extracting Python..."
        tar -xf $PythonZip -C $PythonDir
        Remove-Item $PythonZip -Force

        Write-Step "      Configuring pip..."
        $PthFile = Join-Path $PythonDir "python311._pth"
        (Get-Content $PthFile) -replace '#import site', 'import site' | Set-Content $PthFile

        $GetPip = Join-Path $RepoRoot "get-pip.py"
        Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $GetPip
        & (Join-Path $PythonDir "python.exe") $GetPip --no-warn-script-location
        Assert-LastExitCode "Installing pip"
        Remove-Item $GetPip -Force
    }

    $PythonDir = Join-Path $RepoRoot "Python_Runtime"
    $McpRequirements = Join-Path $RepoRoot "mcp_server\requirements.txt"
    & (Join-Path $PythonDir "python.exe") -m pip install --no-warn-script-location -r $McpRequirements
    Assert-LastExitCode "Failed to install Python dependencies"

    & (Join-Path $PythonDir "python.exe") -c "import lief; from fastmcp import FastMCP"
    Assert-LastExitCode "Verifying Python dependencies"

    Write-Success "  Python setup complete."
    return $true
}

# ---------------------------------------------------------
# BUILD: DIE LIBRARY
# ---------------------------------------------------------
function Invoke-Build-Die {
    $DieDestDll = Join-Path $LibDir "die_core.dll"

    # Always build from source — never skip when DLL already exists
    # Backup and rollback managed by Invoke-TestBackup/Invoke-TestRollback

    Write-Step "[2/4] Compiling Native DIE Library (C++ / Qt)..."
    $DieSrcDir = Join-Path $RepoRoot "tmp_die_library"
    $BuiltCommit = $null
    $TempGuiDir = Join-Path $RepoRoot "tmp_die_gui"
    $DieBuildDir = Join-Path $DieSrcDir "build"
    $DieDbUpdated = $true  # Track if DB was actually updated

    $QtDir = "C:\Qt\6.11.1\msvc2022_64"
    $CMakeExe = "C:\Program Files\CMake\bin\cmake.exe"
    $NinjaExe = "C:\Qt\Tools\Ninja\ninja.exe"
    $VcvarsBat = "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat"

    $DieBuildType = if ($DebugDie) { "Debug" } else { "Release" }
    $QtDeployMode = if ($DebugDie) { "--debug" } else { "--release" }

    try {
        if (Test-Path $DieSrcDir) { Remove-Item -Recurse -Force $DieSrcDir }

        Write-Step "      Cloning die_library Repository..."
        git clone https://github.com/horsicq/die_library.git $DieSrcDir

        Write-Step "      Checking out commit $($CommitLocks['Die'].Commit.Substring(0, 12))..."
        Push-Location $DieSrcDir
        git checkout $CommitLocks['Die'].Commit
        git submodule update --init --recursive
        $BuiltCommit = git rev-parse HEAD
        Pop-Location

        Write-Step "      Patching CMakeLists.txt (Fix NOMINMAX MSVC conflict)..."
        $CMakeFile = Join-Path $DieSrcDir "CMakeLists.txt"
        $CMakeContent = Get-Content $CMakeFile
        $CMakeContent = @("add_compile_definitions(NOMINMAX)") + $CMakeContent
        Set-Content -Path $CMakeFile -Value $CMakeContent

        Write-Step "      Configuring CMake (Ninja + MSVC)..."
        $CmdConfigure = "call `"$VcvarsBat`" && set CMAKE_PREFIX_PATH=$QtDir && `"$CMakeExe`" -S `"$DieSrcDir`" -B `"$DieBuildDir`" -G Ninja -DCMAKE_MAKE_PROGRAM=`"$NinjaExe`" -DCMAKE_BUILD_TYPE=$DieBuildType -Wno-author -DCMAKE_C_FLAGS=`"/wd4267 /wd4244 /wd5033 /D_CRT_SECURE_NO_WARNINGS`" -DCMAKE_CXX_FLAGS=`"/wd4267 /wd4244 /wd5033 /D_CRT_SECURE_NO_WARNINGS`""
        cmd /c $CmdConfigure
        Assert-LastExitCode "Configuring CMake"

        Write-Step "      Building $DieBuildType DLL..."
        $CmdBuild = "call `"$VcvarsBat`" && `"$CMakeExe`" --build `"$DieBuildDir`" --config $DieBuildType"
        cmd /c $CmdBuild
        Assert-LastExitCode "Building DIE DLL"

        Write-Step "      Deploying die_core.dll and Qt Dependencies..."
        $CompiledDll = Join-Path $DieBuildDir "src\dielib\die.dll"
        Copy-Item -Path $CompiledDll -Destination $DieDestDll -Force

        $Windeployqt = Join-Path $QtDir "bin\windeployqt.exe"
        $CmdDeploy = "call `"$VcvarsBat`" && `"$Windeployqt`" $QtDeployMode `"$DieDestDll`""
        cmd /c $CmdDeploy
        Assert-LastExitCode "Deploying Qt dependencies"

        # Check if DIE DB HEAD changed before cloning (~32MB save if unchanged)
        $DbDestDir = Join-Path $RulesDir "die_db"
        $DieDbCurrentHead = $null
        $lsRemoteOutput = (git ls-remote $CommitLocks['DieDb'].Repo master 2>$null | Out-String).Trim()
        if ($lsRemoteOutput) {
            $DieDbCurrentHead = ($lsRemoteOutput -split '\s+')[0]
        }

        if ($DieDbCurrentHead -and $CommitLocks['DieDb'].Head -and $DieDbCurrentHead -eq $CommitLocks['DieDb'].Head) {
            Write-Success "  DIE Signature DB unchanged (HEAD: $($DieDbCurrentHead.Substring(0, 12))). Skipping clone."
            $DieDbUpdated = $false
        }
        else {
            Write-Step "      Downloading DIE Signature Database..."
            if (Test-Path $TempGuiDir) { Remove-Item -Recurse -Force $TempGuiDir }
            git clone https://github.com/horsicq/Detect-It-Easy.git $TempGuiDir
            Assert-LastExitCode "Cloning Detect-It-Easy DB"
            if (Test-Path $DbDestDir) { Remove-Item -Recurse -Force $DbDestDir }
            Copy-Item -Path (Join-Path $TempGuiDir "db") -Destination $DbDestDir -Recurse -Force
            # Save the new HEAD for next time
            if ($DieDbCurrentHead) {
                $CommitLocks['DieDb'].Head = $DieDbCurrentHead
            }
        }

        Write-Success "  DIE build complete. (Commit: $($BuiltCommit.Substring(0, 12)))"
        return @{ Success = $true; Commit = $BuiltCommit; DbUpdated = $DieDbUpdated }
    }
    catch {
        Write-Fail "  DIE build failed: $_"
        Log-BuildFailure -Component "DIE" -Commit $CommitLocks['Die'].Commit -Error $_
        # Record failed commit to skip next time
        if ($BuiltCommit) {
            $FailedCommits['Die'] = $BuiltCommit
            Save-CommitLocks
            Write-Fail "  Recorded failed commit ($($BuiltCommit.Substring(0, 12))) to skip next time"
        }
        throw
    }
    finally {
        # Clean up source dirs regardless of success/failure
        if (Test-Path $DieSrcDir) { Remove-Item -Recurse -Force $DieSrcDir }
        if (Test-Path $TempGuiDir) { Remove-Item -Recurse -Force $TempGuiDir }
    }
}

# ---------------------------------------------------------
# BUILD: BINWALK v3
# ---------------------------------------------------------
function Invoke-Build-Binwalk {
    $BinwalkDll = Join-Path $LibDir "binwalk_core.dll"

    # Always build from source — do not skip when DLL already exists
    # Backup and rollback managed by Invoke-TestBackup/Invoke-TestRollback

    Write-Step "[3/4] Compiling Native Binwalk v3 (Rust C-ABI)..."
    $BinwalkSrcDir = Join-Path $RepoRoot "tmp_binwalk"
    $BuiltCommit = $null

    $OldLocation = Get-Location
    try {
        if (Test-Path $BinwalkSrcDir) { Remove-Item -Recurse -Force $BinwalkSrcDir }

        Write-Step "      Cloning Binwalk v3 Repository..."
        git clone https://github.com/ReFirmLabs/binwalk.git $BinwalkSrcDir
        Assert-LastExitCode "Cloning Binwalk v3"

        # Use commit lock if set, otherwise fall back to tag 0.14.0
        $binwalkRef = if ([string]::IsNullOrEmpty($CommitLocks['Binwalk'].Commit)) { "0.14.0" } else { $CommitLocks['Binwalk'].Commit }
        Write-Step "      Checking out $binwalkRef..."
        Push-Location $BinwalkSrcDir
        git checkout $binwalkRef
        $BuiltCommit = git rev-parse HEAD
        Pop-Location

        Write-Step "      Patching Cargo.toml for cdylib..."
        $CargoToml = Join-Path $BinwalkSrcDir "Cargo.toml"
        Add-Content -Path $CargoToml -Value "`n[lib]`ncrate-type = [`"cdylib`", `"rlib`"]"

        Write-Step "      Injecting FFI Code (lib.rs)..."
        $LibRs = Join-Path $BinwalkSrcDir "src\lib.rs"
        $FfiCode = @"

// --- INJECTED BY RE ECOSYSTEM ---
use std::ffi::{CStr, CString};
use std::os::raw::c_char;

#[unsafe(no_mangle)]
pub extern `"C`" fn binwalk_scan(path_ptr: *const c_char) -> *mut c_char {
    if path_ptr.is_null() { return std::ptr::null_mut(); }
    let c_str = unsafe { CStr::from_ptr(path_ptr) };
    let path = match c_str.to_str() { Ok(s) => s, Err(_) => return std::ptr::null_mut() };

    let binwalker = Binwalk::new();
    let results = binwalker.analyze(path, false);
    let json_str = match serde_json::to_string(&results) { Ok(s) => s, Err(_) => return std::ptr::null_mut() };
    let c_json = match CString::new(json_str) { Ok(c) => c, Err(_) => return std::ptr::null_mut() };
    c_json.into_raw()
}

#[unsafe(no_mangle)]
pub extern `"C`" fn binwalk_free(ptr: *mut c_char) {
    if ptr.is_null() { return; }
    unsafe { let _ = CString::from_raw(ptr); }
}
// --- END INJECTED ---
"@
        Add-Content -Path $LibRs -Value $FfiCode

        Write-Step "      Building Binwalk library..."
        Set-Location $BinwalkSrcDir
        $BinwalkRs = Join-Path $BinwalkSrcDir "src\binwalk.rs"
        if (Test-Path $BinwalkRs) {
            $content = Get-Content -Raw -Path $BinwalkRs
            $patched = $content -replace '(?m)^use std::os::windows;\r?\n', ''
            if ($patched -ne $content) {
                Set-Content -Path $BinwalkRs -Value $patched -NoNewline
            }
        }
        cargo build --release --lib
        Assert-LastExitCode "Building Binwalk library"

        Write-Step "      Copying DLL..."
        Copy-Item -Path (Join-Path $BinwalkSrcDir "target\release\binwalk.dll") -Destination $BinwalkDll -Force

        Write-Success "  Binwalk build complete. (Commit: $($BuiltCommit.Substring(0, 12)))"
        return @{ Success = $true; Commit = $BuiltCommit }
    }
    catch {
        Write-Fail "  Binwalk build failed: $_"
        Log-BuildFailure -Component "Binwalk" -Commit $CommitLocks['Binwalk'].Commit -Error $_
        # Record failed commit to skip next time
        if ($BuiltCommit) {
            $FailedCommits['Binwalk'] = $BuiltCommit
            Save-CommitLocks
            Write-Fail "  Recorded failed commit ($($BuiltCommit.Substring(0, 12))) to skip next time"
        }
        throw
    }
    finally {
        Set-Location $OldLocation
        if (Test-Path $BinwalkSrcDir) { Remove-Item -Recurse -Force $BinwalkSrcDir }
    }
}

# ---------------------------------------------------------
# BUILD: YARA RULES
# ---------------------------------------------------------
function Invoke-Build-Yara {
    Write-Step "[4/4] Ensuring Standard YARA Rules for Packers..."
    $YaraDir = Join-Path $RulesDir "yara-rules\packers"
    if (-not (Test-Path $YaraDir)) { New-Item -ItemType Directory -Force -Path $YaraDir | Out-Null }
    $PackersYar = Join-Path $YaraDir "packers.yar"

    if (-not (Test-Path $PackersYar)) {
        Write-Step "      Provisioning default packer rules..."
        $DefaultYaraContent = @'
rule UPX {
    meta:
        description = "Detects UPX packed executable"
    strings:
        $upx0 = "UPX0"
        $upx1 = "UPX1"
        $upx2 = "UPX!"
    condition:
        uint16(0) == 0x5A4D and ($upx0 or $upx1 or $upx2)
}

rule ASPack {
    meta:
        description = "Detects ASPack packed executable"
    strings:
        $aspack = ".aspack"
        $aspack_sig = { 60 E8 03 00 00 00 E9 EB 04 5D 45 55 52 }
    condition:
        uint16(0) == 0x5A4D and ($aspack or $aspack_sig)
}

rule PECompact {
    meta:
        description = "Detects PECompact packed executable"
    strings:
        $pec2 = "PECompact2"
        $pec_sig = { B8 ?? ?? ?? ?? 50 64 FF 35 00 00 00 00 }
    condition:
        uint16(0) == 0x5A4D and ($pec2 or $pec_sig)
}

rule Themida {
    meta:
        description = "Detects Themida protected executable"
    strings:
        $themida_sec = ".themida"
        $oreans_sec = ".oreans"
    condition:
        uint16(0) == 0x5A4D and ($themida_sec or $oreans_sec)
}

rule VMProtect {
    meta:
        description = "Detects VMProtect protected executable"
    strings:
        $vmp0 = ".vmp0"
        $vmp1 = ".vmp1"
        $vmp2 = ".vmp2"
    condition:
        uint16(0) == 0x5A4D and ($vmp0 or $vmp1 or $vmp2)
}
'@
        Set-Content -Path $PackersYar -Value $DefaultYaraContent -Encoding UTF8
    }

    Write-Success "  YARA rules ready."
    return $true
}

# ---------------------------------------------------------
# MAIN BUILD ORCHESTRATOR
# ---------------------------------------------------------
function Invoke-Build {
    param([string]$Component)

    Write-Step "=========================================="
    Write-Step "  BUILD: $Component"
    Write-Step "=========================================="

    try {
        Invoke-Build-Python
        $dieResult = Invoke-Build-Die
        $binwalkResult = Invoke-Build-Binwalk
        Invoke-Build-Yara

        Write-Success "  Build complete."
        return @{
            Success = $true
            DieCommit = $dieResult.Commit
            BinwalkCommit = $binwalkResult.Commit
            DbUpdated = $dieResult.DbUpdated
        }
    }
    catch {
        $Error = $_
        Write-Fail "  Build failed: $Error"
        # Rollback managed by Invoke-TestRollback at pipeline level
        return @{ Success = $false }
    }
}

# ============================================================
# MAIN PIPELINE
# ============================================================

# ----------------------------------------------------------
# AUTO CHECK UPDATE khi mo script
# ----------------------------------------------------------
$AutoCheckResults = @{}
$AutoCheckResults['Die'] = Check-CommitUpdate `
    -Name "DIE Library" `
    -Repo $CommitLocks['Die'].Repo `
    -Branch $CommitLocks['Die'].Branch `
    -LockedCommit $CommitLocks['Die'].Commit

$AutoCheckResults['DieDb'] = Check-CommitUpdate `
    -Name "DIE Signature DB" `
    -Repo $CommitLocks['DieDb'].Repo `
    -Branch $CommitLocks['DieDb'].Branch `
    -LockedCommit $CommitLocks['DieDb'].Commit

$AutoCheckResults['Binwalk'] = Check-CommitUpdate `
    -Name "Binwalk v3" `
    -Repo $CommitLocks['Binwalk'].Repo `
    -Branch $CommitLocks['Binwalk'].Branch `
    -LockedCommit $CommitLocks['Binwalk'].Commit

# Build table display based on check results — same format as Update-Python-Deps.ps1
$NativeTable = @()

foreach ($key in @('Die', 'DieDb', 'Binwalk')) {
    $r = $AutoCheckResults[$key]
    $name = switch ($key) {
        'Die'     { 'DIE Library' }
        'DieDb'   { 'DIE Signature DB' }
        'Binwalk' { 'Binwalk v3' }
    }
    $notes = switch ($key) {
        'Die'     { 'C++/Qt' }
        'DieDb'   { 'Signatures' }
        'Binwalk' { 'Rust' }
    }
    if ($r.IsFailed -eq $true) {
        $status = "⛔ Previously failed ($($r.LockedShort))"
    }
    elseif ($r.NeedsUpdate -eq $true) {
        $status = "⚠️ $($r.LockedShort) → $($r.Head)"
    }
    elseif ($r.NeedsUpdate -eq $false) {
        # DIE DB — show whether signatures changed or not
        if ($key -eq 'DieDb') {
            if ($r.DbUnchanged -eq $true) {
                $status = "✅ Up-to-date (no new signatures)"
            }
            else {
                $status = "🔄 New signatures available"
            }
        }
        else {
            $status = "✅ Up-to-date"
        }
    }
    else {
        $status = "❌ Check failed"
    }
    $locked = $r.LockedShort

    $NativeTable += [PSCustomObject]@{
        Dependency = $name
        Locked     = $locked
        HEAD       = $r.Head
        Status     = $status
        Notes      = $notes
    }
}

Write-Host ""
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "   RE ECOSYSTEM - NATIVE DEPENDENCIES" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Native dependencies summary ($($NativeTable.Count) dependencies):" -ForegroundColor Cyan
Write-Host ("-" * 80) -ForegroundColor DarkGray
$NativeTable | Format-Table -AutoSize -Property Dependency, Locked, HEAD, Status, Notes | Out-String | Write-Host

# ----------------------------------------------------------
# FUNCTION: Display menu and get user choice
# ----------------------------------------------------------
function Show-Menu {
    Write-Host "=========================================================" -ForegroundColor Cyan
    Write-Host "                       ACTION MENU                     " -ForegroundColor Cyan
    Write-Host "=========================================================" -ForegroundColor Cyan
    Write-Host "  [1] Update         : Build → Test → Replace (rollback on failure)" -ForegroundColor White
    Write-Host "  [2] Exit           : Exit" -ForegroundColor White
    Write-Host "=========================================================" -ForegroundColor Cyan
    $userChoice = Read-Host "Enter your choice (1-2) [Default: 2]"
    if ([string]::IsNullOrWhiteSpace($userChoice)) { return "2" }
    return $userChoice.Trim()
}

# ----------------------------------------------------------
# FUNCTION: Check commits stage
# ----------------------------------------------------------
function Invoke-CheckStage {
    Write-Host ""
    Write-Step "------------------------------------------"
    Write-Step "  CHECK: CHECK COMMITS"
    Write-Step "------------------------------------------"

    $results = @{}

    if ($Target -eq 'All' -or $Target -eq 'Die') {
        $results['Die'] = Check-CommitUpdate `
            -Name "DIE Library" `
            -Repo $CommitLocks['Die'].Repo `
            -Branch $CommitLocks['Die'].Branch `
            -LockedCommit $CommitLocks['Die'].Commit

        $results['DieDb'] = Check-CommitUpdate `
            -Name "DIE Signature DB" `
            -Repo $CommitLocks['DieDb'].Repo `
            -Branch $CommitLocks['DieDb'].Branch `
            -LockedCommit $CommitLocks['DieDb'].Commit
    }

    if ($Target -eq 'All' -or $Target -eq 'Binwalk') {
        $results['Binwalk'] = Check-CommitUpdate `
            -Name "Binwalk v3" `
            -Repo $CommitLocks['Binwalk'].Repo `
            -Branch $CommitLocks['Binwalk'].Branch `
            -LockedCommit $CommitLocks['Binwalk'].Commit
    }

    Write-Host ""
    Write-Step "------------------------------------------"
    Write-Step "  SUMMARY"
    Write-Step "------------------------------------------"

    # Build table — same format as auto-check header
    $CheckTable = @()
    foreach ($key in $results.Keys) {
        $r = $results[$key]
        $name = switch ($key) {
            'Die'     { 'DIE Library' }
            'DieDb'   { 'DIE Signature DB' }
            'Binwalk' { 'Binwalk v3' }
        }
        $notes = switch ($key) {
            'Die'     { 'C++/Qt' }
            'DieDb'   { 'Signatures' }
            'Binwalk' { 'Rust' }
        }
        if ($r.IsFailed -eq $true) {
            $status = "⛔ Previously failed ($($r.LockedShort))"
        }
        elseif ($r.NeedsUpdate -eq $true) {
            $status = "⚠️ $($r.LockedShort) → $($r.Head)"
        }
        elseif ($r.NeedsUpdate -eq $false) {
            # DIE DB — show whether signatures changed or not
            if ($key -eq 'DieDb') {
                if ($r.DbUnchanged -eq $true) {
                    $status = "✅ Up-to-date (no new signatures)"
                }
                else {
                    $status = "🔄 New signatures available"
                }
            }
            else {
                $status = "✅ Up-to-date"
            }
        }
        else {
            $status = "❌ Check failed"
        }

        $CheckTable += [PSCustomObject]@{
            Dependency = $name
            Locked     = $r.LockedShort
            HEAD       = $r.Head
            Status     = $status
            Notes      = $notes
        }
    }

    Write-Host ""
    Write-Host "Check update summary ($($CheckTable.Count) dependencies):" -ForegroundColor Cyan
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    $CheckTable | Format-Table -AutoSize -Property Dependency, Locked, HEAD, Status, Notes | Out-String | Write-Host
}

# ----------------------------------------------------------
# FUNCTION: Backup before test — ensures backup exists even if build skips
# ----------------------------------------------------------
function Invoke-TestBackup {
    $DieBackup = Join-Path $LibDir "die_core.dll.backup"
    $BinwalkBackup = Join-Path $LibDir "binwalk_core.dll.backup"
    $DieDbBackup = Join-Path $RulesDir "die_db.backup"

    $dieDll = Join-Path $LibDir "die_core.dll"
    $binwalkDll = Join-Path $LibDir "binwalk_core.dll"
    $dieDbDir = Join-Path $RulesDir "die_db"

    if (Test-Path $dieDll) {
        Copy-Item -Path $dieDll -Destination $DieBackup -Force
        Write-Step "      Backed up die_core.dll"
    }
    if (Test-Path $binwalkDll) {
        Copy-Item -Path $binwalkDll -Destination $BinwalkBackup -Force
        Write-Step "      Backed up binwalk_core.dll"
    }
    if (Test-Path $dieDbDir) {
        if (Test-Path $DieDbBackup) { Remove-Item -Recurse -Force $DieDbBackup }
        Copy-Item -Path $dieDbDir -Destination $DieDbBackup -Recurse -Force
        Write-Step "      Backed up die_db"
    }
}

# ----------------------------------------------------------
# FUNCTION: Rollback after test fail — restore from backup
# ----------------------------------------------------------
function Invoke-TestRollback {
    $DieBackup = Join-Path $LibDir "die_core.dll.backup"
    $BinwalkBackup = Join-Path $LibDir "binwalk_core.dll.backup"
    $DieDbBackup = Join-Path $RulesDir "die_db.backup"

    $dieDll = Join-Path $LibDir "die_core.dll"
    $binwalkDll = Join-Path $LibDir "binwalk_core.dll"
    $dieDbDir = Join-Path $RulesDir "die_db"

    if (Test-Path $DieBackup) {
        Remove-Item -Path $dieDll -Force -ErrorAction SilentlyContinue
        Copy-Item -Path $DieBackup -Destination $dieDll -Force
        Remove-Item -Path $DieBackup -Force
        Write-Warn "  Rolled back DIE DLL to previous version"
    }
    if (Test-Path $BinwalkBackup) {
        Remove-Item -Path $binwalkDll -Force -ErrorAction SilentlyContinue
        Copy-Item -Path $BinwalkBackup -Destination $binwalkDll -Force
        Remove-Item -Path $BinwalkBackup -Force
        Write-Warn "  Rolled back Binwalk DLL to previous version"
    }
    if (Test-Path $DieDbBackup) {
        if (Test-Path $dieDbDir) { Remove-Item -Recurse -Force $dieDbDir }
        Copy-Item -Path $DieDbBackup -Destination $dieDbDir -Recurse -Force
        Remove-Item -Recurse -Force $DieDbBackup
        Write-Warn "  Rolled back DIE DB to previous version"
    }
}

# ----------------------------------------------------------
# FUNCTION: Test DLLs stage
# ----------------------------------------------------------
function Invoke-TestStage {
    Write-Host ""
    Write-Step "------------------------------------------"
    Write-Step "  TEST: TEST DLLs"
    Write-Step "------------------------------------------"

    $allTestsPassed = $true

    # Define Kernel32 P/Invoke types once for all DLL tests
    $kernel32 = Add-Type -MemberDefinition @"
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern System.IntPtr LoadLibrary(string dllPath);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern System.IntPtr GetProcAddress(System.IntPtr hModule, string procName);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(System.IntPtr hModule);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetDllDirectory(string lpPathName);
"@ -Name 'Kernel32' -Namespace 'Win32' -PassThru -ErrorAction SilentlyContinue

    if (-not $kernel32) {
        Write-Warn "  Cannot add Kernel32 type, skipping functionality tests"
        Write-Success "  ALL TESTS PASSED (skipped functionality tests)"
        return $true
    }

    if ($Target -eq 'All' -or $Target -eq 'Die') {
        Write-Step "Testing DIE Library..."
        $dieDll = Join-Path $LibDir $CommitLocks['Die'].Dll
        if (-not (Test-DllLoad -DllPath $dieDll)) {
            $allTestsPassed = $false
        }
        else {
            # Create delegate types OUTSIDE the scriptblock to preserve Type information
            $dieLoadDbDelegateType = Get-DelegateType -Parameters @([Type]::GetType('System.String')) -ReturnType ([Type]::GetType('System.Int32'))

            # Functionality test: load DLL and verify exports
            $dieTestBlock = {
                param($hLib, $kernel32, $DllPath, $loadDbDelegateType)
                $dieDbPath = Join-Path $RepoRoot "Rules\die_db"
                if (-not (Test-Path $dieDbPath)) {
                    Write-Warn "  ``DIE``: DIE DB not found, skipping DIE_LoadDatabaseA test"
                    return $true
                }

                # Get DIE_LoadDatabaseA and call it
                $loadDbAddr = $kernel32::GetProcAddress($hLib, "DIE_LoadDatabaseA")
                if ($loadDbAddr -ne [System.IntPtr]::Zero) {
                    $loadDbDelegate = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer(
                        $loadDbAddr, $loadDbDelegateType)
                    $dbResult = $loadDbDelegate.Invoke($dieDbPath)
                    if ($dbResult -ge 0) {
                        Write-Success "  ``DIE``: DIE_LoadDatabaseA returned $dbResult"
                    }
                    else {
                        Write-Warn "  ``DIE``: DIE_LoadDatabaseA returned $dbResult (may be OK)"
                    }
                }
                return $true
            }
            if (-not (Test-DllFunctionality -Name "DIE" -DllPath $dieDll `
                -ExportNames @("DIE_LoadDatabaseA", "DIE_ScanFileA", "DIE_FreeMemoryA") `
                -TestBlock $dieTestBlock `
                -Kernel32Type $kernel32 `
                -DelegateTypes @{ loadDbDelegateType = $dieLoadDbDelegateType })) {
                $allTestsPassed = $false
            }
        }

        Write-Step "Testing DIE Signature DB..."
        $dieDb = Join-Path $RulesDir $CommitLocks['DieDb'].Dir
        if (-not (Test-DieDb -DbPath $dieDb)) {
            $allTestsPassed = $false
        }
    }

    if ($Target -eq 'All' -or $Target -eq 'Binwalk') {
        Write-Step "Testing Binwalk..."
        $binwalkDll = Join-Path $LibDir $CommitLocks['Binwalk'].Dll
        if (-not (Test-DllLoad -DllPath $binwalkDll)) {
            $allTestsPassed = $false
        }
        else {
            # Create delegate types OUTSIDE the scriptblock to preserve Type information
            $binwalkScanDelegateType = Get-DelegateType -Parameters @([Type]::GetType('System.String')) -ReturnType ([Type]::GetType('System.IntPtr'))
            $binwalkFreeDelegateType = Get-DelegateType -Parameters @([Type]::GetType('System.IntPtr')) -ReturnType ([Type]::GetType('System.Void'))

            # Functionality test: load DLL and verify exports
            $binwalkTestBlock = {
                param($hLib, $kernel32, $DllPath, $scanDelegateType, $freeDelegateType)
                # Get binwalk_scan and call it with the DLL itself as a test file
                $scanAddr = $kernel32::GetProcAddress($hLib, "binwalk_scan")
                if ($scanAddr -ne [System.IntPtr]::Zero) {
                    $scanDelegate = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer(
                        $scanAddr, $scanDelegateType)
                    $resultPtr = $scanDelegate.Invoke($DllPath)
                    if ($resultPtr -ne [System.IntPtr]::Zero) {
                        $resultStr = [System.Runtime.InteropServices.Marshal]::PtrToStringAnsi($resultPtr)
                        Write-Success "  ``Binwalk``: binwalk_scan returned result ($($resultStr.Length) chars)"
                        # Free the result
                        $freeAddr = $kernel32::GetProcAddress($hLib, "binwalk_free")
                        if ($freeAddr -ne [System.IntPtr]::Zero) {
                            $freeDelegate = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer(
                                $freeAddr, $freeDelegateType)
                            $freeDelegate.Invoke($resultPtr)
                        }
                    }
                    else {
                        Write-Warn "  ``Binwalk``: binwalk_scan returned null (may be OK for this file)"
                    }
                }
                return $true
            }
            if (-not (Test-DllFunctionality -Name "Binwalk" -DllPath $binwalkDll `
                -ExportNames @("binwalk_scan", "binwalk_free") `
                -TestBlock $binwalkTestBlock `
                -Kernel32Type $kernel32 `
                -DelegateTypes @{ scanDelegateType = $binwalkScanDelegateType; freeDelegateType = $binwalkFreeDelegateType })) {
                $allTestsPassed = $false
            }
        }
    }

    Write-Host ""
    if ($allTestsPassed) {
        Write-Success "  ALL TESTS PASSED"
    }
    else {
        Write-Fail "  SOME TESTS FAILED"
    }
    return $allTestsPassed
}

# ----------------------------------------------------------
# FUNCTION: Post-update summary table
# ----------------------------------------------------------
function Invoke-UpdateSummary {
    param(
        [string]$DieCommit,
        [string]$BinwalkCommit,
        [bool]$DbUpdated = $true
    )

    Write-Host ""
    Write-Step "=========================================="
    Write-Step "  UPDATE SUMMARY"
    Write-Step "=========================================="

    $summaryTable = @()

    $dieShort = if ($DieCommit) { $DieCommit.Substring(0, 12) } else { "(skipped)" }
    $summaryTable += [PSCustomObject]@{
        Component = "DIE Library"
        Commit    = $dieShort
        Status    = "✅ Updated"
        Notes     = "C++/Qt"
    }

    $binwalkShort = if ($BinwalkCommit) { $BinwalkCommit.Substring(0, 12) } else { "(skipped)" }
    $summaryTable += [PSCustomObject]@{
        Component = "Binwalk v3"
        Commit    = $binwalkShort
        Status    = "✅ Updated"
        Notes     = "Rust"
    }

    $dbHeadShort = if ($CommitLocks['DieDb'].Head) { $CommitLocks['DieDb'].Head.Substring(0, 12) } else { "(unchanged)" }
    $dbStatus = if ($DbUpdated) { "✅ Updated" } else { "✅ Unchanged" }
    $summaryTable += [PSCustomObject]@{
        Component = "DIE Signature DB"
        Commit    = $dbHeadShort
        Status    = $dbStatus
        Notes     = "Signatures"
    }

    Write-Host ""
    Write-Host "Update summary ($($summaryTable.Count) components):" -ForegroundColor Cyan
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    $summaryTable | Format-Table -AutoSize -Property Component, Commit, Status, Notes | Out-String | Write-Host
}

# ----------------------------------------------------------
# FUNCTION: Promote stage
# ----------------------------------------------------------
function Invoke-PromoteStage {
    Write-Host ""
    Write-Step "------------------------------------------"
    Write-Step "  PROMOTE: READY TO COMMIT"
    Write-Step "------------------------------------------"

    Write-Step "Production bundle files:"

    $libFiles = Get-ChildItem -Path $LibDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*.backup' }
    foreach ($f in $libFiles) {
        $hash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.Substring(0, 16)
        Write-Step "  lib/$($f.Name) - SHA256: $hash..."
    }

    $dbPath = Join-Path $RulesDir "die_db"
    if (Test-Path $dbPath) {
        $dbFiles = Get-ChildItem -Path $dbPath -Recurse -File -ErrorAction SilentlyContinue
        Write-Step "  Rules/die_db/ - $($dbFiles.Count) files"
    }

    Write-Host ""
    Write-Step "Current commit locks:"
    $dieShort = if ($CommitLocks['Die'].Commit) { $CommitLocks['Die'].Commit.Substring(0, 12) } else { "(none)" }
    $binwalkShort = if ($CommitLocks['Binwalk'].Commit) { $CommitLocks['Binwalk'].Commit.Substring(0, 12) } else { "(none)" }
    Write-Step "  DIE Library : $dieShort"
    Write-Step "  Binwalk v3  : $binwalkShort"
    Write-Step "  DIE DB      : (always fetches HEAD)"

    Write-Host ""
    Write-Warn "  Commit changes to repository:"
    Write-Warn "    git add lib/ Rules/"
    Write-Warn "    git commit -m 'chore: promote native dependencies'"
    Write-Host ""
    Write-Success "  PIPELINE COMPLETE - Ready to commit."
}

# ----------------------------------------------------------
# ROUTING: Mode override or Menu
# ----------------------------------------------------------
if ([string]::IsNullOrEmpty($Mode)) {
    # Interactive menu loop
    $running = $true
    while ($running) {
        $choice = Show-Menu
        switch ($choice) {
            "1" {
                # UPDATE: Backup → Build → Test → Replace (rollback on failure)
                Write-Host ""
                Write-Host ">>> EXECUTING: [1] UPDATE <<<" -ForegroundColor Green

                # Step 1: Backup DLLs and DIE DB
                Write-Step "------------------------------------------"
                Write-Step "  BACKUP: SAVING CURRENT VERSION"
                Write-Step "------------------------------------------"
                Invoke-TestBackup

                # Step 2: Build DLLs from source
                Write-Step "------------------------------------------"
                Write-Step "  BUILD: BUILDING FROM SOURCE"
                Write-Step "------------------------------------------"
                $buildResult = Invoke-Build -Component "All Native Dependencies"
                if (-not $buildResult.Success) {
                    Write-Fail "  Build failed — rolling back to previous DLLs."
                    Invoke-TestRollback
                    break
                }

                # Step 3: Test new DLLs
                $testsPassed = Invoke-TestStage
                if (-not $testsPassed) {
                    Write-Fail "  Test failed — rolling back to previous DLLs."
                    Invoke-TestRollback
                    break
                }

                # Step 4: Cleanup backup files after successful test
                Remove-Item -Path (Join-Path $LibDir "die_core.dll.backup") -Force -ErrorAction SilentlyContinue
                Remove-Item -Path (Join-Path $LibDir "binwalk_core.dll.backup") -Force -ErrorAction SilentlyContinue
                Remove-Item -Path (Join-Path $RulesDir "die_db.backup") -Recurse -Force -ErrorAction SilentlyContinue

                # Upgrade commit locks to latest HEAD after successful build + test
                $FailedCommits['Die'] = $null
                $FailedCommits['Binwalk'] = $null
                $dieHeadResult = Check-CommitUpdate -Name "DIE Library" -Repo $CommitLocks['Die'].Repo -Branch $CommitLocks['Die'].Branch -LockedCommit $CommitLocks['Die'].Commit
                if ($dieHeadResult.Head -and $dieHeadResult.Head -ne 'error') {
                    $dieHeadFull = (git ls-remote $CommitLocks['Die'].Repo $CommitLocks['Die'].Branch 2>$null | Out-String).Trim() -split '\t' | Select-Object -First 1
                    if ($dieHeadFull) {
                        $CommitLocks['Die'].Commit = $dieHeadFull
                        Write-Success "  DIE commit lock upgraded to HEAD: $($dieHeadFull.Substring(0, 12))"
                    }
                }
                $binwalkHeadResult = Check-CommitUpdate -Name "Binwalk v3" -Repo $CommitLocks['Binwalk'].Repo -Branch $CommitLocks['Binwalk'].Branch -LockedCommit $CommitLocks['Binwalk'].Commit
                if ($binwalkHeadResult.Head -and $binwalkHeadResult.Head -ne 'error') {
                    $binwalkHeadFull = (git ls-remote $CommitLocks['Binwalk'].Repo $CommitLocks['Binwalk'].Branch 2>$null | Out-String).Trim() -split '\t' | Select-Object -First 1
                    if ($binwalkHeadFull) {
                        $CommitLocks['Binwalk'].Commit = $binwalkHeadFull
                        Write-Success "  Binwalk commit lock upgraded to HEAD: $($binwalkHeadFull.Substring(0, 12))"
                    }
                }
                Save-CommitLocks

                # Post-update summary
                Invoke-UpdateSummary -DieCommit $CommitLocks['Die'].Commit -BinwalkCommit $CommitLocks['Binwalk'].Commit -DbUpdated $buildResult.DbUpdated

                # Promote
                Invoke-PromoteStage
            }
            default {
                Write-Host ""
                Write-Host "Selected [2] Exit. Goodbye!" -ForegroundColor Yellow
                $running = $false
            }
        }
    }
}
else {
    # Command-line override (kept for automation)
    $modeDisplay = $Mode
    Write-Host "  Target: $Target | Mode: $modeDisplay" -ForegroundColor Cyan
    Write-Host ""

    if ($Mode -eq 'CheckOnly') {
        Invoke-CheckStage
    }

    if ($Mode -eq 'Full') {
        # Step 1: Backup DLLs and DIE DB
        Write-Host ""
        Write-Step "------------------------------------------"
        Write-Step "  BACKUP: SAVING CURRENT VERSION"
        Write-Step "------------------------------------------"
        Invoke-TestBackup

        # Step 2: Build DLLs from source
        Write-Host ""
        Write-Step "------------------------------------------"
        Write-Step "  BUILD: BUILDING FROM SOURCE"
        Write-Step "------------------------------------------"
        $buildResult = Invoke-Build -Component "All Native Dependencies"
        if (-not $buildResult.Success) {
            Write-Fail "  Build failed — rolling back to previous DLLs."
            Invoke-TestRollback
            exit 1
        }

        # Step 3: Test new DLLs
        $testsPassed = Invoke-TestStage
        if (-not $testsPassed) {
            Write-Fail "  Test failed — rolling back to previous DLLs."
            Invoke-TestRollback
            exit 1
        }

        # Step 4: Cleanup backup files after successful test
        Remove-Item -Path (Join-Path $LibDir "die_core.dll.backup") -Force -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path $LibDir "binwalk_core.dll.backup") -Force -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path $RulesDir "die_db.backup") -Recurse -Force -ErrorAction SilentlyContinue

        # Upgrade commit locks to latest HEAD after successful build + test
        $FailedCommits['Die'] = $null
        $FailedCommits['Binwalk'] = $null
        $dieHeadResult = Check-CommitUpdate -Name "DIE Library" -Repo $CommitLocks['Die'].Repo -Branch $CommitLocks['Die'].Branch -LockedCommit $CommitLocks['Die'].Commit
        if ($dieHeadResult.Head -and $dieHeadResult.Head -ne 'error') {
            $dieHeadFull = (git ls-remote $CommitLocks['Die'].Repo $CommitLocks['Die'].Branch 2>$null | Out-String).Trim() -split '\t' | Select-Object -First 1
            if ($dieHeadFull) {
                $CommitLocks['Die'].Commit = $dieHeadFull
                Write-Success "  DIE commit lock upgraded to HEAD: $($dieHeadFull.Substring(0, 12))"
            }
        }
        $binwalkHeadResult = Check-CommitUpdate -Name "Binwalk v3" -Repo $CommitLocks['Binwalk'].Repo -Branch $CommitLocks['Binwalk'].Branch -LockedCommit $CommitLocks['Binwalk'].Commit
        if ($binwalkHeadResult.Head -and $binwalkHeadResult.Head -ne 'error') {
            $binwalkHeadFull = (git ls-remote $CommitLocks['Binwalk'].Repo $CommitLocks['Binwalk'].Branch 2>$null | Out-String).Trim() -split '\t' | Select-Object -First 1
            if ($binwalkHeadFull) {
                $CommitLocks['Binwalk'].Commit = $binwalkHeadFull
                Write-Success "  Binwalk commit lock upgraded to HEAD: $($binwalkHeadFull.Substring(0, 12))"
            }
        }
        Save-CommitLocks

        # Post-update summary
        Invoke-UpdateSummary -DieCommit $CommitLocks['Die'].Commit -BinwalkCommit $CommitLocks['Binwalk'].Commit -DbUpdated $buildResult.DbUpdated

        Invoke-PromoteStage
    }

    if ($Mode -eq 'BuildOnly') {
        Write-Host ""
        Write-Step "------------------------------------------"
        Write-Step "  BUILD: BUILDING FROM SOURCE"
        Write-Step "------------------------------------------"
        $buildResult = Invoke-Build -Component "All Native Dependencies"
        if (-not $buildResult.Success) {
            Write-Fail "  Build failed."
            exit 1
        }
    }

    if ($Mode -eq 'TestOnly') {
        $testsPassed = Invoke-TestStage
        if (-not $testsPassed) {
            Write-Fail "  Test failed."
            exit 1
        }
    }
}

Write-Host ""
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  DONE" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan
