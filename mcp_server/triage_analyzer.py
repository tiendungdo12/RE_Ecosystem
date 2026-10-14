# triage_analyzer.py
"""
Core Deterministic Static Analysis Engine (RE Ecosystem 2.0).
Doc lap voi FastMCP va LLM - phuc vu cho ca Subprocess Lane va Legacy MCP Wrapper.
Tich hop LIEF, AXML Android Parser, Section Anomaly & IAT Threat Matrix, YARA Packer Detection.
"""

import hashlib
import io
import math
import os
import struct
import sys
import zipfile
from collections import Counter
from typing import Any, Optional

# Ensure directory is in sys.path
_current_dir = os.path.dirname(os.path.abspath(__file__))
if _current_dir not in sys.path:
    sys.path.insert(0, _current_dir)

import lief

from axml_parser import parse_android_container, _safe_open_zip
from malware_rules import (
    ELF_SUSPICIOUS_SYMBOLS,
    KNOWN_PACKER_SECTIONS,
    STANDARD_PE_SECTIONS,
    WINDOWS_API_CATEGORIES,
)


def _read_file(path: str, max_size: int = 200 * 1024 * 1024) -> bytes:
    file_size = os.path.getsize(path)
    to_read = min(file_size, max_size)
    with open(path, "rb") as file:
        return file.read(to_read)


def _is_apk_zip(path: str) -> bool:
    try:
        with _safe_open_zip(path) as zf:
            targets = ("AndroidManifest.xml", "classes.dex", "manifest.json")
            for target in targets:
                try:
                    zf.getinfo(target)
                    return True
                except KeyError:
                    pass
            return False
    except Exception:
        return False


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
        if _is_apk_zip(path) or extension in (".apk", ".xapk", ".apks"):
            signals.append("apk_structure")
            return "apk", "android", signals
        return "zip", "general", signals

    if extension in (".so",):
        signals.append("extension_so")
        return "shared_object", "linux_or_android", signals

    return "unknown", "unknown", signals


def _entropy(data: bytes) -> float:
    if not data:
        return 0.0
    counts = Counter(data)
    length = len(data)
    return -sum((count / length) * math.log2(count / length) for count in counts.values())


_yara_rules = None


def _load_yara_rules() -> tuple[Any, Optional[str]]:
    global _yara_rules
    if _yara_rules is not None:
        return _yara_rules, None

    try:
        import yara
        root_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        packers_dir = os.path.join(root_dir, "Rules", "yara-rules", "packers")

        filepaths = {}
        if os.path.exists(packers_dir):
            for file in os.listdir(packers_dir):
                if file.endswith(".yar") or file.endswith(".yara"):
                    filepaths[file] = os.path.join(packers_dir, file)

        if filepaths:
            _yara_rules = yara.compile(filepaths=filepaths)
            return _yara_rules, None
        return None, "No YARA rules found in Rules/yara-rules/packers directory"
    except Exception as e:
        return None, str(e)


def _detect_packer_yara(path: str, data: Optional[bytes] = None) -> tuple[str, str, Optional[str]]:
    packer = "unknown"
    engine = "unknown"

    rules, err = _load_yara_rules()
    if err:
        return packer, engine, f"YARA rules compilation failed: {err}"
    if not rules:
        return packer, engine, "No YARA rules available for scanning"

    try:
        if data is not None:
            matches = rules.match(data=data)
        else:
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
        return packer, engine, None
    except Exception as match_err:
        return packer, engine, f"YARA scanning failed: {match_err}"


def _calculate_file_hashes(path: str) -> tuple[str, str]:
    try:
        md5 = hashlib.md5()
        sha256 = hashlib.sha256()
        with open(path, "rb") as f:
            while chunk := f.read(1024 * 1024):
                md5.update(chunk)
                sha256.update(chunk)
        return md5.hexdigest(), sha256.hexdigest()
    except Exception:
        return "N/A", "N/A"


def _estimate_packer(
    overall_entropy: float,
    file_type: str,
    sections: list,
    android_meta: Optional[dict] = None,
) -> str:
    if android_meta and android_meta.get("packer") != "none":
        return android_meta["packer"]

    if file_type in ("pe", "elf", "shared_object"):
        for sec in sections:
            name = sec.get("name", "")
            if name in KNOWN_PACKER_SECTIONS:
                return KNOWN_PACKER_SECTIONS[name]
            if sec.get("entropy", 0) > 7.5:
                return "Suspicious High Entropy Section (>7.5)"

        if overall_entropy >= 7.2:
            return "Suspicious High Entropy (>7.2)"

    return "unknown"


def _detect_engine(
    path: str,
    data: bytes,
    signals: list[str],
    android_meta: Optional[dict] = None,
) -> str:
    if android_meta and android_meta.get("engine") != "unknown":
        return android_meta["engine"]

    parent = os.path.dirname(path)
    if os.path.exists(os.path.join(parent, "global-metadata.dat")):
        signals.append("global_metadata_found")
        return "unity_il2cpp"

    sample = data[: 1024 * 1024]
    if b"Dart" in sample or b"Flutter" in sample:
        signals.append("dart_or_flutter_string")
        return "flutter_dart"

    return "unknown"


def _calculate_risk_assessment(
    file_type: str,
    overall_entropy: float,
    sections: list,
    imports_summary: dict,
    packer: str,
    android_metadata: Optional[dict] = None,
    signals: Optional[list[str]] = None,
    analyzer_errors: Optional[dict[str, str]] = None,
    size_truncated: bool = False,
) -> dict[str, Any]:
    score = 0
    factors: list[str] = []

    # 1. API Threats
    categories = imports_summary.get("categories", {})
    if "memory_injection" in categories:
        score += 30
        apis_sample = ", ".join(categories["memory_injection"][:3])
        factors.append(f"Phát hiện API Tiêm mã & Can thiệp bộ nhớ: {apis_sample}")

    if "anti_analysis" in categories:
        score += 15
        apis_sample = ", ".join(categories["anti_analysis"][:3])
        factors.append(f"Phát hiện API Chống dịch ngược / Debugger: {apis_sample}")

    if "network_c2" in categories:
        score += 15
        apis_sample = ", ".join(categories["network_c2"][:3])
        factors.append(f"Phát hiện API Giao tiếp Mạng / Tải file ngầm: {apis_sample}")

    if "persistence_registry" in categories:
        score += 15
        apis_sample = ", ".join(categories["persistence_registry"][:3])
        factors.append(f"Phát hiện API Tự duy trì / Can thiệp Service & Registry: {apis_sample}")

    if "privilege_escalation" in categories:
        score += 15
        apis_sample = ", ".join(categories["privilege_escalation"][:2])
        factors.append(f"Phát hiện API Thao tác Quyền & Token: {apis_sample}")

    if "crypto_ransomware" in categories:
        score += 10
        apis_sample = ", ".join(categories["crypto_ransomware"][:2])
        factors.append(f"Phát hiện API Mật mã / Mã hóa: {apis_sample}")

    # 2. Section Anomalies
    wx_sections = [s["name"] for s in sections if s.get("is_writable_and_executable")]
    if wx_sections:
        score += 25
        factors.append(f"Section có quyền vừa Ghi vừa Thực thi (W+X): {', '.join(wx_sections)}")

    high_ent_secs = [s["name"] for s in sections if s.get("entropy", 0) > 7.2]
    if high_ent_secs:
        score += 20
        factors.append(f"Section có Entropy cao bất thường (>7.2): {', '.join(high_ent_secs)}")

    empty_raw_secs = [s["name"] for s in sections if "empty_raw_size" in s.get("anomalies", [])]
    if empty_raw_secs:
        score += 15
        factors.append(f"Section có Raw Size = 0 (Unpacking Stub Buffer): {', '.join(empty_raw_secs)}")

    # 3. Packer Presence
    if packer != "unknown" and packer != "none":
        score += 25
        factors.append(f"Phát hiện Packer / Protector: {packer}")

    # 4. Android Dangerous Permissions & Threats
    if android_metadata:
        dang_perms = android_metadata.get("dangerous_permissions", [])
        critical_perms = [p for p in dang_perms if p.get("risk") == "critical"]
        high_perms = [p for p in dang_perms if p.get("risk") == "high"]

        if critical_perms:
            score += 30
            perm_names = ", ".join([p["name"].split(".")[-1] for p in critical_perms[:3]])
            factors.append(f"Quyền Android cực kỳ nguy hiểm (Overlay/Accessibility/DeviceAdmin/Install): {perm_names}")

        if high_perms:
            score += 15
            perm_names = ", ".join([p["name"].split(".")[-1] for p in high_perms[:3]])
            factors.append(f"Quyền Android nhạy cảm (SMS/Audio/Camera/Location): {perm_names}")

    # Cap score at 100
    score = min(score, 100)

    has_errors = bool(analyzer_errors)
    has_lief_fail = signals is not None and "lief_parse_failed" in signals

    SUPPORTED_STATIC_ANALYSIS_TYPES = {"pe", "elf", "shared_object", "apk", "xapk", "apks", "dex"}

    if size_truncated:
        level = "UNKNOWN"
        factors.append("Tệp có dung lượng vượt quá giới hạn phân tích tĩnh (200 MiB); phân tích cấu trúc chỉ hoàn tất trên phần dữ liệu đầu.")
    elif file_type not in SUPPORTED_STATIC_ANALYSIS_TYPES or has_errors or has_lief_fail:
        level = "UNKNOWN"
        if file_type == "macho":
            factors.append("Định dạng Mach-O chưa có bộ phân tích cấu trúc chuyên sâu; kết quả chưa xác định (cần kiểm tra thủ công).")
        else:
            factors.append("Định dạng tệp không xác định hoặc phân tích cấu trúc gặp lỗi / chưa hoàn tất.")
    elif score >= 75:
        level = "CRITICAL"
    elif score >= 50:
        level = "HIGH"
    elif score >= 25:
        level = "MEDIUM"
    elif score > 0:
        level = "LOW"
    else:
        level = "CLEAN"

    return {
        "risk_score": score,
        "risk_level": level,
        "risk_factors": factors,
    }


def _recommend_pipeline(
    file_type: str,
    platform: str,
    packer: str,
    engine: str,
    android_meta: Optional[dict] = None,
    size_truncated: bool = False,
    has_errors: bool = False,
) -> str:
    if has_errors or size_truncated or file_type == "unknown":
        return "manual.review"

    if file_type in ("apk", "xapk", "apks") or platform == "android":
        if engine == "unity_il2cpp":
            return "android.unity_il2cpp"
        if engine == "flutter_dart":
            return "android.flutter"
        if packer not in ("unknown", "none"):
            return "android.packed_apk"
        return "android.apk_static"

    if file_type == "dex":
        return "android.dex_static"

    if engine == "unity_il2cpp":
        return "engine.unity_il2cpp"

    if engine == "flutter_dart":
        return "engine.flutter"

    if file_type == "pe":
        if packer not in ("unknown", "none") or "Suspicious" in packer:
            return "windows.packed_pe"
        return "windows.clean_pe"

    if file_type in ("elf", "shared_object") and platform in ("linux_or_android", "android"):
        return "android.native_or_linux_elf"

    return "manual.review"


def _get_magic_hex(data: bytes) -> str:
    return data[:16].hex(" ")


def _triage_file(
    path: str,
    progress_cb=None,
    precomputed_hashes: Optional[dict[str, str]] = None,
) -> dict[str, Any]:
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

    if progress_cb:
        progress_cb("Đọc dữ liệu file...")
    file_size = os.path.getsize(absolute_path)
    SIZE_LIMIT = 200 * 1024 * 1024
    data = _read_file(absolute_path, max_size=SIZE_LIMIT)
    size_truncated = file_size > SIZE_LIMIT
    ext = os.path.splitext(absolute_path)[1].lower()

    file_type = "unknown"
    platform = "unknown"
    architecture = "unknown"
    entrypoint = None
    sections: list[dict[str, Any]] = []
    signals: list[str] = []
    android_metadata: Optional[dict[str, Any]] = None
    analyzer_errors: dict[str, str] = {}

    imports_summary: dict[str, Any] = {
        "total_imports": 0,
        "suspicious_count": 0,
        "categories": {},
        "all_suspicious": [],
        "libraries": [],
    }
    exports_summary: dict[str, Any] = {
        "total_exports": 0,
        "suspicious": [],
        "top_exports": [],
    }

    # 1. Android Container Check (.apk, .xapk, .apks)
    if ext in (".apk", ".xapk", ".apks") or (data.startswith(b"PK\x03\x04") and _is_apk_zip(absolute_path)):
        if progress_cb:
            progress_cb("Phân tích cấu trúc gói ứng dụng Android...")
        file_type = "apk" if ext != ".xapk" and ext != ".apks" else ext.strip(".")
        platform = "android"
        try:
            android_metadata = parse_android_container(absolute_path)
            architecture = (
                ", ".join(android_metadata.get("native_architectures", []))
                if android_metadata.get("native_architectures")
                else "Dalvik/Universal"
            )
            signals.append("android_package")
            if android_metadata.get("engine") != "unknown":
                signals.append(f"engine_{android_metadata.get('engine')}")
            if android_metadata.get("packer") != "none":
                signals.append(f"packer_{android_metadata.get('packer')}")

            # Check for error inside metadata (e.g. invalid zip archive)
            if android_metadata.get("error"):
                analyzer_errors["axml"] = str(android_metadata["error"])
                signals.append("android_parse_failed")
        except Exception as axml_err:
            analyzer_errors["axml"] = str(axml_err)
            signals.append("android_parse_failed")

    # 2. LIEF Binary Parsing (PE, ELF, Mach-O)
    else:
        if progress_cb:
            progress_cb("Phân tích cấu trúc file bằng LIEF...")
        binary = None
        try:
            if size_truncated:
                binary = lief.parse(data)
            else:
                binary = lief.parse(absolute_path)
        except Exception as lief_parse_err:
            analyzer_errors["lief"] = str(lief_parse_err)
            signals.append("lief_parse_failed")

        if binary is None:
            if "lief" not in analyzer_errors:
                analyzer_errors["lief"] = "LIEF parse returned null (unsupported or non-binary format)"
            signals.append("lief_parse_failed")
            if progress_cb:
                progress_cb("Không thể parse bằng LIEF, dùng cách phân loại fallback...")
            file_type, platform, signals = _classify_file_fallback(absolute_path, data)
        else:
            if isinstance(binary, lief.PE.Binary):
                file_type = "pe"
                platform = "windows"
                architecture = str(binary.header.machine).split(".")[-1]
                if binary.optional_header:
                    entrypoint = hex(binary.optional_header.addressof_entrypoint)

                # PE Section Analysis & Anomaly Detection
                for sec in binary.sections:
                    sec_name = sec.name.strip("\x00") if sec.name else "[UNNAMED]"
                    sec_entropy = round(sec.entropy, 4)
                    raw_sz = sec.size
                    virt_sz = sec.virtual_size
                    characteristics = sec.characteristics

                    is_exec = bool(characteristics & 0x20000000)
                    is_read = bool(characteristics & 0x40000000)
                    is_write = bool(characteristics & 0x80000000)
                    is_wx = is_exec and is_write

                    anomalies = []
                    if sec_entropy > 7.5:
                        anomalies.append("extreme_entropy")
                    elif sec_entropy > 7.2:
                        anomalies.append("high_entropy")

                    if sec_name in KNOWN_PACKER_SECTIONS:
                        anomalies.append(f"packer_section_{KNOWN_PACKER_SECTIONS[sec_name]}")
                    elif sec_name not in STANDARD_PE_SECTIONS and sec_name != "[UNNAMED]":
                        anomalies.append("non_standard_name")
                    elif sec_name == "[UNNAMED]":
                        anomalies.append("unnamed_section")

                    if raw_sz == 0 and virt_sz > 0:
                        anomalies.append("empty_raw_size")
                    elif virt_sz > 0 and virt_sz > raw_sz * 3:
                        anomalies.append("virtual_size_bloat")

                    if is_wx:
                        anomalies.append("writable_and_executable")

                    sections.append({
                        "name": sec_name,
                        "entropy": sec_entropy,
                        "raw_size": raw_sz,
                        "virtual_size": virt_sz,
                        "characteristics": f"0x{characteristics:08X}",
                        "is_writable_and_executable": is_wx,
                        "anomalies": anomalies,
                        "is_flagged": len(anomalies) > 0,
                    })

                # Categorized IAT Analysis (Data Pruning)
                total_imp_count = 0
                detected_by_cat: dict[str, list[str]] = {}
                all_suspicious: list[str] = []
                imported_libs: list[str] = []

                if binary.has_imports:
                    for imp in binary.imports:
                        lib_name = imp.name
                        if lib_name:
                            imported_libs.append(lib_name)
                        for entry in imp.entries:
                            total_imp_count += 1
                            api_name = entry.name
                            if not api_name:
                                continue

                            for cat_key, cat_data in WINDOWS_API_CATEGORIES.items():
                                if api_name in cat_data["apis"]:
                                    if cat_key not in detected_by_cat:
                                        detected_by_cat[cat_key] = []
                                    if api_name not in detected_by_cat[cat_key]:
                                        detected_by_cat[cat_key].append(api_name)
                                    if api_name not in all_suspicious:
                                        all_suspicious.append(api_name)

                imports_summary = {
                    "total_imports": total_imp_count,
                    "suspicious_count": len(all_suspicious),
                    "categories": detected_by_cat,
                    "all_suspicious": all_suspicious,
                    "libraries": imported_libs[:20],
                }

                SUSPICIOUS_EXPORT_NAMES = {
                    "install", "servicemain", "run", "work", "payload", "start",
                    "dllregisterserver", "dllunregisterserver",
                    "reflectiveloader", "executeshellcode", "main_inject",
                }

                if binary.has_exports:
                    all_exports = []
                    suspicious_exports = []
                    ordinal_only_count = 0
                    for exp in binary.exported_functions:
                        if exp.name:
                            all_exports.append(exp.name)
                            if exp.name.lower() in SUSPICIOUS_EXPORT_NAMES:
                                suspicious_exports.append(exp.name)
                        else:
                            ordinal_only_count += 1

                    exports_summary = {
                        "total_exports": len(all_exports) + ordinal_only_count,
                        "suspicious": suspicious_exports,
                        "top_exports": all_exports[:10],
                        "ordinal_only_count": ordinal_only_count,
                    }

                    if ordinal_only_count > 5:
                        signals.append("many_ordinal_exports")
                    if len(all_exports) + ordinal_only_count > 500:
                        signals.append("excessive_exports_count")

            elif isinstance(binary, lief.ELF.Binary):
                file_type = "elf"
                platform = "linux_or_android"
                architecture = str(binary.header.machine_type).split(".")[-1]
                entrypoint = hex(binary.header.entrypoint)

                for sec in binary.sections:
                    sec_name = sec.name.strip("\x00") if sec.name else "[UNNAMED]"
                    sec_entropy = round(sec.entropy, 4)
                    anomalies = []
                    if sec_entropy > 7.2:
                        anomalies.append("high_entropy")

                    sections.append({
                        "name": sec_name,
                        "entropy": sec_entropy,
                        "raw_size": sec.size,
                        "virtual_size": sec.virtual_size if hasattr(sec, "virtual_size") else sec.size,
                        "anomalies": anomalies,
                        "is_flagged": len(anomalies) > 0,
                    })

                imported_syms = set()
                for sym in binary.imported_symbols:
                    if sym.name:
                        imported_syms.add(sym.name)

                total_imp_count = len(imported_syms)
                detected_by_cat: dict[str, list[str]] = {}
                all_suspicious: list[str] = []
                for cat_key, cat_data in ELF_SUSPICIOUS_SYMBOLS.items():
                    found = imported_syms & cat_data["apis"]
                    if found:
                        detected_by_cat[cat_key] = sorted(list(found))
                        all_suspicious.extend(found)

                imports_summary = {
                    "total_imports": total_imp_count,
                    "suspicious_count": len(all_suspicious),
                    "categories": detected_by_cat,
                    "all_suspicious": all_suspicious,
                    "libraries": [],
                }

                if not binary.has_symbol_version:
                    signals.append("stripped_binary")

            elif isinstance(binary, lief.MachO.Binary):
                file_type = "macho"
                platform = "macos_or_ios"
                architecture = str(binary.header.cpu_type).split(".")[-1]
                signals.append("unsupported_format_macho")

    if progress_cb:
        progress_cb("Đang tính toán Entropy tổng thể...")
    overall_entropy = round(_entropy(data), 4)

    # Detect Engine & Packer (YARA first, then heuristic fallback)
    yara_packer, yara_engine, yara_err = _detect_packer_yara(absolute_path, data=data)
    if yara_err:
        analyzer_errors["yara"] = yara_err
        signals.append("yara_scan_failed")
    packer = yara_packer if yara_packer != "unknown" else _estimate_packer(overall_entropy, file_type, sections, android_metadata)
    engine = yara_engine if yara_engine != "unknown" else _detect_engine(absolute_path, data, signals, android_metadata)

    magic_hex = _get_magic_hex(data)

    # Full file hash calculation: use precomputed hashes from C# if provided, otherwise stream
    if precomputed_hashes and precomputed_hashes.get("md5") and precomputed_hashes.get("sha256"):
        md5_hash = precomputed_hashes["md5"]
        sha256_hash = precomputed_hashes["sha256"]
    else:
        md5_hash, sha256_hash = _calculate_file_hashes(absolute_path)
        if md5_hash == "N/A" or sha256_hash == "N/A":
            analyzer_errors["hash"] = "Không thể tính toán mã băm cho toàn bộ tệp tin."

    # Heuristic Risk Assessment (calculated after all analyzers and hashes)
    risk_assessment = _calculate_risk_assessment(
        file_type=file_type,
        overall_entropy=overall_entropy,
        sections=sections,
        imports_summary=imports_summary,
        packer=packer,
        android_metadata=android_metadata,
        signals=signals,
        analyzer_errors=analyzer_errors,
        size_truncated=size_truncated,
    )

    result = {
        "path": absolute_path,
        "exists": True,
        "is_file": True,
        "size": file_size,
        "size_truncated": size_truncated,
        "analyzer_errors": analyzer_errors,
        "magic": magic_hex,
        "hashes": {"md5": md5_hash, "sha256": sha256_hash},
        "file_type": file_type,
        "platform": platform,
        "architecture": architecture,
        "entropy": {
            "overall": overall_entropy,
            "is_high": overall_entropy >= 7.2,
            "sections": sections,
            "anomalies_count": sum(1 for s in sections if s.get("is_flagged", False)),
            "deep_scan": "Pending C# processing" if (packer == "unknown" and file_type in ("unknown", "zip")) else None,
        },
        "imports_summary": imports_summary,
        "exports_summary": exports_summary,
        "entrypoint": entrypoint,
        "packer": packer,
        "engine": engine,
        "authenticode": "Pending C# processing" if (platform == "windows" or file_type == "pe") else None,
        "android_metadata": android_metadata,
        "risk_assessment": risk_assessment,
        "signals": signals,
        "recommended_pipeline": _recommend_pipeline(
            file_type,
            platform,
            packer,
            engine,
            android_metadata,
            size_truncated=size_truncated,
            has_errors=bool(analyzer_errors),
        ),
    }

    return result
