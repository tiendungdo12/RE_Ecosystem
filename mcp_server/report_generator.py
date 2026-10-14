# report_generator.py
"""
Trình tạo Executive Markdown Report chuẩn hóa cho Triage Module.
Tối ưu hóa token, trực quan, hỗ trợ đa nền tảng (Windows PE, Linux/ELF, Android APK/XAPK).
"""

from typing import Any


def _sanitize_md_text(val: Any) -> str:
    """Sanitize scalar text to prevent markdown structure injection, table breakage, or line breaks."""
    if val is None:
        return ""
    s = str(val)
    s = s.replace("\r", " ").replace("\n", " ").strip()
    s = s.replace("`", "'").replace("|", "/")
    return s


def _sanitize_md_block(val: Any) -> str:
    """Sanitize multiline text block (e.g. DIE output) to prevent markdown code block breakout."""
    if val is None:
        return ""
    s = str(val).strip()
    s = s.replace("```", "'''")
    return s


def generate_markdown_report(triage_data: dict[str, Any]) -> str:
    md: list[str] = []

    # 0. Check validity
    is_file = triage_data.get("is_file", False)
    path = _sanitize_md_text(triage_data.get("path", "Unknown"))

    if not is_file or not triage_data.get("exists", True):
        md.append("# 🛡️ Báo Cáo Phân Tích Tĩnh (Triage Report)")
        md.append(f"**Tệp tin:** `{path}`")
        md.append("> ❌ **Lỗi:** Tệp không tồn tại hoặc không thể truy cập.")
        return "\n".join(md)

    # 1. Header & Risk Score Badge
    risk_info = triage_data.get("risk_assessment", {})
    risk_score = risk_info.get("risk_score", 0)
    risk_level = risk_info.get("risk_level", "CLEAN")
    risk_factors = risk_info.get("risk_factors", [])

    risk_badge = _get_risk_badge(risk_level, risk_score)

    md.append(f"# 🛡️ Báo Cáo Phân Tích Tĩnh — Executive Triage")
    md.append(f"**Tệp tin:** `{path}`  ")
    md.append(f"**Đánh giá Rủi ro:** {risk_badge}")

    if risk_factors:
        md.append("\n### 🚨 Yếu tố Rủi ro Phát hiện:")
        for factor in risk_factors:
            md.append(f"- ⚠️ {_sanitize_md_text(factor)}")

    # 2. File Metadata Summary Table
    file_type = _sanitize_md_text(str(triage_data.get("file_type", "Unknown")).upper())
    platform = _sanitize_md_text(str(triage_data.get("platform", "Unknown")).capitalize())
    arch = _sanitize_md_text(str(triage_data.get("architecture", "Unknown")))
    size = triage_data.get("size", 0)
    magic = _sanitize_md_text(triage_data.get("magic", ""))

    hashes = triage_data.get("hashes", {})
    md5 = _sanitize_md_text(hashes.get("md5", "N/A"))
    sha256 = _sanitize_md_text(hashes.get("sha256", "N/A"))

    analyzer_errors = triage_data.get("analyzer_errors", {})
    if analyzer_errors:
        err_list = [f"`{_sanitize_md_text(k)}`: {_sanitize_md_text(v)}" for k, v in analyzer_errors.items()]
        md.append(f"\n> ⚠️ **Cảnh báo lỗi phân tích ({', '.join([_sanitize_md_text(k) for k in analyzer_errors.keys()])}):** Một số bộ phân tích tĩnh gặp lỗi ({'; '.join(err_list)}). Kết quả chỉ phản ánh một phần tệp tin.")

    if triage_data.get("size_truncated", False):
        md.append(f"\n> ⚠️ **Lưu ý tệp dung lượng lớn (> 200 MiB):** Tệp tin có kích thước {size:,} bytes. Mã băm SHA256/MD5 được tính toán trên toàn bộ tệp tin, trong khi phân tích cấu trúc section, Entropy và quét chuỗi/quy tắc bộ nhớ được giới hạn trên 200 MiB đầu tiên.")

    md.append("\n## 1. Thông Tin Tệp Tin & Mã Băm")
    md.append(f"| Thuộc tính | Giá trị |")
    md.append(f"|---|---|")
    md.append(f"| **Định dạng / Nền tảng** | `{file_type}` / `{platform}` ({arch}) |")
    md.append(f"| **Kích thước** | {size:,} bytes |")
    md.append(f"| **Magic Bytes** | `{magic}` |")
    md.append(f"| **MD5** | `{md5}` |")
    md.append(f"| **SHA256** | `{sha256}` |")

    # 3. Authenticode (Windows / PE)
    auth = triage_data.get("authenticode")
    if auth is not None and platform.lower() == "windows":
        auth_status = "Unknown"
        auth_subject = "N/A"
        if isinstance(auth, dict):
            auth_status = _sanitize_md_text(auth.get("Status", "Unknown"))
            auth_subject = _sanitize_md_text(auth.get("Subject", "N/A"))
        elif isinstance(auth, str):
            auth_status = _sanitize_md_text(auth)

        if auth_status == "Valid":
            auth_display = f"✅ **Hợp lệ** (`{auth_subject}`)"
        elif auth_status == "NotSigned":
            auth_display = "⚠️ **Không có chữ ký (NotSigned)**"
        elif "Pending" in auth_status:
            auth_display = "⏳ *Đang chờ xác thực native C#...*"
        else:
            auth_display = f"❌ **Không hợp lệ ({auth_status})**"
        md.append(f"| **Chữ ký số (Authenticode)** | {auth_display} |")

    # 4. Android Profile (Nếu là APK / XAPK / APKS)
    android_meta = triage_data.get("android_metadata")
    if android_meta:
        md.append("\n## 2. Hồ Sơ Ứng Dụng Android")
        pkg = _sanitize_md_text(android_meta.get("package_name", "unknown"))
        ver = f"{_sanitize_md_text(android_meta.get('version_name', ''))} (Build {_sanitize_md_text(android_meta.get('version_code', ''))})"
        main_act = _sanitize_md_text(android_meta.get("main_activity", "unknown"))
        engine = _sanitize_md_text(android_meta.get("engine", "native"))
        packer = _sanitize_md_text(android_meta.get("packer", "none"))
        archs = ", ".join([_sanitize_md_text(a) for a in android_meta.get("native_architectures", [])]) or "None (DEX only)"

        md.append(f"- **Package Name:** `{pkg}`")
        md.append(f"- **Phiên bản:** `{ver}`")
        md.append(f"- **Main Activity:** `{main_act}`")
        md.append(f"- **Game Engine / Framework:** `{engine}`")
        md.append(f"- **Protector / Packer:** `{packer}`")
        md.append(f"- **Kiến trúc Native:** `{archs}`")
        md.append(f"- **Số file DEX:** {android_meta.get('dex_count', 0)}")

        dang_perms = android_meta.get("dangerous_permissions", [])
        if dang_perms:
            md.append(f"\n### ⚠️ Quyền Hạn Nguy Hiểm ({len(dang_perms)} quyền):")
            md.append("| Tên Quyền (Permission) | Mức độ | Hành vi / Mô tả |")
            md.append("|---|---|---|")
            for p in dang_perms:
                risk_tag = _get_perm_risk_tag(p.get("risk", "low"))
                p_name = _sanitize_md_text(p.get("name", "").replace("android.permission.", ""))
                p_desc = _sanitize_md_text(p.get("description", ""))
                md.append(f"| `{p_name}` | {risk_tag} | {p_desc} |")

    # 5. Threat Matrix: Suspicious APIs by Category
    imp_summary = triage_data.get("imports_summary", {})
    categories = imp_summary.get("categories", {})
    total_imports = imp_summary.get("total_imports", 0)
    susp_count = imp_summary.get("suspicious_count", 0)

    md.append("\n## 3. Ma Trận Hành Vi & API Nguy Hiểm (Threat Matrix)")
    md.append(f"- **Tổng số API import:** {total_imports} | **Số API nguy hiểm:** {susp_count}")

    if categories:
        for cat_key, apis in categories.items():
            cat_title = _get_category_title(cat_key)
            api_list = ", ".join([f"`{_sanitize_md_text(a)}`" for a in apis])
            md.append(f"- 🔴 **{cat_title}:** {api_list}")
    else:
        if file_type == "PE":
            md.append("- 🟢 Không phát hiện lệnh gọi API trong danh sách đen (Blacklist).")
        else:
            md.append("- ℹ️ Không áp dụng phân tích IAT cho loại tệp này.")

    # 6. Section & Entropy Anomalies
    entropy_info = triage_data.get("entropy", {})
    overall_ent = entropy_info.get("overall", 0.0)
    sections = entropy_info.get("sections", [])
    anomalies_count = entropy_info.get("anomalies_count", 0)

    md.append("\n## 4. Phân Tích Entropy & Cấu Trúc Section")
    ent_status = "🔴 Rất cao (Packed/Encrypted)" if overall_ent >= 7.2 else "🟢 Bình thường"
    md.append(f"- **Entropy tổng thể:** `{overall_ent} / 8.0` — {ent_status}")
    md.append(f"- **Số Section bị cắm cờ (Flagged):** {anomalies_count} / {len(sections)}")

    if sections:
        md.append("\n| Section | Raw Size | Virt Size | Entropy | Cờ Cảnh Báo (Anomalies) |")
        md.append("|---|---|---|---|---|")
        for sec in sections:
            name = _sanitize_md_text(sec.get("name", ""))
            r_sz = sec.get("raw_size", 0)
            v_sz = sec.get("virtual_size", r_sz)
            ent = sec.get("entropy", 0.0)
            anom_list = sec.get("anomalies", [])
            anom_str = ", ".join([_sanitize_md_text(a) for a in anom_list]) if anom_list else "None"
            flag_icon = "🔴" if sec.get("is_flagged") else "🟢"
            md.append(f"| `{name}` | {r_sz:,} | {v_sz:,} | {ent} | {flag_icon} {anom_str} |")

    # 7. Compiler, Packer & Engine Detection
    packer = _sanitize_md_text(triage_data.get("packer", "unknown"))
    engine = _sanitize_md_text(triage_data.get("engine", "unknown"))
    die_raw = triage_data.get("die_analysis")

    md.append("\n## 5. Nhận Diện Trình Biên Dịch & Packer (DIE / Engine)")
    md.append(f"- **Engine / Framework:** `{engine}`")
    md.append(f"- **Packer / Protector:** `{packer}`")
    if die_raw:
        md.append(f"- **DIE Signatures:**\n```text\n{_sanitize_md_block(die_raw)}\n```")

    # 8. Deep Scan (Binwalk v3)
    deep_scan = entropy_info.get("deep_scan")
    if deep_scan and deep_scan != "Pending C# processing" and isinstance(deep_scan, list):
        md.append("\n## 6. Tệp Nhúng Bên Trong (Binwalk Deep Scan)")
        for item in deep_scan:
            off = _sanitize_md_text(item.get("offset", "?"))
            desc = _sanitize_md_text(item.get("description", ""))
            md.append(f"- `Offset: {off}`: {desc}")

    # 9. Next Pipeline Recommendation
    rec_pipe = _sanitize_md_text(triage_data.get("recommended_pipeline", "manual.review"))
    md.append("\n## 7. Khuyến Nghị Hệ Thống (Next Pipeline)")
    md.append(f"👉 **Định tuyến tiếp theo:** `{rec_pipe}`")
    risk_lvl = (triage_data.get("risk_assessment", {}).get("risk_level") or "UNKNOWN").upper()
    has_errors = bool(triage_data.get("analyzer_errors"))
    action_hint = (
        "⚠️ Phân tích chưa hoàn tất hoặc gặp lỗi analyzer. Cần kiểm tra thủ công."
        if (has_errors or risk_lvl == "UNKNOWN")
        else _get_pipeline_action_hint(rec_pipe)
    )
    md.append(f"> *Hành động đề xuất:* {action_hint}")

    return "\n".join(md)


def _get_risk_badge(level: str, score: int) -> str:
    level_upper = (level or "").upper()
    if level_upper == "CRITICAL":
        return f"🔴 **NGUY HIỂM CAO ({score}/100)**"
    if level_upper == "HIGH":
        return f"🟠 **RỦI RO CAO ({score}/100)**"
    if level_upper == "MEDIUM":
        return f"🟡 **TRUNG BÌNH ({score}/100)**"
    if level_upper == "LOW":
        return f"🟢 **THẤP ({score}/100)**"
    if level_upper == "UNKNOWN":
        return f"❓ **CHƯA XÁC ĐỊNH ({score}/100)**"
    if level_upper in ("CLEAN", "SAFE"):
        return f"🛡️ **AN TOÀN ({score}/100)**"
    return f"❓ **CHƯA XÁC ĐỊNH ({score}/100)**"


def _get_perm_risk_tag(risk: str) -> str:
    if risk == "critical":
        return "🔴 Cực kỳ nguy hiểm"
    if risk == "high":
        return "🟠 Nguy hiểm"
    if risk == "medium":
        return "🟡 Cảnh báo"
    return "🟢 Thấp"


def _get_category_title(key: str) -> str:
    titles = {
        "memory_injection": "Tiêm mã & Can thiệp Bộ nhớ (Memory Injection)",
        "dynamic_loading": "Nạp Hàm Động (Dynamic Loading)",
        "process_execution": "Khởi tạo Tiến trình (Process Execution)",
        "anti_analysis": "Chống Phân tích & Gỡ lỗi (Anti-Analysis)",
        "persistence_registry": "Duy trì Tồn tại & Registry (Persistence)",
        "crypto_ransomware": "Mã hóa & Ransomware (Cryptography)",
        "network_c2": "Giao tiếp Mạng & C2 (Network / C2)",
        "privilege_escalation": "Leo thang Đặc quyền (Privilege Escalation)",
        "file_evasion_dropper": "Thao tác Tệp & Dropper (File Dropper)",
    }
    return titles.get(key, key.replace("_", " ").title())


def _get_pipeline_action_hint(pipeline: str) -> str:
    hints = {
        "windows.packed_pe": "Tệp bị đóng gói/mã hóa. Kích hoạt cụm Memory Dumping (KsDumper / MemProcFS) hoặc De-obfuscation (angr) trước khi Decompile.",
        "windows.clean_pe": "Tệp thực thi Windows (PE) tiêu chuẩn. Sẵn sàng cho phân tích sâu bằng Ghidra hoặc IDA Pro.",
        "android.unity_il2cpp": "Tệp Unity Game. Chuyển qua Il2CppDumper MCP để khôi phục cấu trúc `global-metadata.dat` và hàm C#.",
        "android.flutter": "Tệp Flutter Dart AOT. Chuyển qua Blutter Parser để khôi phục Object Pool từ `libapp.so`.",
        "android.packed_apk": "Ứng dụng Android có dấu hiệu bọc bảo vệ (Packer). Khởi động Frida-DexDumper trên Emulator để dump DEX sạch.",
        "android.apk_static": "Ứng dụng Android tiêu chuẩn. Đẩy qua JADX CLI MCP để dịch ngược ra mã nguồn Java.",
        "android.native_or_linux_elf": "Tệp Native ARM/x64 ELF. Đẩy qua Ghidra ARM hoặc Frida Hooking.",
        "manual.review": "Tệp không rõ cấu trúc. Cần chuyên gia kiểm tra bằng Hex Editor (ImHex) và Binwalk.",
    }
    return hints.get(pipeline, "Chuyển giao cho AI Agent phân tích tự do.")
