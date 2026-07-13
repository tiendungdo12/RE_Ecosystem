def generate_markdown_report(triage_data: dict) -> str:
    md = []
    
    # 1. Header & Basic Info
    md.append(f"# 🛡️ Báo Cáo Phân Tích Tĩnh (Triage Report)")
    md.append(f"**Tệp tin:** `{triage_data.get('path', 'Unknown')}`")
    
    is_file = triage_data.get('is_file', False)
    if not is_file:
        md.append("> ❌ **Lỗi:** Tệp không tồn tại hoặc không thể truy cập.")
        return "\n".join(md)
        
    md.append(f"**Kích thước:** {triage_data.get('size', 0):,} bytes")
    md.append(f"**Định dạng (File Type):** `{triage_data.get('file_type', 'Unknown').upper()}`")
    md.append(f"**Nền tảng (Platform):** `{triage_data.get('platform', 'Unknown').capitalize()}`")
    md.append(f"**Kiến trúc:** `{triage_data.get('architecture', 'Unknown')}`")
    
    # 2. Hashes
    hashes = triage_data.get("hashes", {})
    md.append("\n## 1. Mã băm (Hashes) & Nhận diện")
    md.append(f"- **MD5:** `{hashes.get('md5', '')}`")
    md.append(f"- **SHA256:** `{hashes.get('sha256', '')}`")
    md.append(f"- **Magic Bytes:** `{triage_data.get('magic', '')}`")
    
    # 3. Authenticode
    auth = triage_data.get("authenticode")
    md.append("\n## 2. Chữ Ký Điện Tử (Authenticode)")
    if auth:
        status = auth.get("Status", "Unknown")
        subject = auth.get("Subject", "N/A")
        if status == "Valid":
            md.append(f"✅ **Hợp lệ (Valid)**")
            md.append(f"- **Chủ thể ký (Subject):** `{subject}`")
        elif status == "NotSigned":
            md.append(f"⚠️ **Không có chữ ký (NotSigned)** - File ẩn chứa rủi ro cao.")
        else:
            md.append(f"❌ **Trạng thái:** `{status}`")
            md.append(f"- **Chi tiết:** `{auth.get('Details', 'N/A')}`")
    else:
        md.append("- Không áp dụng (Không phải file PE/Windows).")
        
    # 4. Compiler & Packer (DIE)
    packer = triage_data.get("packer", "unknown")
    engine = triage_data.get("engine", "unknown")
    md.append("\n## 3. Compiler & Packer (Detect It Easy)")
    md.append(f"- **Compiler/Engine:** `{engine}`")
    md.append(f"- **Packer/Protector:** `{packer}`")
    if packer != "unknown":
        md.append("> 🚨 **CẢNH BÁO:** File có dấu hiệu bị nén (Packed) hoặc làm rối mã (Obfuscated).")
        
    # 5. Entropy & Sections
    entropy_info = triage_data.get("entropy", {})
    md.append("\n## 4. Phân Tích Entropy")
    md.append(f"- **Entropy tổng thể:** `{entropy_info.get('overall', 0)} / 8.0`")
    
    sections = entropy_info.get("sections", [])
    if sections:
        md.append("\n| Tên Section | Kích Thước | Entropy | Đánh giá |")
        md.append("|---|---|---|---|")
        for sec in sections:
            ent = sec.get("entropy", 0)
            status = "🔴 Cao (Khả nghi bị nén)" if ent >= 7.0 else "🟢 Bình thường"
            md.append(f"| `{sec.get('name', '')}` | {sec.get('raw_size', 0)} | {ent} | {status} |")
            
    # 6. Suspicious Imports
    imports_info = triage_data.get("imports_summary", {})
    suspicious = imports_info.get("suspicious", [])
    md.append("\n## 5. API Đáng Ngờ (Suspicious Imports)")
    if suspicious:
        md.append(f"Tìm thấy **{len(suspicious)}** API nằm trong Blacklist:")
        for api in suspicious:
            md.append(f"- `{api}`")
    else:
        md.append("- Không phát hiện API nguy hiểm trực tiếp.")
        
    # 7. Deep Scan (Binwalk)
    deep_scan = entropy_info.get("deep_scan")
    md.append("\n## 6. Deep Scan (Binwalk v3)")
    if deep_scan is None:
        md.append("- Đã bỏ qua (Không cần thiết cho file này).")
    elif isinstance(deep_scan, list) and deep_scan:
        md.append("Phát hiện các tệp tin/chữ ký nhúng bên trong:")
        for item in deep_scan:
            md.append(f"- `Offset: {item.get('offset', '?')} -> {item.get('description', '')}`")
    else:
        md.append("- Không phát hiện tệp nhúng.")
        
    # 8. Pipeline Routing
    pipeline = triage_data.get("recommended_pipeline", "unknown")
    md.append("\n## 7. Khuyến Nghị Hệ Thống (Routing Pipeline)")
    md.append(f"👉 **Pipeline tiếp theo:** `{pipeline}`")
    
    return "\n".join(md)
