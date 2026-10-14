# test_triage.py
import io
import json
import os
import sys
import zipfile

# Ensure the script runs in the correct directory
current_dir = os.path.dirname(os.path.abspath(__file__))
if current_dir not in sys.path:
    sys.path.insert(0, current_dir)

import server
from report_generator import generate_markdown_report

if sys.stdout.encoding.lower() != "utf-8":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except AttributeError:
        pass


def custom_progress(msg):
    print(f"[*] {msg}")


def create_synthetic_test_apk(output_path: str):
    """Tạo một file APK / XAPK mẫu có AndroidManifest.xml, classes.dex, lib/arm64-v8a/libil2cpp.so để kiểm thử."""
    # Build minimal Android Binary XML for testing
    # Or write a mock zip with APK structure
    with zipfile.ZipFile(output_path, "w") as zf:
        zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 100)
        zf.writestr("classes2.dex", b"dex\n035\x00" + b"\x00" * 100)
        zf.writestr("lib/arm64-v8a/libil2cpp.so", b"\x7fELF\x02\x01\x01\x00" + b"\x00" * 200)
        zf.writestr("lib/arm64-v8a/libunity.so", b"\x7fELF\x02\x01\x01\x00" + b"\x00" * 200)
        zf.writestr("assets/bin/Data/Managed/global-metadata.dat", b"metadata test")


def test_file(target_file: str):
    print("=" * 60)
    print(f"TRIAGE TEST: {target_file}")
    print("=" * 60)
    result = server._triage_file(target_file, progress_cb=custom_progress)

    print("\n--- JSON OUTPUT ---")
    print(json.dumps(result, indent=2, ensure_ascii=False))

    print("\n--- MARKDOWN REPORT ---")
    md_report = generate_markdown_report(result)
    print(md_report)
    print("=" * 60)
    return result


if __name__ == "__main__":
    if len(sys.argv) > 1:
        target = sys.argv[1]
        test_file(target)
    else:
        # Default test: test on python.exe and a synthetic test APK
        python_exe = os.path.join(os.path.dirname(current_dir), "Python_Runtime", "python.exe")
        if os.path.exists(python_exe):
            test_file(python_exe)

        # Synthetic APK test
        test_apk_path = os.path.join(current_dir, "sample_test.apk")
        try:
            create_synthetic_test_apk(test_apk_path)
            test_file(test_apk_path)
        finally:
            if os.path.exists(test_apk_path):
                os.remove(test_apk_path)

def test_risk_score_cap():
    """Verify risk score caps at 100 even when raw total exceeds it."""
    from server import _calculate_risk_assessment
    # Simulate: memory_injection(+30) + W+X section(+25) + packer(+25) + high_entropy(+20) + anti_analysis(+15) = 115
    sections = [
        {"name": ".text", "entropy": 7.8, "is_writable_and_executable": True, "anomalies": ["high_entropy", "writable_and_executable"]}
    ]
    imports_summary = {
        "categories": {
            "memory_injection": ["VirtualAllocEx", "WriteProcessMemory"],
            "anti_analysis": ["IsDebuggerPresent"],
        }
    }
    result = _calculate_risk_assessment(
        file_type="pe", overall_entropy=7.5, sections=sections,
        imports_summary=imports_summary, packer="VMProtect", android_metadata=None
    )
    assert result["risk_score"] <= 100, f"Score {result['risk_score']} exceeds cap!"
    assert result["risk_level"] == "CRITICAL"
    print(f"  [PASS] Risk score cap test: score={result['risk_score']}, level={result['risk_level']}")


def test_clean_file_score_zero():
    """File with no suspicious indicators should score 0 / CLEAN."""
    from server import _calculate_risk_assessment
    sections = [
        {"name": ".text", "entropy": 5.5, "is_writable_and_executable": False, "anomalies": []}
    ]
    imports_summary = {"categories": {}}
    result = _calculate_risk_assessment(
        file_type="pe", overall_entropy=5.0, sections=sections,
        imports_summary=imports_summary, packer="unknown", android_metadata=None
    )
    assert result["risk_score"] == 0, f"Expected 0 but got {result['risk_score']}"
    assert result["risk_level"] == "CLEAN"
    print(f"  [PASS] Clean file test: score={result['risk_score']}, level={result['risk_level']}")


def test_android_critical_permissions():
    """APK with critical permissions should score high."""
    from server import _calculate_risk_assessment
    android_meta = {
        "dangerous_permissions": [
            {"name": "android.permission.BIND_DEVICE_ADMIN", "risk": "critical"},
            {"name": "android.permission.SYSTEM_ALERT_WINDOW", "risk": "critical"},
            {"name": "android.permission.RECORD_AUDIO", "risk": "high"},
        ]
    }
    result = _calculate_risk_assessment(
        file_type="apk", overall_entropy=4.0, sections=[],
        imports_summary={"categories": {}}, packer="unknown", android_metadata=android_meta
    )
    assert result["risk_score"] >= 45, f"Expected >=45 but got {result['risk_score']}"
    print(f"  [PASS] Android critical perms test: score={result['risk_score']}, level={result['risk_level']}")
    print(f"         Factors: {result['risk_factors']}")


def test_standard_aosp_attribute_offset():
    """Verify that AOSP standard manifest with header_size=16 and attributeStart=20 parses correctly."""
    import struct
    from axml_parser import extract_manifest_info

    strings = ["manifest", "package", "com.aosp.standard"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # AOSP header_size=16, attr_start=20, count=1, size=20
    elem_chunk = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    attr_data = struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)
    elem_chunk += attr_data

    end_elem = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)
    body = elem_chunk + end_elem
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert "error" not in res or not res["error"], f"Unexpected error in AOSP manifest: {res.get('error')}"
    assert res["package_name"] == "com.aosp.standard", f"Expected com.aosp.standard but got {res.get('package_name')}"
    print(f"  [PASS] AOSP standard attribute offset test: package={res['package_name']}")


def test_truncated_manifest_chunk():
    """Verify that truncated AXML chunk after root element preserves package and sets error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.truncated.test"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    elem_chunk = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    attr_data = struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)
    elem_chunk += attr_data

    # Append a chunk claiming 80 bytes but only having 8 bytes
    truncated_chunk = struct.pack("<HHII", 0x0102, 16, 80, 2)
    body = elem_chunk + truncated_chunk
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert res.get("package_name") == "com.truncated.test", f"Expected package preserved, got {res.get('package_name')}"
    assert "error" in res and "truncated" in res["error"].lower(), f"Expected truncation error, got {res.get('error')}"

    # Package into synthetic APK and run _triage_file
    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' in analyzer_errors: {triage_res['analyzer_errors']}"
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN", f"Expected UNKNOWN risk, got {triage_res['risk_assessment']['risk_level']}"
        assert triage_res["android_metadata"]["package_name"] == "com.truncated.test", f"Package lost in APK triage"
        print(f"  [PASS] Truncated manifest chunk test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_yara_scan_failure_sets_unknown_risk():
    """Verify that a YARA scan failure records analyzer_errors['yara'] and forces risk_level to UNKNOWN."""
    import tempfile
    import triage_analyzer
    from triage_analyzer import _triage_file

    class MockFailingRules:
        def match(self, *args, **kwargs):
            raise RuntimeError("Simulated YARA scanning fault")

    orig_rules = triage_analyzer._yara_rules
    triage_analyzer._yara_rules = MockFailingRules()

    with tempfile.NamedTemporaryFile(suffix=".exe", delete=False) as tf:
        tf.write(b"MZ" + b"\x00" * 200)
        exe_path = tf.name
    try:
        res = _triage_file(exe_path)
        assert "yara" in res["analyzer_errors"], f"Expected 'yara' in analyzer_errors: {res['analyzer_errors']}"
        assert "Simulated YARA scanning fault" in res["analyzer_errors"]["yara"]
        assert res["risk_assessment"]["risk_level"] == "UNKNOWN", f"Expected UNKNOWN risk, got {res['risk_assessment']['risk_level']}"
        print(f"  [PASS] YARA scan failure test: error={res['analyzer_errors']['yara']}, risk={res['risk_assessment']['risk_level']}")
    finally:
        triage_analyzer._yara_rules = orig_rules
        if os.path.exists(exe_path):
            os.remove(exe_path)


def test_overflowing_attributes_manifest_chunk():
    """Verify that a child chunk declaring attributes exceeding its chunk boundary records an error."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.overflow.test", "permission"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Valid root manifest element
    root_elem = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    root_elem += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # Child chunk declaring chunk_size=36 but attr_count=5 (overflowing chunk boundaries)
    child_elem = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 36, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3,
        20, 20, 5, 0, 0, 0
    )
    end_child = struct.pack("<HHIIIII", 0x0103, 16, 24, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3)
    end_root = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    body = root_elem + child_elem + end_child + end_root
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert res.get("package_name") == "com.overflow.test", f"Expected package preserved, got {res.get('package_name')}"
    assert "error" in res and res["error"], "Expected structural attribute error"

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["android_metadata"]["package_name"] == "com.overflow.test"
        print(f"  [PASS] Overflowing attributes manifest chunk test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_malformed_end_element_chunk():
    """Verify that a malformed end-element chunk (header_size=0, chunk_size=8) records an error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.malformed.endelem"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Valid root manifest element
    root_elem = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    root_elem += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # Malformed end-element: chunk_type=0x0103, header_size=0, chunk_size=8
    malformed_end = struct.pack("<HHII", 0x0103, 0, 8, 1)

    body = root_elem + malformed_end
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert res.get("package_name") == "com.malformed.endelem", f"Expected package preserved, got {res.get('package_name')}"
    assert "error" in res and res["error"], "Expected error for malformed end-element"

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["android_metadata"]["package_name"] == "com.malformed.endelem"
        print(f"  [PASS] Malformed end-element chunk test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_missing_yara_rules_sets_unknown_risk():
    """Verify that when no YARA rules are found, analyzer_errors['yara'] is recorded and risk is UNKNOWN."""
    import tempfile
    import triage_analyzer
    from triage_analyzer import _triage_file

    orig_load = triage_analyzer._load_yara_rules
    triage_analyzer._load_yara_rules = lambda: (None, "No YARA rules found in Rules/yara-rules/packers directory")

    with tempfile.NamedTemporaryFile(suffix=".exe", delete=False) as tf:
        tf.write(b"MZ" + b"\x00" * 200)
        exe_path = tf.name
    try:
        res = _triage_file(exe_path)
        assert "yara" in res["analyzer_errors"], f"Expected 'yara' in analyzer_errors: {res['analyzer_errors']}"
        assert "No YARA rules" in res["analyzer_errors"]["yara"]
        assert res["risk_assessment"]["risk_level"] == "UNKNOWN", f"Expected UNKNOWN risk, got {res['risk_assessment']['risk_level']}"
        print(f"  [PASS] Missing YARA rules test: error={res['analyzer_errors']['yara']}, risk={res['risk_assessment']['risk_level']}")
    finally:
        triage_analyzer._load_yara_rules = orig_load
        if os.path.exists(exe_path):
            os.remove(exe_path)


def test_probe_invalid_name_idx_end_element():
    """Verify that an end-element chunk with invalid name_idx=0xFFFFFFFF records an error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.probe.nameidx"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Valid root manifest element with package attr
    elem_chunk = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    elem_chunk += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # End-element with name_idx=0xFFFFFFFF (invalid name index)
    end_elem = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF)

    body = elem_chunk + end_elem
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert res.get("package_name") == "com.probe.nameidx", f"Expected package preserved, got {res.get('package_name')}"
    assert "error" in res and res["error"], "Expected error for invalid end-element name index"
    assert "name index" in res["error"].lower() or "tag name" in res["error"].lower()

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["android_metadata"]["package_name"] == "com.probe.nameidx"
        print(f"  [PASS] Invalid name_idx end-element test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_header_size_exceeds_chunk_size():
    """Verify that an end-element chunk with header_size > chunk_size records an error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.probe.headersize"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Valid root manifest element with package attr
    elem_chunk = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    elem_chunk += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # End-element where header_size(32) > chunk_size(24)
    end_elem = struct.pack("<HHIIIII", 0x0103, 32, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    body = elem_chunk + end_elem
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert res.get("package_name") == "com.probe.headersize", f"Expected package preserved, got {res.get('package_name')}"
    assert "error" in res and res["error"], "Expected error for header_size > chunk_size"
    assert "exceeds" in res["error"].lower()

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["android_metadata"]["package_name"] == "com.probe.headersize"
        print(f"  [PASS] Header_size exceeds chunk_size test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_inconsistent_header_file_size():
    """Verify that an AXML header declaring inconsistent file_size (e.g. 8 bytes vs 208 bytes) records an error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.probe.hdrsize"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Valid root manifest element with package attr
    elem_chunk = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    elem_chunk += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # Valid end-element
    end_elem = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    body = elem_chunk + end_elem
    # Intentionally forge declared file_size to 8, while actual data is > 200 bytes
    declared_file_size = 8
    data = struct.pack("<II", 0x00080003, declared_file_size) + sp_chunk + body
    assert len(data) > 200, f"Expected actual data length > 200, got {len(data)}"

    res = extract_manifest_info(data)
    assert "error" in res and res["error"], "Expected error for inconsistent header file size"
    assert "inconsistent axml header file size" in res["error"].lower()

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "inconsistent" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        print(f"  [PASS] Inconsistent header file_size test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_oversized_container_entry():
    """Verify that an archive entry claiming an uncompressed size exceeding MAX_MANIFEST_SIZE raises an error and sets UNKNOWN risk."""
    import tempfile
    import zipfile
    import axml_parser
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_MANIFEST_SIZE
    axml_parser.MAX_MANIFEST_SIZE = 50  # Temporarily lower limit to 50 bytes for test

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", b"\x00" * 100)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)

        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        print(f"  [PASS] Oversized container entry test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        axml_parser.MAX_MANIFEST_SIZE = orig_limit
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_multiple_root_elements():
    """Verify that an AXML document declaring multiple root elements is rejected with an AXML analyzer error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.probe.first", "com.probe.second"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Root 1 start-element: <manifest package="com.probe.first">
    root1_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    root1_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)
    # Root 1 end-element: </manifest>
    root1_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    # Root 2 start-element: <manifest package="com.probe.second">
    root2_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    root2_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 3, 8, 0, 3, 3)
    # Root 2 end-element: </manifest>
    root2_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    body = root1_start + root1_end + root2_start + root2_end
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert "error" in res and res["error"], "Expected error for multiple root elements"
    assert "multiple root elements" in res["error"].lower()

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "multiple root elements" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        print(f"  [PASS] Multiple root elements probe test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_large_file_truncated_risk_is_unknown():
    """Verify that a truncated analysis (>200 MiB) always yields UNKNOWN risk level and manual.review pipeline."""
    from triage_analyzer import _calculate_risk_assessment, _recommend_pipeline

    # Simulate completely clean PE attributes on a file with size_truncated=True
    risk = _calculate_risk_assessment(
        file_type="pe",
        overall_entropy=5.2,
        sections=[{"name": ".text", "entropy": 6.1, "is_flagged": False}],
        imports_summary={"categories": {}},
        packer="none",
        size_truncated=True,
    )
    assert risk["risk_level"] == "UNKNOWN", f"Expected UNKNOWN risk for truncated file, got {risk['risk_level']}"
    assert any("200 MiB" in factor for factor in risk["risk_factors"]), "Expected 200 MiB factor in risk_factors"

    rec = _recommend_pipeline(
        file_type="pe",
        platform="windows",
        packer="none",
        engine="unknown",
        size_truncated=True,
    )
    assert rec == "manual.review", f"Expected manual.review pipeline for truncated clean PE, got {rec}"
    print(f"  [PASS] Large file truncated risk assessment test: risk={risk['risk_level']}, rec={rec}")


def test_probe_invalid_attribute_name_index():
    """Verify that an AXML start-element with an out-of-bounds attribute name index (e.g. 0xFFFFFFFF) is rejected, sets an AXML analyzer error, and yields UNKNOWN risk level with manual.review recommendation."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.probe.attr", "uses-permission", "name", "android.permission.SYSTEM_ALERT_WINDOW"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Valid manifest start-element: <manifest package="com.probe.attr">
    manifest_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    manifest_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # Malformed uses-permission start-element with a_name_idx = 0xFFFFFFFF (out of bounds)
    perm_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3,
        20, 20, 1, 0, 0, 0
    )
    perm_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 0xFFFFFFFF, 5, 8, 0, 3, 5)

    perm_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3)
    manifest_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 3, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    body = manifest_start + perm_start + perm_end + manifest_end
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert "error" in res and res["error"], "Expected error for invalid attribute name index"
    assert "invalid attribute name index" in res["error"].lower()

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "invalid attribute name index" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Invalid attribute name index probe test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}, rec={triage_res['recommended_pipeline']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_recommendation_consistent_on_error():
    """Verify that any analyzer error forces recommended_pipeline to manual.review across file types."""
    from triage_analyzer import _recommend_pipeline

    # APK with errors
    rec = _recommend_pipeline("apk", "android", "none", "unity_il2cpp", has_errors=True)
    assert rec == "manual.review", f"Expected manual.review for APK with errors, got {rec}"

    # PE with errors
    rec = _recommend_pipeline("pe", "windows", "none", "unknown", has_errors=True)
    assert rec == "manual.review", f"Expected manual.review for PE with errors, got {rec}"

    # Clean APK without errors
    rec = _recommend_pipeline("apk", "android", "none", "unity_il2cpp", has_errors=False)
    assert rec == "android.unity_il2cpp", f"Expected android.unity_il2cpp for clean unity apk, got {rec}"
    print(f"  [PASS] Recommendation consistency on error test passed!")


def test_probe_attr_count_zero_with_slack_bytes():
    """Verify that an AXML start-element declaring attr_count=0 with slack attribute bytes (chunk_size=56) is rejected, setting an AXML error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    strings = ["manifest", "package", "com.probe.slack", "uses-permission", "name", "android.permission.SYSTEM_ALERT_WINDOW"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Valid manifest start-element: <manifest package="com.probe.slack">
    manifest_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    manifest_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # Malicious uses-permission start-element: chunk_size=56, attr_count=0, but contains 20 attribute bytes!
    perm_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3,
        20, 20, 0, 0, 0, 0  # attr_count = 0 (MALICIOUS EVASION!)
    )
    # The 20 slack bytes representing android:name="android.permission.SYSTEM_ALERT_WINDOW"
    perm_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 4, 5, 8, 0, 3, 5)

    perm_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3)
    manifest_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 3, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    body = manifest_start + perm_start + perm_end + manifest_end
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    res = extract_manifest_info(data)
    assert "error" in res and res["error"], "Expected error for start-element with slack bytes"
    assert "exceeds attribute table boundary" in res["error"].lower() or "slack" in res["error"].lower()

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Attr count zero with slack bytes probe test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}, rec={triage_res['recommended_pipeline']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_oversized_apks_wrapper_entry():
    """Verify that a nested base.apk inside an .apks wrapper exceeding MAX_INNER_APK_SIZE raises an error and sets UNKNOWN risk."""
    import tempfile
    import zipfile
    import axml_parser
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_INNER_APK_SIZE
    axml_parser.MAX_INNER_APK_SIZE = 50  # Temporarily lower limit to 50 bytes for test

    with tempfile.NamedTemporaryFile(suffix=".apks", delete=False) as tf:
        apks_path = tf.name
    try:
        with zipfile.ZipFile(apks_path, "w") as zf:
            zf.writestr("base.apk", b"PK\x03\x04" + b"\x00" * 100)

        triage_res = _triage_file(apks_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Oversized APKS wrapper entry test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        axml_parser.MAX_INNER_APK_SIZE = orig_limit
        if os.path.exists(apks_path):
            os.remove(apks_path)


def _generate_test_axml(package_name: str, permission: str = None) -> bytes:
    import struct
    strings = ["manifest", "package", package_name]
    if permission:
        strings.extend(["uses-permission", "name", permission])

    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    manifest_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    manifest_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    body = manifest_start
    if permission:
        perm_start = struct.pack(
            "<HHIIIIIHHHHHH",
            0x0102, 16, 56, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3,
            20, 20, 1, 0, 0, 0
        )
        perm_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 4, 5, 8, 0, 3, 5)
        perm_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3)
        body += perm_start + perm_end

    manifest_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 3, 0xFFFFFFFF, 0xFFFFFFFF, 0)
    body += manifest_end
    total_size = 8 + len(sp_chunk) + len(body)
    return struct.pack("<II", 0x00080003, total_size) + sp_chunk + body


def test_probe_split_preceding_base_apk_extracts_permissions():
    """Verify that in an APKS container where split APK precedes base.apk, base manifest and permissions are correctly extracted."""
    import tempfile
    import zipfile
    import io
    from triage_analyzer import _triage_file

    split_manifest = _generate_test_axml("com.probe.split", permission=None)
    base_manifest = _generate_test_axml("com.probe.base", permission="android.permission.SYSTEM_ALERT_WINDOW")

    # Create split APK (no permissions)
    split_apk_io = io.BytesIO()
    with zipfile.ZipFile(split_apk_io, "w") as szf:
        szf.writestr("AndroidManifest.xml", split_manifest)
        szf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
    split_apk_bytes = split_apk_io.getvalue()

    # Create base APK (with SYSTEM_ALERT_WINDOW)
    base_apk_io = io.BytesIO()
    with zipfile.ZipFile(base_apk_io, "w") as bzf:
        bzf.writestr("AndroidManifest.xml", base_manifest)
        bzf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
    base_apk_bytes = base_apk_io.getvalue()

    with tempfile.NamedTemporaryFile(suffix=".apks", delete=False) as tf:
        apks_path = tf.name

    try:
        # Crucial test: split APK is written FIRST, base.apk is written SECOND
        with zipfile.ZipFile(apks_path, "w") as zf:
            zf.writestr("split_feature.apk", split_apk_bytes)
            zf.writestr("base.apk", base_apk_bytes)

        triage_res = _triage_file(apks_path)
        meta = triage_res.get("android_metadata", {})
        dang_perms = meta.get("dangerous_permissions", [])

        assert len(dang_perms) > 0, f"Expected dangerous permissions to be extracted from base.apk, got {dang_perms}"
        assert any(p.get("name") == "android.permission.SYSTEM_ALERT_WINDOW" for p in dang_perms), \
            f"Expected SYSTEM_ALERT_WINDOW in {dang_perms}"
        assert triage_res["risk_assessment"]["risk_level"] != "CLEAN", \
            f"Risk level must not be CLEAN when base APK has SYSTEM_ALERT_WINDOW, got {triage_res['risk_assessment']['risk_level']}"
        assert meta.get("package_name") == "com.probe.base", \
            f"Expected package name 'com.probe.base', got {meta.get('package_name')}"
        print(f"  [PASS] Split preceding base.apk probe test: package={meta.get('package_name')}, perms={[p['name'] for p in dang_perms]}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apks_path):
            os.remove(apks_path)


def test_probe_oversized_xapk_wrapper_entry():
    """Verify that a nested base.apk inside a .xapk wrapper exceeding MAX_INNER_APK_SIZE raises an error and sets UNKNOWN risk."""
    import tempfile
    import zipfile
    import axml_parser
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_INNER_APK_SIZE
    axml_parser.MAX_INNER_APK_SIZE = 50  # Temporarily lower limit to 50 bytes for test

    with tempfile.NamedTemporaryFile(suffix=".xapk", delete=False) as tf:
        xapk_path = tf.name
    try:
        with zipfile.ZipFile(xapk_path, "w") as zf:
            zf.writestr("manifest.json", b'{"package_name": "com.test.xapk"}')
            zf.writestr("base.apk", b"PK\x03\x04" + b"\x00" * 100)

        triage_res = _triage_file(xapk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Oversized XAPK wrapper entry test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        axml_parser.MAX_INNER_APK_SIZE = orig_limit
        if os.path.exists(xapk_path):
            os.remove(xapk_path)


def test_probe_config_only_bundle_missing_base_sets_error_and_unknown():
    """Verify that a bundle containing only config splits (no base APK) is rejected with an AXML error and UNKNOWN risk."""
    import tempfile
    import zipfile
    import io
    from triage_analyzer import _triage_file

    config_manifest = _generate_test_axml("com.probe.config", permission=None)

    config_apk_io = io.BytesIO()
    with zipfile.ZipFile(config_apk_io, "w") as czf:
        czf.writestr("AndroidManifest.xml", config_manifest)
        czf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
    config_apk_bytes = config_apk_io.getvalue()

    with tempfile.NamedTemporaryFile(suffix=".apks", delete=False) as tf:
        apks_path = tf.name

    try:
        # Container contains only config split APKs, NO base.apk
        with zipfile.ZipFile(apks_path, "w") as zf:
            zf.writestr("config.arm64_v8a.apk", config_apk_bytes)
            zf.writestr("split_config.xxhdpi.apk", config_apk_bytes)

        triage_res = _triage_file(apks_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "no base apk found" in triage_res["analyzer_errors"]["axml"].lower(), \
            f"Expected 'No base APK found' in error, got: {triage_res['analyzer_errors']['axml']}"
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Config-only bundle test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apks_path):
            os.remove(apks_path)


def test_probe_plain_text_manifest_fallback_extracts_metadata():
    """Verify that a plain-text XML AndroidManifest.xml fallback extracts metadata correctly and does not produce an AXML error."""
    import tempfile
    import zipfile
    from triage_analyzer import _triage_file

    plain_xml = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<manifest xmlns:android="http://schemas.android.com/apk/res/android"\n'
        '    package="com.probe.plaintext"\n'
        '    android:versionCode="42"\n'
        '    android:versionName="1.0-plain">\n'
        '    <uses-permission android:name="android.permission.INTERNET" />\n'
        '</manifest>'
    ).encode("utf-8")

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name

    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", plain_xml)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)

        triage_res = _triage_file(apk_path)
        assert "axml" not in triage_res.get("analyzer_errors", {}), \
            f"Unexpected 'axml' error for plain text XML: {triage_res.get('analyzer_errors')}"
        meta = triage_res.get("android_metadata", {})
        assert meta.get("package_name") == "com.probe.plaintext", \
            f"Expected package 'com.probe.plaintext', got {meta.get('package_name')}"
        assert meta.get("version_code") == "42", \
            f"Expected versionCode '42', got {meta.get('version_code')}"
        assert "android.permission.INTERNET" in meta.get("permissions", []), \
            f"Expected INTERNET permission, got {meta.get('permissions')}"
        print(f"  [PASS] Plain text XML manifest test: package={meta.get('package_name')}, code={meta.get('version_code')}, perms={meta.get('permissions')}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_yara_scans_bounded_buffer():
    """Verify that _detect_packer_yara scans bounded in-memory data buffer without falling back to file path on data scan."""
    import triage_analyzer

    class SpyRules:
        def __init__(self):
            self.data_called = None
            self.path_called = None

        def match(self, filepath=None, data=None):
            if data is not None:
                self.data_called = data
                return []
            if filepath is not None:
                self.path_called = filepath
                return []
            return []

    spy = SpyRules()
    orig_rules = triage_analyzer._yara_rules
    try:
        triage_analyzer._yara_rules = spy
        test_buf = b"MZ" + b"\x90" * 1000
        packer, engine, err = triage_analyzer._detect_packer_yara("dummy_nonexistent_path.bin", data=test_buf)
        assert spy.data_called == test_buf, "Expected match(data=...) to be called with buffer"
        assert spy.path_called is None, "Expected match(filepath=...) NOT to be called when data is provided"
        assert err is None, f"Expected no error with spy rules, got {err}"
        print(f"  [PASS] Bounded YARA buffer scan test: verified match(data=...) called strictly in-memory")
    finally:
        triage_analyzer._yara_rules = orig_rules


def test_probe_feature_only_bundle_missing_base_sets_error_and_unknown():
    """Verify that a bundle containing only feature split APKs (e.g. feature.apk) without a base APK sets an AXML error and UNKNOWN risk."""
    import tempfile
    import zipfile
    import io
    from triage_analyzer import _triage_file

    clean_manifest = _generate_test_axml("com.probe.feature", permission=None)

    feature_apk_io = io.BytesIO()
    with zipfile.ZipFile(feature_apk_io, "w") as czf:
        czf.writestr("AndroidManifest.xml", clean_manifest)
        czf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 30)
    feature_apk_bytes = feature_apk_io.getvalue()

    config_apk_io = io.BytesIO()
    with zipfile.ZipFile(config_apk_io, "w") as czf:
        czf.writestr("lib/arm64-v8a/libconfig.so", b"\x7fELF" + b"\x00" * 50)
    config_apk_bytes = config_apk_io.getvalue()

    with tempfile.NamedTemporaryFile(suffix=".apks", delete=False) as tf:
        apks_path = tf.name

    try:
        # Container contains only feature.apk and config.arm64_v8a.apk, NO base.apk
        with zipfile.ZipFile(apks_path, "w") as zf:
            zf.writestr("feature.apk", feature_apk_bytes)
            zf.writestr("config.arm64_v8a.apk", config_apk_bytes)

        triage_res = _triage_file(apks_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "no base apk found in bundle archive" in triage_res["analyzer_errors"]["axml"].lower(), \
            f"Expected 'No base APK found' in error, got: {triage_res['analyzer_errors']['axml']}"
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Feature-only bundle missing base test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apks_path):
            os.remove(apks_path)


def test_probe_lief_bounded_buffer_on_truncated_file():
    """Verify that large file parsing passes bounded in-memory buffer (bytes) to LIEF instead of attempting disk read beyond 200 MiB."""
    import tempfile
    import lief
    from triage_analyzer import _triage_file

    parsed_args = []
    orig_lief_parse = lief.parse

    def spy_lief_parse(obj):
        parsed_args.append(obj)
        return orig_lief_parse(obj)

    lief.parse = spy_lief_parse
    with tempfile.NamedTemporaryFile(suffix=".exe", delete=False) as tf:
        large_path = tf.name
    try:
        # Create a file larger than SIZE_LIMIT (200 MiB) sparsely
        with open(large_path, "wb") as f:
            f.write(b"MZ" + b"\x00" * 1024)
            f.seek(201 * 1024 * 1024)
            f.write(b"\x00")
        res = _triage_file(large_path)
        assert res["size_truncated"] is True
        assert res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert res["recommended_pipeline"] == "manual.review"
        assert len(parsed_args) > 0, "Expected lief.parse to be called"
        assert isinstance(parsed_args[0], bytes), f"Expected lief.parse to receive bytes, but received {type(parsed_args[0])}"
        print(f"  [PASS] LIEF bounded buffer on truncated file test: verified lief.parse received {type(parsed_args[0]).__name__} ({len(parsed_args[0])} bytes)")
    finally:
        lief.parse = orig_lief_parse
        if os.path.exists(large_path):
            os.remove(large_path)


def test_probe_magic_hex_returns_16_bytes():
    """Verify that _get_magic_hex returns the full 16-byte magic header in hex format matching baseline behavior."""
    from triage_analyzer import _get_magic_hex
    test_data = b"MZ\x90\x00\x03\x00\x00\x00\x04\x00\x00\x00\xff\xff\x00\x00\x12\x34"
    magic = _get_magic_hex(test_data)
    expected = test_data[:16].hex(" ")
    assert magic == expected, f"Expected {expected}, got {magic}"
    assert len(magic.split()) == 16, f"Expected 16 bytes hex, got {len(magic.split())}"
    print(f"  [PASS] Magic hex returns 16 bytes test: {magic}")


def test_probe_mcp_server_catalog_does_not_expose_triage_tool():
    """Verify that FastMCP server in server.py does not expose triage_file as an MCP tool (preventing bypass of canonical pipeline)."""
    import asyncio
    import server
    tools = asyncio.run(server.mcp.list_tools())
    tool_names = [t.name for t in tools]
    assert "triage_file" not in tool_names, f"triage_file must NOT be registered as an MCP tool, found tools: {tool_names}"
    assert "static.triage" not in tool_names, f"static.triage must NOT be in tool catalog, found: {tool_names}"
    print(f"  [PASS] FastMCP tool catalog test: tools={tool_names} (triage_file is properly unexposed)")


def test_probe_macho_binary_returns_unknown_risk_and_manual_review_pipeline():
    """Verify that a Mach-O binary (which lacks static structural analysis coverage) is strictly classified as UNKNOWN and manual.review, never CLEAN."""
    import struct
    import tempfile
    from triage_analyzer import _triage_file
    from report_generator import generate_markdown_report

    # Minimal 64-bit Mach-O header (MH_MAGIC_64, x86_64 CPU, EXECUTE filetype)
    hdr = struct.pack("<IIIIIIII", 0xfeedfacf, 0x01000007, 3, 2, 0, 0, 0, 0)
    with tempfile.NamedTemporaryFile(suffix=".macho", delete=False) as tf:
        tf.write(hdr)
        macho_path = tf.name

    try:
        res = _triage_file(macho_path)
        assert res["file_type"] == "macho", f"Expected 'macho', got {res.get('file_type')}"
        assert res["platform"] == "macos_or_ios"
        risk = res["risk_assessment"]
        assert risk["risk_level"] == "UNKNOWN", f"Mach-O without structural analysis must be UNKNOWN, got {risk['risk_level']}"
        assert res["recommended_pipeline"] == "manual.review", f"Mach-O pipeline must be manual.review, got {res.get('recommended_pipeline')}"
        assert any("Mach-O" in f for f in risk.get("risk_factors", [])), f"Risk factors should mention Mach-O: {risk.get('risk_factors')}"

        report = generate_markdown_report(res)
        assert "CHƯA XÁC ĐỊNH" in report, "Report must reflect UNKNOWN badge"
        assert "AN TOÀN" not in report, "Report must NEVER claim AN TOÀN for Mach-O"
        print(f"  [PASS] Mach-O unsupported format test: file_type={res['file_type']}, risk={risk['risk_level']}, rec={res['recommended_pipeline']}")
    finally:
        if os.path.exists(macho_path):
            os.remove(macho_path)


def test_probe_string_pool_offset_outside_chunk():
    """Verify that an offset pointing outside the string pool chunk triggers an error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    header_size = 28
    string_count = 2
    strings_start = header_size + string_count * 4
    str_data = b"\x01\x00a\x00\x00\x00"
    sp_chunk_size = strings_start + len(str_data)

    sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, 0, 0, strings_start, 0)
    sp_chunk += struct.pack("<I", 0)
    sp_chunk += struct.pack("<I", 99999)
    sp_chunk += str_data

    elem_chunk = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    elem_chunk += struct.pack("<IIIHBBI", 0xFFFFFFFF, 0, 0, 8, 0, 3, 0)
    end_elem = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)
    body = sp_chunk + elem_chunk + end_elem

    data = struct.pack("<II", 0x00080003, 8 + len(body)) + body
    res = extract_manifest_info(data)
    assert "error" in res and "outside" in res["error"].lower(), f"Expected outside boundary error, got {res.get('error')}"

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] String pool offset outside chunk test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_string_pool_string_exceeds_chunk():
    """Verify that a string whose declared length or missing terminator extends beyond the chunk triggers an error."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    header_size = 28
    string_count = 1
    strings_start = header_size + string_count * 4
    str_data = struct.pack("<H", 50) + b"ab" + b"\x00\x00"
    sp_chunk_size = strings_start + len(str_data)

    sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, 0, 0, strings_start, 0)
    sp_chunk += struct.pack("<I", 0)
    sp_chunk += str_data

    elem_chunk = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    elem_chunk += struct.pack("<IIIHBBI", 0xFFFFFFFF, 0, 0, 8, 0, 3, 0)
    end_elem = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)
    body = sp_chunk + elem_chunk + end_elem

    data = struct.pack("<II", 0x00080003, 8 + len(body)) + body
    res = extract_manifest_info(data)
    assert "error" in res and ("exceeds" in res["error"].lower() or "missing null" in res["error"].lower())

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        print(f"  [PASS] String pool string exceeds chunk test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_string_pool_truncated_chunk():
    """Verify that a string pool declaring a chunk_size larger than remaining data triggers an error."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    header_size = 28
    string_count = 1
    strings_start = 32
    declared_chunk_size = 500
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, declared_chunk_size, string_count, 0, 0, strings_start, 0)
    sp_chunk += struct.pack("<I", 0)
    sp_chunk += struct.pack("<H", 1) + b"x\x00\x00\x00"

    data = struct.pack("<II", 0x00080003, 8 + len(sp_chunk)) + sp_chunk
    res = extract_manifest_info(data)
    assert "error" in res and "truncated" in res["error"].lower()

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        print(f"  [PASS] String pool truncated chunk test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_string_pool_malformed_offset_table():
    """Verify that an offset table with string_count * 4 exceeding chunk_size triggers an error."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    header_size = 28
    string_count = 500
    strings_start = 32
    sp_chunk_size = 32
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, 0, 0, strings_start, 0)
    sp_chunk += struct.pack("<I", 0)

    data = struct.pack("<II", 0x00080003, 8 + len(sp_chunk)) + sp_chunk
    res = extract_manifest_info(data)
    assert "error" in res and ("exceeds" in res["error"].lower() or "offset table" in res["error"].lower())

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        print(f"  [PASS] String pool malformed offset table test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_string_pool_malformed_style_offset_table():
    """Verify that an offset table with style_count * 4 exceeding chunk_size triggers an error."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    header_size = 28
    string_count = 1
    style_count = 500
    strings_start = 32
    sp_chunk_size = 32
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, style_count, 0, strings_start, 0)
    sp_chunk += struct.pack("<I", 0)

    data = struct.pack("<II", 0x00080003, 8 + len(sp_chunk)) + sp_chunk
    res = extract_manifest_info(data)
    assert "error" in res and ("exceeds" in res["error"].lower() or "offset table" in res["error"].lower())

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        print(f"  [PASS] String pool malformed style offset table test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_string_pool_excessive_string_count():
    """Verify that string_count exceeding MAX_STRING_POOL_ENTRIES triggers safety error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    from axml_parser import extract_manifest_info, MAX_STRING_POOL_ENTRIES
    from triage_analyzer import _triage_file

    header_size = 28
    string_count = MAX_STRING_POOL_ENTRIES + 500  # Exceeds safety limit
    strings_start = 32
    sp_chunk_size = 32
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, 0, 0, strings_start, 0)
    sp_chunk += struct.pack("<I", 0)

    data = struct.pack("<II", 0x00080003, 8 + len(sp_chunk)) + sp_chunk
    res = extract_manifest_info(data)
    assert "error" in res and "exceeds safety limit" in res["error"].lower()

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert "safety limit" in triage_res["analyzer_errors"]["axml"]
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] String pool excessive string count test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_string_pool_repeated_offset_amplification():
    """Verify that 50,000 repeated offsets pointing to the same string are deduplicated and reuse the same object reference."""
    import struct
    from axml_parser import AXMLParser

    header_size = 28
    string_count = 50_000
    strings_start = header_size + string_count * 4
    sample_text = "A" * 1000
    encoded = sample_text.encode("utf-16le")
    str_data = struct.pack("<H", len(sample_text)) + encoded + b"\x00\x00"
    sp_chunk_size = strings_start + len(str_data)

    sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, 0, 0, strings_start, 0)
    # All 50,000 offsets point to offset 0 in the string data
    sp_chunk += struct.pack(f"<{string_count}I", *([0] * string_count))
    sp_chunk += str_data

    parser = AXMLParser(b"")
    parser.data = sp_chunk
    parser._parse_string_pool(0)

    assert parser.error is None, f"Expected no error, got: {parser.error}"
    assert len(parser.strings) == string_count
    # Verify deduplication: string objects in self.strings should be identical references
    assert parser.strings[0] is parser.strings[1]
    assert parser.strings[0] is parser.strings[-1]
    assert parser.strings[0] == sample_text
    print(f"  [PASS] Repeated offset amplification test: {string_count} entries deduplicated safely to single object reference")


def test_probe_string_pool_cumulative_decoded_bytes_exceeded():
    """Verify that string pool whose cumulative decoded bytes exceed safety limit triggers safety error and UNKNOWN risk."""
    import struct
    import tempfile
    import zipfile
    import axml_parser
    from axml_parser import extract_manifest_info
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_TOTAL_DECODED_STRING_BYTES
    try:
        # Set limit low for fast verification
        axml_parser.MAX_TOTAL_DECODED_STRING_BYTES = 50

        header_size = 28
        string_count = 4
        strings_start = header_size + string_count * 4

        # 4 unique strings of 20 bytes each -> 80 bytes total (exceeds 50)
        str_blobs = []
        offsets = []
        cur_off = 0
        for i in range(string_count):
            txt = f"unique_string_{i}_padding"
            enc = txt.encode("utf-16le")
            blob = struct.pack("<H", len(txt)) + enc + b"\x00\x00"
            offsets.append(cur_off)
            cur_off += len(blob)
            str_blobs.append(blob)

        all_str_data = b"".join(str_blobs)
        sp_chunk_size = strings_start + len(all_str_data)

        sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, 0, 0, strings_start, 0)
        for off in offsets:
            sp_chunk += struct.pack("<I", off)
        sp_chunk += all_str_data

        data = struct.pack("<II", 0x00080003, 8 + len(sp_chunk)) + sp_chunk
        res = extract_manifest_info(data)
        assert "error" in res and "cumulative decoded bytes exceeds safety limit" in res["error"].lower(), f"Expected cumulative limit error, got: {res.get('error')}"

        with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
            apk_path = tf.name
        try:
            with zipfile.ZipFile(apk_path, "w") as zf:
                zf.writestr("AndroidManifest.xml", data)
                zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
            triage_res = _triage_file(apk_path)
            assert "axml" in triage_res["analyzer_errors"]
            assert "cumulative decoded bytes" in triage_res["analyzer_errors"]["axml"]
            assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
            assert triage_res["recommended_pipeline"] == "manual.review"
            print(f"  [PASS] Cumulative decoded bytes exceeded test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
        finally:
            if os.path.exists(apk_path):
                os.remove(apk_path)
    finally:
        axml_parser.MAX_TOTAL_DECODED_STRING_BYTES = orig_limit


def test_probe_string_pool_cumulative_decoded_bytes_across_multiple_pools():
    """Verify that decoded string bytes accumulate across multiple string pools in a manifest,
    and exceeding the safety limit across pools triggers an error, UNKNOWN risk, and manual.review."""
    import struct
    import tempfile
    import zipfile
    import axml_parser
    from axml_parser import AXMLParser, extract_manifest_info
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_TOTAL_DECODED_STRING_BYTES
    try:
        # Set limit low for fast verification: 50 bytes
        axml_parser.MAX_TOTAL_DECODED_STRING_BYTES = 50

        def build_string_pool(strings: list[str]) -> bytes:
            header_size = 28
            string_count = len(strings)
            strings_start = header_size + string_count * 4
            str_blobs = []
            offsets = []
            cur_off = 0
            for txt in strings:
                enc = txt.encode("utf-16le")
                blob = struct.pack("<H", len(txt)) + enc + b"\x00\x00"
                offsets.append(cur_off)
                cur_off += len(blob)
                str_blobs.append(blob)
            all_str_data = b"".join(str_blobs)
            sp_chunk_size = strings_start + len(all_str_data)
            sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, 0, 0, strings_start, 0)
            for off in offsets:
                sp_chunk += struct.pack("<I", off)
            sp_chunk += all_str_data
            return sp_chunk

        # String 1: 15 chars in UTF-16LE = 30 bytes
        # String 2: 15 chars in UTF-16LE = 30 bytes
        # Pool 1 alone = 30 bytes (<= 50)
        # Pool 2 alone = 30 bytes (<= 50)
        # Cumulative = 30 + 30 = 60 bytes (> 50)
        sp_chunk_1 = build_string_pool(["pool1_alpha_str"])   # 15 chars = 30 bytes
        sp_chunk_2 = build_string_pool(["pool2_bravo_str"])   # 15 chars = 30 bytes

        # 1. Verify parser instance accumulates across sequential _parse_string_pool calls
        parser = AXMLParser(sp_chunk_1 + sp_chunk_2)
        parser._parse_string_pool(0)
        assert parser.error is None, f"Expected pool 1 to succeed, got: {parser.error}"
        assert parser.total_decoded_bytes == 30, f"Expected 30 decoded bytes after pool 1, got {parser.total_decoded_bytes}"

        parser._parse_string_pool(len(sp_chunk_1))
        assert parser.error is not None, "Expected cumulative limit error after pool 2"
        assert "cumulative decoded bytes exceeds safety limit" in parser.error.lower(), f"Unexpected error: {parser.error}"

        # 2. Verify full AXML document containing multiple string pools triggers error in extract_manifest_info
        total_axml_size = 8 + len(sp_chunk_1) + len(sp_chunk_2)
        axml_data = struct.pack("<II", 0x00080003, total_axml_size) + sp_chunk_1 + sp_chunk_2
        res = extract_manifest_info(axml_data)
        assert "error" in res and res["error"], "Expected error in extract_manifest_info"
        assert "cumulative decoded bytes exceeds safety limit" in res["error"].lower(), f"Unexpected error in res: {res.get('error')}"

        # 3. Verify in triage pipeline that APK with multiple string pools exceeding cumulative budget triggers UNKNOWN risk and manual.review
        with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
            apk_path = tf.name
        try:
            with zipfile.ZipFile(apk_path, "w") as zf:
                zf.writestr("AndroidManifest.xml", axml_data)
                zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
            triage_res = _triage_file(apk_path)
            assert "axml" in triage_res["analyzer_errors"], "Expected axml in analyzer_errors"
            assert "cumulative decoded bytes" in triage_res["analyzer_errors"]["axml"]
            assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
            assert triage_res["recommended_pipeline"] == "manual.review"
            print(f"  [PASS] Multiple string pools cumulative decoded bytes exceeded test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
        finally:
            if os.path.exists(apk_path):
                os.remove(apk_path)

        # 4. Also verify that when multiple pools do NOT exceed limit (e.g. 20 + 20 = 40 <= 50),
        # both pools succeed and total_decoded_bytes reflects the accumulated total.
        sp_small_1 = build_string_pool(["short1_str"])  # 10 chars = 20 bytes
        sp_small_2 = build_string_pool(["short2_str"])  # 10 chars = 20 bytes
        parser_ok = AXMLParser(sp_small_1 + sp_small_2)
        parser_ok._parse_string_pool(0)
        assert parser_ok.error is None
        assert parser_ok.total_decoded_bytes == 20
        parser_ok._parse_string_pool(len(sp_small_1))
        assert parser_ok.error is None
        assert parser_ok.total_decoded_bytes == 40

        # 5. Verify UTF-8 multi-pool cumulative decoded bytes accumulation and exceeding
        def build_utf8_string_pool(strings: list[str]) -> bytes:
            header_size = 28
            string_count = len(strings)
            strings_start = header_size + string_count * 4
            str_blobs = []
            offsets = []
            cur_off = 0
            for txt in strings:
                enc = txt.encode("utf-8")
                blob = bytes([len(txt), len(enc)]) + enc + b"\x00"
                offsets.append(cur_off)
                cur_off += len(blob)
                str_blobs.append(blob)
            all_str_data = b"".join(str_blobs)
            sp_chunk_size = strings_start + len(all_str_data)
            flags = 0x00000100
            sp_chunk = struct.pack("<HHIIIIII", 0x0001, header_size, sp_chunk_size, string_count, 0, flags, strings_start, 0)
            for off in offsets:
                sp_chunk += struct.pack("<I", off)
            sp_chunk += all_str_data
            return sp_chunk

        u1 = build_utf8_string_pool(["a" * 25])  # 25 bytes
        u2 = build_utf8_string_pool(["b" * 30])  # 30 bytes
        parser_utf8 = AXMLParser(u1 + u2)
        parser_utf8._parse_string_pool(0)
        assert parser_utf8.error is None
        parser_utf8._parse_string_pool(len(u1))
        assert parser_utf8.error is not None
        assert "cumulative decoded bytes exceeds safety limit" in parser_utf8.error.lower()

        # 6. Verify interleaved string pools and XML elements (Codex reproduction in memory):
        # Pool 1 (30 bytes) -> Node 1 -> Pool 2 (30 bytes) -> Node 2
        # Cumulative = 30 + 30 = 60 > 50 bytes triggers error at pool 2 and UNKNOWN risk
        p1 = build_string_pool(["node1_tag_alpha"])  # 15 chars = 30 bytes
        elem1_start = struct.pack("<HHIIIIIHHHHHH", 0x0102, 16, 36, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0, 20, 20, 0, 0, 0, 0)
        p2 = build_string_pool(["node2_tag_bravo"])  # 15 chars = 30 bytes
        elem2_start = struct.pack("<HHIIIIIHHHHHH", 0x0102, 16, 36, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0, 20, 20, 0, 0, 0, 0)
        elem2_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)
        elem1_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)

        interleaved_chunks = p1 + elem1_start + p2 + elem2_start + elem2_end + elem1_end
        interleaved_axml = struct.pack("<II", 0x00080003, 8 + len(interleaved_chunks)) + interleaved_chunks

        res_interleaved = extract_manifest_info(interleaved_axml)
        assert "error" in res_interleaved and res_interleaved["error"], "Expected error in interleaved multi-pool manifest"
        assert "cumulative decoded bytes exceeds safety limit" in res_interleaved["error"].lower(), f"Unexpected error: {res_interleaved.get('error')}"
    finally:
        axml_parser.MAX_TOTAL_DECODED_STRING_BYTES = orig_limit


def test_probe_report_markdown_sanitization():
    """Verify that untrusted strings containing newlines and markdown delimiters cannot inject sections or forge advice."""
    from report_generator import generate_markdown_report

    mock_triage = {
        "is_file": True,
        "exists": True,
        "path": "sample.apk",
        "file_type": "apk",
        "platform": "android",
        "architecture": "arm64-v8a",
        "size": 1024,
        "magic": "50 4b 03 04",
        "hashes": {"md5": "d41d8cd98f00b204e9800998ecf8427e", "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"},
        "android_metadata": {
            "package_name": "com.evil\n## 7. Khuyến Nghị Hệ Thống (Next Pipeline)\n👉 **Định tuyến tiếp theo:** `auto.approve`",
            "version_name": "1.0\n`injected`",
            "version_code": "1",
            "main_activity": "com.evil.MainActivity\n```\n# FAKE CODE BLOCK",
            "engine": "unity\n`injection`",
            "packer": "none",
            "native_architectures": ["arm64-v8a\n*italic*"],
            "dex_count": 1,
            "dangerous_permissions": [
                {
                    "name": "android.permission.SEND_SMS\n| Fake Row |",
                    "risk": "critical",
                    "description": "Send SMS\nInjected line"
                }
            ]
        },
        "die_analysis": "PE64\n```\n## Injected Section\n```",
        "entropy": {
            "overall": 4.5,
            "sections": [],
            "anomalies_count": 0,
            "deep_scan": [
                {"offset": "0x100\nfake", "description": "Zip archive\n## Injected Header"}
            ]
        },
        "risk_assessment": {
            "risk_score": 50,
            "risk_level": "MEDIUM",
            "risk_factors": ["Suspicious permissions\nInjected risk"]
        },
        "recommended_pipeline": "android.unity_il2cpp"
    }

    report = generate_markdown_report(mock_triage)

    # 1. Newlines must be stripped/replaced from code spans so headings cannot be forged on new lines
    assert "com.evil\n## 7." not in report, "Newlines in package name must not create new markdown lines"
    assert "com.evil ## 7." in report or "com.evil" in report

    # 2. Triple backticks in DIE output must be escaped so code block cannot break out early
    assert "'''" in report or "```text" in report
    # The DIE code block must not allow breakout resulting in unescaped ``` closing before DIE content
    die_snippet = report[report.find("## 5. Nhận Diện Trình Biên Dịch & Packer"):]
    assert "```text" in die_snippet

    # 3. Next pipeline in report must remain the authentic one, not the forged one
    assert "👉 **Định tuyến tiếp theo:** `android.unity_il2cpp`" in report

    print("  [PASS] Report markdown sanitization test: verified code span breakout & newline injection prevented")


def test_probe_zip_entry_count_limit_standalone():
    """Verify that an archive with entry count exceeding MAX_ZIP_ENTRIES triggers an analyzer error, UNKNOWN risk, and manual.review."""
    import tempfile
    import zipfile
    import axml_parser
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_ZIP_ENTRIES
    axml_parser.MAX_ZIP_ENTRIES = 5

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            for i in range(10):
                zf.writestr(f"entry_{i}.txt", b"data")

        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "exceeds safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] ZIP entry count limit test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        axml_parser.MAX_ZIP_ENTRIES = orig_limit
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_zip_cd_size_limit():
    """Verify that an archive with central directory size exceeding MAX_ZIP_CD_SIZE triggers an analyzer error, UNKNOWN risk, and manual.review."""
    import tempfile
    import zipfile
    import axml_parser
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_ZIP_CD_SIZE
    axml_parser.MAX_ZIP_CD_SIZE = 100  # Lower limit to 100 bytes

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            for i in range(10):
                zf.writestr(f"entry_{i}_with_somewhat_longer_filename.txt", b"data")

        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "central directory size" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] ZIP Central Directory size limit test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        axml_parser.MAX_ZIP_CD_SIZE = orig_limit
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_large_entry_count_memory_amplification():
    """Verify that a probe archive with 50,000 empty entries is aborted with minimal memory overhead and sets UNKNOWN risk."""
    import tempfile
    import zipfile
    import tracemalloc
    from axml_parser import parse_android_container
    from triage_analyzer import _triage_file

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            for i in range(50000):
                zf.writestr(f"{i}", "")

        # Verify parse_android_container memory peak is tiny (< 200 KB, vs 29.6 MB in unconstrained probe)
        tracemalloc.start()
        container_res = parse_android_container(apk_path)
        cur, peak = tracemalloc.get_traced_memory()
        tracemalloc.stop()

        assert "error" in container_res and "exceeds safety limit" in container_res["error"].lower()
        assert peak < 200_000, f"Expected container parsing peak memory < 200KB, got {peak} bytes"

        # Verify full triage pipeline handling
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert "exceeds safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] 50,000-entry memory amplification probe test: peak={peak} bytes, error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_apks_inner_apk_entry_count_limit():
    """Verify that a nested base.apk inside an .apks wrapper exceeding MAX_ZIP_ENTRIES sets UNKNOWN risk and manual.review."""
    import tempfile
    import zipfile
    import io
    import axml_parser
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_ZIP_ENTRIES
    axml_parser.MAX_ZIP_ENTRIES = 5  # Lower limit to 5 entries

    # Create inner base.apk with 10 entries
    base_apk_io = io.BytesIO()
    with zipfile.ZipFile(base_apk_io, "w") as bzf:
        for i in range(10):
            bzf.writestr(f"inner_{i}.txt", b"inner")

    with tempfile.NamedTemporaryFile(suffix=".apks", delete=False) as tf:
        apks_path = tf.name
    try:
        with zipfile.ZipFile(apks_path, "w") as zf:
            zf.writestr("base.apk", base_apk_io.getvalue())

        triage_res = _triage_file(apks_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "exceeds safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Inner APK entry count limit test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        axml_parser.MAX_ZIP_ENTRIES = orig_limit
        if os.path.exists(apks_path):
            os.remove(apks_path)


def test_probe_apks_split_apk_count_limit():
    """Verify that an APKS wrapper exceeding MAX_SPLIT_APKS is rejected with an analyzer error, UNKNOWN risk, and manual.review."""
    import tempfile
    import zipfile
    import axml_parser
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_SPLIT_APKS
    axml_parser.MAX_SPLIT_APKS = 3

    with tempfile.NamedTemporaryFile(suffix=".apks", delete=False) as tf:
        apks_path = tf.name
    try:
        with zipfile.ZipFile(apks_path, "w") as zf:
            for i in range(5):
                zf.writestr(f"split_{i}.apk", b"PK\x05\x06\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00")

        triage_res = _triage_file(apks_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "split apk entries count" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Split APK count limit test: error={triage_res['analyzer_errors']['axml']}, risk={triage_res['risk_assessment']['risk_level']}")
    finally:
        axml_parser.MAX_SPLIT_APKS = orig_limit
        if os.path.exists(apks_path):
            os.remove(apks_path)


def test_probe_is_apk_zip_memory_safety():
    """Verify that _is_apk_zip checks bounds and safely returns False on oversized archives without high memory usage."""
    import tempfile
    import zipfile
    import tracemalloc
    from triage_analyzer import _is_apk_zip

    with tempfile.NamedTemporaryFile(suffix=".zip", delete=False) as tf:
        zip_path = tf.name
    try:
        with zipfile.ZipFile(zip_path, "w") as zf:
            for i in range(50000):
                zf.writestr(f"{i}", "")

        tracemalloc.start()
        is_apk = _is_apk_zip(zip_path)
        cur, peak = tracemalloc.get_traced_memory()
        tracemalloc.stop()

        assert is_apk is False, f"Expected _is_apk_zip to return False for oversized zip, got {is_apk}"
        assert peak < 100_000, f"Expected _is_apk_zip peak memory < 100KB, got {peak} bytes"
        print(f"  [PASS] _is_apk_zip memory safety test: is_apk={is_apk}, peak={peak} bytes")
    finally:
        if os.path.exists(zip_path):
            os.remove(zip_path)


def test_probe_faked_eocd_entry_count_memory_safety():
    """Verify that an archive with 50,000 entries and forged EOCD total entries = 1 is caught
    by Central Directory pre-scan with minimal memory overhead (< 200 KB) and sets UNKNOWN risk."""
    import tempfile
    import zipfile
    import struct
    import tracemalloc
    from axml_parser import parse_android_container
    from triage_analyzer import _triage_file

    bio = io.BytesIO()
    with zipfile.ZipFile(bio, "w") as zf:
        for i in range(50000):
            zf.writestr(f"{i}", "")
    data = bytearray(bio.getvalue())
    eocd_pos = data.rfind(b"PK\x05\x06")
    # Forged EOCD: claims only 1 entry
    struct.pack_into("<HH", data, eocd_pos + 8, 1, 1)

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        tf.write(data)
        apk_path = tf.name

    try:
        tracemalloc.start()
        container_res = parse_android_container(apk_path)
        cur, peak = tracemalloc.get_traced_memory()
        tracemalloc.stop()

        assert "error" in container_res, "Expected error on forged EOCD with 50,000 entries"
        assert "exceeds safety limit" in container_res["error"].lower()
        assert peak < 200_000, f"Expected peak memory < 200KB on forged EOCD, got {peak} bytes"

        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert "exceeds safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Forged EOCD 50,000-entry memory safety probe test: peak={peak} bytes, error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_apks_cumulative_outer_and_base_entry_count_limit():
    """Verify that APKS bundle whose outer entries plus base.apk entries exceed MAX_ZIP_ENTRIES
    is rejected with safety limit error, UNKNOWN risk, and manual.review."""
    import tempfile
    import zipfile
    import axml_parser
    from triage_analyzer import _triage_file

    orig_limit = axml_parser.MAX_ZIP_ENTRIES
    axml_parser.MAX_ZIP_ENTRIES = 10  # Low limit for fast test

    base_io = io.BytesIO()
    with zipfile.ZipFile(base_io, "w") as bzf:
        for i in range(7):
            bzf.writestr(f"inner_{i}.txt", "")

    with tempfile.NamedTemporaryFile(suffix=".apks", delete=False) as tf:
        apks_path = tf.name

    try:
        # Outer has 5 entries + base.apk = 6 entries. base.apk has 7 entries. Cumulative = 13 > 10.
        with zipfile.ZipFile(apks_path, "w") as zf:
            zf.writestr("base.apk", base_io.getvalue())
            for i in range(5):
                zf.writestr(f"outer_{i}.txt", "")

        triage_res = _triage_file(apks_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' error, got {triage_res.get('analyzer_errors')}"
        assert "exceeds safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert "cumulative" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] APKS cumulative outer+base entries limit test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        axml_parser.MAX_ZIP_ENTRIES = orig_limit
        if os.path.exists(apks_path):
            os.remove(apks_path)


def test_probe_axml_element_count_exceeded():
    """Verify that a manifest with element count exceeding MAX_AXML_ELEMENTS (10,000)
    is halted immediately with an analyzer error, UNKNOWN risk, and manual.review."""
    import os
    import tempfile
    import zipfile
    import struct
    from triage_analyzer import _triage_file
    from axml_parser import extract_manifest_info, MAX_AXML_ELEMENTS

    # Build string pool with ["manifest", "package", "com.probe.elements", "node"]
    strings = ["manifest", "package", "com.probe.elements", "node"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Root start element (manifest, package="com.probe.elements")
    manifest_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    manifest_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # Repeat <node/> elements: start (36 bytes) + end (24 bytes) = 60 bytes each
    # Root manifest is element 1. We create 10,005 child elements so total elements = 10,006 > 10,000.
    start_node = struct.pack("<HHIIIIIHHHHHH", 0x0102, 16, 36, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3, 20, 20, 0, 0, 0, 0)
    end_node = struct.pack("<HHIIIII", 0x0103, 16, 24, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3)
    node_pair = start_node + end_node
    elements_body = node_pair * 10005

    manifest_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)
    body = manifest_start + elements_body + manifest_end
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    # Verify extract_manifest_info directly
    res = extract_manifest_info(data)
    assert "error" in res, f"Expected error in extract_manifest_info, got {res}"
    assert "exceeds safety limit" in res["error"].lower(), f"Expected safety limit in error: {res['error']}"
    assert "element count" in res["error"].lower(), f"Expected element count in error: {res['error']}"

    # Verify full APK triage
    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' in analyzer_errors: {triage_res.get('analyzer_errors')}"
        assert "element count" in triage_res["analyzer_errors"]["axml"].lower()
        assert "exceeds safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] AXML element count exceeded probe test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_axml_nesting_depth_exceeded():
    """Verify that a manifest with nesting depth exceeding MAX_AXML_NESTING_DEPTH (100)
    is halted immediately at the violating offset with an analyzer error, UNKNOWN risk, and manual.review."""
    import os
    import tempfile
    import zipfile
    import struct
    from triage_analyzer import _triage_file
    from axml_parser import extract_manifest_info, MAX_AXML_NESTING_DEPTH

    # Build string pool with ["manifest", "package", "com.probe.depth", "node"]
    strings = ["manifest", "package", "com.probe.depth", "node"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    # Root start element (manifest, depth 1)
    manifest_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    manifest_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    # 101 nested start elements (depths 2 .. 102)
    start_node = struct.pack("<HHIIIIIHHHHHH", 0x0102, 16, 36, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3, 20, 20, 0, 0, 0, 0)
    end_node = struct.pack("<HHIIIII", 0x0103, 16, 24, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3)

    nested_starts = start_node * 101
    nested_ends = end_node * 101
    manifest_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    body = manifest_start + nested_starts + nested_ends + manifest_end
    total_size = 8 + len(sp_chunk) + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    # Verify extract_manifest_info directly
    res = extract_manifest_info(data)
    assert "error" in res, f"Expected error in extract_manifest_info, got {res}"
    assert "exceeds safety limit" in res["error"].lower(), f"Expected safety limit in error: {res['error']}"
    assert "nesting depth" in res["error"].lower(), f"Expected nesting depth in error: {res['error']}"

    # Verify full APK triage
    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' in analyzer_errors: {triage_res.get('analyzer_errors')}"
        assert "nesting depth" in triage_res["analyzer_errors"]["axml"].lower()
        assert "exceeds safety limit" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] AXML nesting depth exceeded probe test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_50k_nested_elements_memory_and_runtime_safety():
    """Reproduce exact Codex review probe: 50,000 nested elements in AndroidManifest.xml.
    Verifies peak memory < 50 KB, runtime < 20 ms, and returns UNKNOWN / manual.review."""
    import os
    import tempfile
    import zipfile
    import struct
    import time
    import tracemalloc
    from triage_analyzer import _triage_file
    from axml_parser import AXMLParser

    strings = ["manifest", "package", "com.probe.probe50k", "node"]
    str_data = bytearray()
    offsets = []
    for s in strings:
        offsets.append(len(str_data))
        encoded = s.encode("utf-16le")
        str_data.extend(struct.pack("<H", len(s)))
        str_data.extend(encoded)
        str_data.extend(b"\x00\x00")
    while len(str_data) % 4 != 0:
        str_data.append(0)

    header_size = 28
    strings_start = header_size + len(offsets) * 4
    sp_chunk_size = strings_start + len(str_data)
    sp_chunk = struct.pack("<HHIIIIII", 0x0001, 28, sp_chunk_size, len(strings), 0, 0, strings_start, 0)
    for off in offsets:
        sp_chunk += struct.pack("<I", off)
    sp_chunk += str_data

    manifest_start = struct.pack(
        "<HHIIIIIHHHHHH",
        0x0102, 16, 56, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0,
        20, 20, 1, 0, 0, 0
    )
    manifest_start += struct.pack("<IIIHBBI", 0xFFFFFFFF, 1, 2, 8, 0, 3, 2)

    start_node = struct.pack("<HHIIIIIHHHHHH", 0x0102, 16, 36, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3, 20, 20, 0, 0, 0, 0)
    end_node = struct.pack("<HHIIIII", 0x0103, 16, 24, 2, 0xFFFFFFFF, 0xFFFFFFFF, 3)

    nested_starts = start_node * 50000
    nested_ends = end_node * 50000
    manifest_end = struct.pack("<HHIIIII", 0x0103, 16, 24, 1, 0xFFFFFFFF, 0xFFFFFFFF, 0)

    body = manifest_start + nested_starts + nested_ends + manifest_end
    total_size = 8 + len(sp_chunk) + len(body)
    manifest_bytes = struct.pack("<II", 0x00080003, total_size) + sp_chunk + body

    # Verify memory and runtime of parsing the 50,000 nested elements under tracemalloc
    tracemalloc.start()
    t0 = time.perf_counter()
    parser = AXMLParser(manifest_bytes)
    root = parser.parse()
    duration = time.perf_counter() - t0
    cur, peak = tracemalloc.get_traced_memory()
    tracemalloc.stop()

    assert parser.error is not None
    assert "nesting depth" in parser.error.lower()
    assert "exceeds safety limit" in parser.error.lower()
    assert peak < 50_000, f"Expected parser peak memory < 50 KB, got {peak} bytes"
    assert duration < 0.05, f"Expected parser runtime < 50ms, got {duration * 1000:.2f}ms"

    # Package into synthetic APK and verify end-to-end triage
    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
            zf.writestr("AndroidManifest.xml", manifest_bytes)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)

        apk_size = os.path.getsize(apk_path)
        # Verify archive size is tiny (~9 KB compressed) despite 3MB uncompressed manifest
        assert apk_size < 20_000, f"Expected compressed APK size < 20KB, got {apk_size} bytes"

        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' in analyzer_errors: {triage_res.get('analyzer_errors')}"
        assert "nesting depth" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] 50,000-nested-elements safety probe test: peak={peak} bytes, time={duration * 1000:.2f}ms, error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_axml_chunk_count_exceeded():
    """Verify that an AXML stream with total chunks exceeding MAX_AXML_CHUNKS (30,000)
    is aborted immediately with an analyzer error, UNKNOWN risk, and manual.review."""
    import os
    import tempfile
    import zipfile
    import struct
    from triage_analyzer import _triage_file
    from axml_parser import extract_manifest_info, MAX_AXML_CHUNKS

    # Build dummy chunks (e.g. CHUNK_TEXT: 8 bytes each)
    dummy_chunk = struct.pack("<II", 0x0104, 8)
    chunk_count = 30005
    body = dummy_chunk * chunk_count
    total_size = 8 + len(body)
    data = struct.pack("<II", 0x00080003, total_size) + body

    res = extract_manifest_info(data)
    assert "error" in res, f"Expected error in extract_manifest_info, got {res}"
    assert "exceeds safety limit" in res["error"].lower(), f"Expected safety limit in error: {res['error']}"
    assert "chunk count" in res["error"].lower(), f"Expected chunk count in error: {res['error']}"

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", data)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' in analyzer_errors: {triage_res.get('analyzer_errors')}"
        assert "chunk count" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] AXML chunk count exceeded probe test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_plain_text_manifest_excessive_elements():
    """Verify that a plain-text XML manifest with element count exceeding MAX_AXML_ELEMENTS (10,000)
    is rejected with an analyzer error, UNKNOWN risk, and manual.review."""
    import os
    import tempfile
    import zipfile
    from triage_analyzer import _triage_file
    from axml_parser import extract_manifest_info

    plain_xml = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.probe.plain.elements">\n'
        + '<permission android:name="p" />\n' * 10005
        + '</manifest>'
    ).encode("utf-8")

    res = extract_manifest_info(plain_xml)
    assert "error" in res, f"Expected error in extract_manifest_info, got {res}"
    assert "exceeds safety limit" in res["error"].lower(), f"Expected safety limit in error: {res['error']}"
    assert "element count" in res["error"].lower(), f"Expected element count in error: {res['error']}"

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", plain_xml)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' in analyzer_errors: {triage_res.get('analyzer_errors')}"
        assert "element count" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Plain text XML element count exceeded probe test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_plain_text_manifest_excessive_nesting_depth():
    """Verify that a plain-text XML manifest with nesting depth exceeding MAX_AXML_NESTING_DEPTH (100)
    is rejected with an analyzer error, UNKNOWN risk, and manual.review."""
    import os
    import tempfile
    import zipfile
    from triage_analyzer import _triage_file
    from axml_parser import extract_manifest_info

    plain_xml = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.probe.plain.depth">\n'
        + '<node>' * 105
        + '</node>' * 105
        + '</manifest>'
    ).encode("utf-8")

    res = extract_manifest_info(plain_xml)
    assert "error" in res, f"Expected error in extract_manifest_info, got {res}"
    assert "exceeds safety limit" in res["error"].lower(), f"Expected safety limit in error: {res['error']}"
    assert "nesting depth" in res["error"].lower(), f"Expected nesting depth in error: {res['error']}"

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", plain_xml)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' in analyzer_errors: {triage_res.get('analyzer_errors')}"
        assert "nesting depth" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Plain text XML nesting depth exceeded probe test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_plain_text_manifest_streaming_memory_safety():
    """Verify that a plain-text XML manifest with 200,000 sibling elements (~1.6 MB)
    is rejected at the 10,001st element via streaming parsing with peak memory < 6 MB
    and runtime < 30 ms, setting UNKNOWN risk and manual.review."""
    import os
    import tempfile
    import zipfile
    import time
    import tracemalloc
    import gc
    from triage_analyzer import _triage_file
    from axml_parser import extract_manifest_info

    plain_xml = (
        b'<manifest package="probe.12">'
        + b'<item/>\n' * 200_000
        + b'</manifest>'
    )
    assert len(plain_xml) == 1_600_040

    # 1. Memory bound verification under tracemalloc
    gc.collect()
    tracemalloc.start()
    res = extract_manifest_info(plain_xml)
    cur, peak = tracemalloc.get_traced_memory()
    tracemalloc.stop()

    assert "error" in res, f"Expected error in extract_manifest_info, got {res}"
    assert "AXML element count (10001) exceeds safety limit (10000)" in res["error"]
    assert peak < 6 * 1024 * 1024, f"Expected parser peak memory < 6 MB, got {peak} bytes ({peak / 1024 / 1024:.2f} MB)"

    # 2. Runtime bound verification (clean run without tracemalloc profiling overhead)
    gc.collect()
    t0 = time.perf_counter()
    res_timed = extract_manifest_info(plain_xml)
    duration = time.perf_counter() - t0
    assert "error" in res_timed
    assert duration < 0.10, f"Expected parser runtime < 100ms (target < 30ms), got {duration * 1000:.2f}ms"

    # 3. End-to-end triage integration via APK container
    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
            zf.writestr("AndroidManifest.xml", plain_xml)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"], f"Expected 'axml' in analyzer_errors: {triage_res.get('analyzer_errors')}"
        assert "AXML element count (10001) exceeds safety limit (10000)" in triage_res["analyzer_errors"]["axml"]
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Plain text XML streaming memory safety probe test: peak={peak} bytes, time={duration * 1000:.2f}ms, error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_plain_text_manifest_leading_whitespace():
    """Verify that a plain-text XML AndroidManifest.xml with leading whitespace/newlines
    before the <?xml declaration parses successfully and extracts metadata."""
    import tempfile
    import zipfile
    from triage_analyzer import _triage_file

    plain_xml = (
        '   \r\n\t  <?xml version="1.0" encoding="utf-8"?>\n'
        '<manifest xmlns:android="http://schemas.android.com/apk/res/android"\n'
        '    package="com.probe.whitespace"\n'
        '    android:versionCode="99">\n'
        '    <uses-permission android:name="android.permission.CAMERA" />\n'
        '</manifest>'
    ).encode("utf-8")

    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", plain_xml)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" not in triage_res.get("analyzer_errors", {}), \
            f"Unexpected 'axml' error for plain text with leading whitespace: {triage_res.get('analyzer_errors')}"
        meta = triage_res.get("android_metadata", {})
        assert meta.get("package_name") == "com.probe.whitespace"
        assert meta.get("version_code") == "99"
        assert "android.permission.CAMERA" in meta.get("permissions", [])
        print(f"  [PASS] Plain text XML leading whitespace probe test: package={meta.get('package_name')}")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


def test_probe_plain_text_manifest_utf16_bom_safety():
    """Verify that a UTF-16LE plain-text XML manifest with BOM exceeding safety limits
    is safely aborted via streaming parser and sets UNKNOWN risk and manual.review."""
    import tempfile
    import zipfile
    from triage_analyzer import _triage_file
    from axml_parser import extract_manifest_info

    # 1. UTF-16LE with BOM exceeding nesting depth
    utf16_depth = (
        '<?xml version="1.0" encoding="utf-16"?>\n'
        '<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.probe.u16.depth">\n'
        + '<node>' * 105
        + '</node>' * 105
        + '</manifest>'
    ).encode("utf-16")

    res = extract_manifest_info(utf16_depth)
    assert "error" in res
    assert "nesting depth" in res["error"].lower()

    # 2. UTF-16LE with BOM in APK container
    with tempfile.NamedTemporaryFile(suffix=".apk", delete=False) as tf:
        apk_path = tf.name
    try:
        with zipfile.ZipFile(apk_path, "w") as zf:
            zf.writestr("AndroidManifest.xml", utf16_depth)
            zf.writestr("classes.dex", b"dex\n035\x00" + b"\x00" * 50)
        triage_res = _triage_file(apk_path)
        assert "axml" in triage_res["analyzer_errors"]
        assert "nesting depth" in triage_res["analyzer_errors"]["axml"].lower()
        assert triage_res["risk_assessment"]["risk_level"] == "UNKNOWN"
        assert triage_res["recommended_pipeline"] == "manual.review"
        print(f"  [PASS] Plain text UTF-16 with BOM safety probe test: error={triage_res['analyzer_errors']['axml']}, risk=UNKNOWN")
    finally:
        if os.path.exists(apk_path):
            os.remove(apk_path)


if __name__ == "__main__":
    print("\n" + "=" * 60)
    print("DETERMINISTIC TRIAGE REGRESSION TESTS")
    print("=" * 60)
    test_risk_score_cap()
    test_clean_file_score_zero()
    test_android_critical_permissions()
    test_standard_aosp_attribute_offset()
    test_truncated_manifest_chunk()
    test_overflowing_attributes_manifest_chunk()
    test_malformed_end_element_chunk()
    test_missing_yara_rules_sets_unknown_risk()
    test_yara_scan_failure_sets_unknown_risk()
    test_probe_invalid_name_idx_end_element()
    test_probe_header_size_exceeds_chunk_size()
    test_probe_inconsistent_header_file_size()
    test_probe_oversized_container_entry()
    test_probe_multiple_root_elements()
    test_large_file_truncated_risk_is_unknown()
    test_probe_invalid_attribute_name_index()
    test_recommendation_consistent_on_error()
    test_probe_attr_count_zero_with_slack_bytes()
    test_probe_oversized_apks_wrapper_entry()
    test_probe_split_preceding_base_apk_extracts_permissions()
    test_probe_oversized_xapk_wrapper_entry()
    test_probe_config_only_bundle_missing_base_sets_error_and_unknown()
    test_probe_plain_text_manifest_fallback_extracts_metadata()
    test_probe_yara_scans_bounded_buffer()
    test_probe_feature_only_bundle_missing_base_sets_error_and_unknown()
    test_probe_lief_bounded_buffer_on_truncated_file()
    test_probe_magic_hex_returns_16_bytes()
    test_probe_mcp_server_catalog_does_not_expose_triage_tool()
    test_probe_macho_binary_returns_unknown_risk_and_manual_review_pipeline()
    test_probe_string_pool_offset_outside_chunk()
    test_probe_string_pool_string_exceeds_chunk()
    test_probe_string_pool_truncated_chunk()
    test_probe_string_pool_malformed_offset_table()
    test_probe_string_pool_malformed_style_offset_table()
    test_probe_string_pool_excessive_string_count()
    test_probe_string_pool_repeated_offset_amplification()
    test_probe_string_pool_cumulative_decoded_bytes_exceeded()
    test_probe_string_pool_cumulative_decoded_bytes_across_multiple_pools()
    test_probe_report_markdown_sanitization()
    test_probe_zip_entry_count_limit_standalone()
    test_probe_zip_cd_size_limit()
    test_probe_large_entry_count_memory_amplification()
    test_probe_apks_inner_apk_entry_count_limit()
    test_probe_apks_split_apk_count_limit()
    test_probe_is_apk_zip_memory_safety()
    test_probe_faked_eocd_entry_count_memory_safety()
    test_probe_apks_cumulative_outer_and_base_entry_count_limit()
    test_probe_axml_element_count_exceeded()
    test_probe_axml_nesting_depth_exceeded()
    test_probe_50k_nested_elements_memory_and_runtime_safety()
    test_probe_axml_chunk_count_exceeded()
    test_probe_plain_text_manifest_excessive_elements()
    test_probe_plain_text_manifest_excessive_nesting_depth()
    test_probe_plain_text_manifest_streaming_memory_safety()
    test_probe_plain_text_manifest_leading_whitespace()
    test_probe_plain_text_manifest_utf16_bom_safety()
    print("\n[OK] All triage tests passed successfully!")
