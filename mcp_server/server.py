import hashlib
import math
import os
import struct
import zipfile
import subprocess
from collections import Counter
from typing import Any
import lief

from mcp.server.fastmcp import FastMCP

mcp = FastMCP("RE_Unified_Server")

# Hop den triage:
# Input la file path local. Output la dict JSON-serializable lam "ban do chi duong"
# cho Branch Table va cac tool RE nang hon o cac block sau.
def _triage_file(path: str, progress_cb=None) -> dict[str, Any]:
    absolute_path = os.path.abspath(path)

    if not os.path.exists(absolute_path):
        return {
            "path": absolute_path,
            "exists": False,
            "error": "File does not exist.",
            "recommended_pipeline": "manual.review",
        }

    if not os.path.isfile(absolute_path):
        return {
            "path": absolute_path,
            "exists": True,
            "is_file": False,
            "error": "Path is not a file.",
            "recommended_pipeline": "manual.review",
        }

    if progress_cb: progress_cb("Đọc dữ liệu file...")
    data = _read_file(absolute_path)
    
    if progress_cb: progress_cb("Đang tính toán MD5 / SHA256...")
    hashes = _hash_file(data)
    
    if progress_cb: progress_cb("Phân tích cấu trúc file bằng LIEF...")
    file_type = "unknown"
    platform = "unknown"
    architecture = "unknown"
    entrypoint = None
    sections = []
    signals = []
    imports_summary = {"count": 0, "suspicious": []}
    
    # LIEF Integration
    binary = lief.parse(list(data))
    if binary is None:
        if progress_cb: progress_cb("Không thể parse bằng LIEF, dùng cách thủ công...")
        file_type, platform, signals = _classify_file_fallback(absolute_path, data)
    else:
        if isinstance(binary, lief.PE.Binary):
            file_type = "pe"
            platform = "windows"
            architecture = str(binary.header.machine).split(".")[-1]
            if binary.optional_header:
                entrypoint = hex(binary.optional_header.addressof_entrypoint)
            
            for sec in binary.sections:
                sections.append({
                    "name": sec.name,
                    "entropy": round(sec.entropy, 4),
                    "raw_size": sec.size,
                })
                
            suspicious = []
            count = 0
            if binary.has_imports:
                for imp in binary.imports:
                    for entry in imp.entries:
                        count += 1
                        name = entry.name
                        if name in (
                            "LoadLibraryA", "LoadLibraryW", "GetProcAddress", 
                            "VirtualAlloc", "VirtualAllocEx", "VirtualProtect", "VirtualProtectEx",
                            "CreateProcessA", "CreateProcessW", "CreateRemoteThread",
                            "WriteProcessMemory", "ReadProcessMemory", 
                            "LdrLoadDll", "LdrGetProcedureAddress", "MapViewOfFile"
                        ):
                            suspicious.append(name)
            imports_summary = {"count": count, "suspicious": suspicious}
            
        elif isinstance(binary, lief.ELF.Binary):
            file_type = "elf"
            platform = "linux_or_android"
            architecture = str(binary.header.machine_type).split(".")[-1]
            entrypoint = hex(binary.header.entrypoint)
            
            for sec in binary.sections:
                sections.append({
                    "name": sec.name,
                    "entropy": round(sec.entropy, 4),
                    "raw_size": sec.size,
                })
                
        elif isinstance(binary, lief.MachO.Binary):
            file_type = "macho"
            platform = "macos_or_ios"
            architecture = str(binary.header.cpu_type).split(".")[-1]

    if progress_cb: progress_cb("Đang tính toán Entropy tổng thể...")
    overall_entropy = _entropy(data)
    
    if progress_cb: progress_cb("Đang quét cấu trúc Packer bằng YARA...")
    die_packer, die_engine = _detect_packer_yara(absolute_path)
    
    packer = die_packer if die_packer != "unknown" else _estimate_packer(overall_entropy, file_type, sections)
    engine = die_engine if die_engine != "unknown" else _detect_engine(absolute_path, data, signals)

    if progress_cb: progress_cb("Đang đợi kiểm tra Chữ ký số (sẽ do C# xử lý)...")

    if progress_cb and packer == "unknown" and file_type == "unknown":
        progress_cb("Đang đợi Deep Scan bằng Binwalk (sẽ do C# xử lý bằng Rust Core)...")

    return {
        "path": absolute_path,
        "exists": True,
        "is_file": True,
        "size": len(data),
        "hashes": hashes,
        "magic": data[:16].hex(" "),
        "file_type": file_type,
        "platform": platform,
        "architecture": architecture,
        "entropy": {
            "overall": round(overall_entropy, 4),
            "sections": sections,
            "deep_scan": "Pending C# processing" if (packer == "unknown" and file_type == "unknown") else None
        },
        "imports_summary": imports_summary,
        "entrypoint": entrypoint,
        "packer": packer,
        "engine": engine,
        "authenticode": "Pending C# processing" if (platform == "windows" or file_type == "pe") else None,
        "signals": signals,
        "recommended_pipeline": _recommend_pipeline(file_type, platform, packer, engine),
    }


def _read_file(path: str) -> bytes:
    with open(path, "rb") as file:
        return file.read()


def _hash_file(data: bytes) -> dict[str, str]:
    return {
        "md5": hashlib.md5(data).hexdigest(),
        "sha256": hashlib.sha256(data).hexdigest(),
    }


def _classify_file_fallback(path: str, data: bytes) -> tuple[str, str, list[str]]:
    signals: list[str] = []
    extension = os.path.splitext(path)[1].lower()

    if data.startswith(b"MZ"):
        signals.append("mz_header")
        return "pe", "windows", signals

    if data.startswith(b"\x7fELF"):
        signals.append("elf_header")
        return "elf", "linux_or_android", signals

    if data.startswith(b"dex\n"):
        signals.append("dex_header")
        return "dex", "android", signals

    if data.startswith(b"PK\x03\x04"):
        signals.append("zip_header")
        if _zip_contains(path, "AndroidManifest.xml") or _zip_contains(path, "classes.dex") or extension == ".apk":
            signals.append("apk_structure")
            return "apk", "android", signals
        return "zip", "general", signals

    if extension in (".so",):
        signals.append("extension_so")
        return "shared_object", "linux_or_android", signals

    return "unknown", "unknown", signals


def _zip_contains(path: str, name: str) -> bool:
    try:
        with zipfile.ZipFile(path) as archive:
            return name in archive.namelist()
    except zipfile.BadZipFile:
        return False


def _entropy(data: bytes) -> float:
    if not data:
        return 0.0

    counts = Counter(data)
    length = len(data)
    return -sum((count / length) * math.log2(count / length) for count in counts.values())


def _estimate_packer(overall_entropy: float, file_type: str, sections: list) -> str:
    if file_type in ("pe", "elf", "shared_object"):
        # LIEF section based detection
        for sec in sections:
            name = sec["name"]
            if name in (".vmp0", ".vmp1", ".UPX0", ".UPX1", ".themida"):
                return f"suspicious_section_name_{name.strip('.')}"
            if sec["entropy"] > 7.5:
                return "suspicious_high_entropy_section"
                
        # Fallback to overall entropy
        if overall_entropy >= 7.2:
            return "suspicious_high_entropy"

    return "unknown"


def _detect_engine(path: str, data: bytes, signals: list[str]) -> str:
    parent = os.path.dirname(path)
    if os.path.exists(os.path.join(parent, "global-metadata.dat")):
        signals.append("global_metadata_found")
        return "unity_il2cpp"

    if b"Dart" in data[:1024 * 1024] or b"Flutter" in data[:1024 * 1024]:
        signals.append("dart_or_flutter_string")
        return "flutter_dart"

    return "unknown"


_yara_rules = None

def _load_yara_rules():
    global _yara_rules
    if _yara_rules is not None:
        return _yara_rules
        
    try:
        import yara
        import os
        
        root_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        packers_dir = os.path.join(root_dir, "Rules", "yara-rules", "packers")
        
        filepaths = {}
        if os.path.exists(packers_dir):
            for file in os.listdir(packers_dir):
                if file.endswith(".yar") or file.endswith(".yara"):
                    filepaths[file] = os.path.join(packers_dir, file)
                    
        if filepaths:
            _yara_rules = yara.compile(filepaths=filepaths)
        return _yara_rules
    except Exception as e:
        print(f"YARA Compile Error: {e}")
        return None

def _detect_packer_yara(path: str) -> tuple[str, str]:
    packer = "unknown"
    engine = "unknown"
    
    rules = _load_yara_rules()
    if not rules:
        return packer, engine
        
    try:
        matches = rules.match(path)
        if matches:
            generic_rules = {
                "IsWindowsGUI", "IsConsole", "IsPacked", "HasDebugData", 
                "HasRichSignature", "IsDLL", "IsEXE", "HasOverlay", 
                "HasModifiedDOSMessage", "HasOverlayData"
            }
            names = []
            for m in matches:
                name = m.rule
                if name not in names and not name.startswith("IsPE") and name not in generic_rules:
                    names.append(name)
            
            if len(names) > 0:
                packer = names[0]
            if len(names) > 1:
                engine = ", ".join(names[1:4])
    except Exception:
        pass
        
    return packer, engine


def _run_binwalk(path: str) -> list[dict]:
    try:
        import subprocess
        import os
        # Gọi file thực thi Binwalk v3 (Rust) cục bộ trong thư mục Tools
        script_dir = os.path.dirname(os.path.abspath(__file__))
        binwalk_exe = os.path.join(script_dir, "..", "binwalk", "binwalk.exe")
        binwalk_exe = os.path.normpath(binwalk_exe)
        
        if not os.path.exists(binwalk_exe):
            return [{"error": f"Binwalk v3 (Rust) not found at {binwalk_exe}"}]
        
        # Binwalk v3 CLI. Flag `-e` (extract) bị lược bỏ để tập trung scan.
        result = subprocess.run(
            [binwalk_exe, path], 
            capture_output=True, text=True, timeout=30
        )
        
        results = []
        lines = result.stdout.splitlines()
        for line in lines:
            line = line.strip()
            if not line or line.startswith("DECIMAL") or line.startswith("----"):
                continue
            
            # Format chuẩn của binwalk: DECIMAL \t HEXADECIMAL \t DESCRIPTION
            parts = line.split(maxsplit=2)
            if len(parts) >= 3 and parts[0].isdigit():
                results.append({
                    "offset": int(parts[0]),
                    "description": parts[2]
                })
                
        return results[:20] # Return top 20 matches to avoid bloat
    except subprocess.TimeoutExpired:
        return [{"error": "Binwalk scan timed out after 30 seconds"}]
    except Exception as e:
        return [{"error": str(e)}]




def _recommend_pipeline(file_type: str, platform: str, packer: str, engine: str) -> str:
    if file_type == "apk":
        return "android.apk_static"

    if file_type == "dex":
        return "android.dex_static"

    if file_type == "pe" and (packer != "unknown" or "suspicious" in packer):
        return "windows.packed_pe"

    if file_type == "pe":
        return "windows.clean_pe"

    if file_type in ("elf", "shared_object") and platform in ("linux_or_android", "android"):
        return "android.native_or_linux_elf"

    return "manual.review"


from mcp.server.fastmcp import Context

@mcp.tool()
def triage_file(path: str, ctx: Context = None) -> dict[str, Any]:
    """Analyze a local file and return basic static triage JSON."""
    def report_progress(msg: str):
        if ctx:
            ctx.info(msg)
            
    result = _triage_file(path, progress_cb=report_progress)
    # Tự động gen Markdown Report và đính kèm vào response
    try:
        if ctx: ctx.info("Đang tổng hợp báo cáo Markdown...")
        from report_generator import generate_markdown_report
        result["markdown_report"] = generate_markdown_report(result)
    except Exception as e:
        result["markdown_report"] = f"Lỗi khi tạo Markdown Report: {e}"
        
    return result


# Hop den xu ly hash:
# Input la duong dan file local, output la dict JSON-serializable gom path/md5/size.
def _hash_md5(path: str) -> dict[str, Any]:
    absolute_path = os.path.abspath(path)

    if not os.path.exists(absolute_path):
        raise FileNotFoundError(f"File does not exist: {absolute_path}")

    if not os.path.isfile(absolute_path):
        raise ValueError(f"Path is not a file: {absolute_path}")

    digest = hashlib.md5()
    size = 0

    with open(absolute_path, "rb") as file:
        while True:
            chunk = file.read(1024 * 1024)
            if not chunk:
                break

            size += len(chunk)
            digest.update(chunk)

    return {
        "path": absolute_path,
        "md5": digest.hexdigest(),
        "size": size,
    }


@mcp.tool()
def md5_file(path: str) -> dict[str, Any]:
    """Calculate the MD5 hash and byte size of a local file."""
    return _hash_md5(path)


if __name__ == "__main__":
    mcp.run(transport="stdio")

