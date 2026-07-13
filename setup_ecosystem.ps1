param (
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ToolsDir = $PSScriptRoot
$LibDir = Join-Path $ToolsDir "lib"

if (-not (Test-Path $LibDir)) { New-Item -ItemType Directory -Force -Path $LibDir | Out-Null }

Write-Host "=========================================="
Write-Host "  RE ECOSYSTEM 2.0 - TOOL SETUP SCRIPT"
Write-Host "=========================================="

# ---------------------------------------------------------
# 1. PYTHON PORTABLE 3.11 SETUP (For YARA, LIEF, MCP)
# ---------------------------------------------------------
$PythonDir = "$ToolsDir\Python_Runtime"
if ($Force -or -not (Test-Path "$PythonDir\python.exe")) {
    Write-Host "[1/3] Installing Portable Python 3.11.9 (For YARA, LIEF, MCP)..."
    
    if (Test-Path $PythonDir) { Remove-Item $PythonDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $PythonDir | Out-Null

    $PythonZip = "$ToolsDir\python-3.11.9.zip"
    Write-Host "      Downloading Python..."
    Invoke-WebRequest -Uri "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip" -OutFile $PythonZip
    
    Write-Host "      Extracting Python..."
    tar -xf $PythonZip -C $PythonDir
    Remove-Item $PythonZip -Force

    Write-Host "      Configuring pip..."
    $PthFile = "$PythonDir\python311._pth"
    (Get-Content $PthFile) -replace '#import site', 'import site' | Set-Content $PthFile

    $GetPip = "$ToolsDir\get-pip.py"
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $GetPip
    & "$PythonDir\python.exe" $GetPip
    Remove-Item $GetPip -Force
    
    Write-Host "      Installing Dependencies (mcp, lief, yara-python)..."
    & "$PythonDir\python.exe" -m pip install mcp[cli] lief yara-python
} else {
    Write-Host "[1/3] Portable Python 3.11.9 already installed."
}


# ---------------------------------------------------------
# 2. DIE LIBRARY C++ SETUP
# ---------------------------------------------------------
$DieDestDll = Join-Path $LibDir "die_core.dll"

if ($Force -or -not (Test-Path $DieDestDll)) {
    Write-Host "[2/3] Compiling Native DIE Library (C++ / Qt)..."
    $DieSrcDir = Join-Path $ToolsDir "tmp_die_library"
    $DieBuildDir = Join-Path $DieSrcDir "build"
    
    # Tool paths (Modify these if necessary)
    $QtDir = "E:\Qt\6.11.1\msvc2022_64"
    $CMakeExe = "E:\CMake\bin\cmake.exe"
    $NinjaExe = "E:\Qt\Tools\Ninja\ninja.exe"
    $VcvarsBat = "E:\VSBuildTools\VC\Auxiliary\Build\vcvars64.bat"

    if (Test-Path $DieSrcDir) { Remove-Item -Recurse -Force $DieSrcDir }

    Write-Host "      Cloning die_library Repository (Recursive)..."
    git clone --recursive https://github.com/horsicq/die_library.git $DieSrcDir

    Write-Host "      Patching CMakeLists.txt (Fix NOMINMAX MSVC conflict)..."
    $CMakeFile = Join-Path $DieSrcDir "CMakeLists.txt"
    $CMakeContent = Get-Content $CMakeFile
    $CMakeContent = @("add_compile_definitions(NOMINMAX)") + $CMakeContent
    Set-Content -Path $CMakeFile -Value $CMakeContent

    Write-Host "      Configuring CMake (Ninja + MSVC)..."
    $CmdConfigure = "call `"$VcvarsBat`" && set CMAKE_PREFIX_PATH=$QtDir && `"$CMakeExe`" -S `"$DieSrcDir`" -B `"$DieBuildDir`" -G Ninja -DCMAKE_MAKE_PROGRAM=`"$NinjaExe`""
    cmd /c $CmdConfigure

    Write-Host "      Building Release DLL..."
    $CmdBuild = "call `"$VcvarsBat`" && `"$CMakeExe`" --build `"$DieBuildDir`" --config Release"
    cmd /c $CmdBuild

    Write-Host "      Deploying die_core.dll and Qt Dependencies..."
    $CompiledDll = Join-Path $DieBuildDir "src\dielib\die.dll"
    Copy-Item -Path $CompiledDll -Destination $DieDestDll -Force
    
    $Windeployqt = Join-Path $QtDir "bin\windeployqt.exe"
    & $Windeployqt $DieDestDll

    Write-Host "      Downloading DIE Signature Database..."
    $RulesDir = Join-Path $ToolsDir "Rules"
    if (-not (Test-Path $RulesDir)) { New-Item -ItemType Directory -Force -Path $RulesDir | Out-Null }
    $DbDestDir = Join-Path $RulesDir "die_db"
    $TempGuiDir = Join-Path $ToolsDir "tmp_die_gui"
    if (Test-Path $TempGuiDir) { Remove-Item -Recurse -Force $TempGuiDir }
    git clone https://github.com/horsicq/Detect-It-Easy.git $TempGuiDir
    Copy-Item -Path (Join-Path $TempGuiDir "db") -Destination $DbDestDir -Recurse -Force
    Remove-Item -Recurse -Force $TempGuiDir

    Write-Host "      Cleaning Up DIE Source..."
    Remove-Item -Recurse -Force $DieSrcDir
} else {
    Write-Host "[2/3] DIE Library already compiled and deployed."
}


# ---------------------------------------------------------
# 3. BINWALK v3 RUST C ABI SETUP
# ---------------------------------------------------------
$BinwalkDll = Join-Path $LibDir "binwalk_core.dll"

if ($Force -or -not (Test-Path $BinwalkDll)) {
    Write-Host "[3/3] Compiling Native Binwalk v3 (Rust C-ABI)..."
    $BinwalkSrcDir = Join-Path $ToolsDir "tmp_binwalk"
    
    if (Test-Path $BinwalkSrcDir) { Remove-Item -Recurse -Force $BinwalkSrcDir }

    Write-Host "      Cloning Binwalk v3 Repository..."
    git clone https://github.com/ReFirmLabs/binwalk.git $BinwalkSrcDir

    Write-Host "      Patching Cargo.toml for cdylib..."
    $CargoToml = Join-Path $BinwalkSrcDir "Cargo.toml"
    Add-Content -Path $CargoToml -Value "`n[lib]`ncrate-type = [`"cdylib`", `"rlib`"]"

    Write-Host "      Injecting FFI Code (lib.rs)..."
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

    Write-Host "      Building Binwalk Release DLL..."
    $OldLocation = Get-Location
    Set-Location $BinwalkSrcDir
    cargo build --release
    
    Write-Host "      Copying DLL and Cleaning Up..."
    Copy-Item -Path (Join-Path $BinwalkSrcDir "target\release\binwalk.dll") -Destination $BinwalkDll -Force
    Set-Location $OldLocation
    Remove-Item -Recurse -Force $BinwalkSrcDir
} else {
    Write-Host "[3/3] Binwalk Native DLL already compiled."
}

Write-Host "=========================================="
Write-Host "SETUP COMPLETE! All tools are ready."
