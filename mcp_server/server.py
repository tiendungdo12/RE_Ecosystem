# server.py
"""
Unified Static Analysis MCP Server (RE Ecosystem 2.0)
Legacy reference MCP adapter for Phase 1 Static Analysis.
Canonical deterministic triage in Phase 1 runs directly via ITriagePipeline and direct_triage.py.
"""

import os
import sys
from typing import Any

# Ensure directory is in sys.path
_current_dir = os.path.dirname(os.path.abspath(__file__))
if _current_dir not in sys.path:
    sys.path.insert(0, _current_dir)

from fastmcp import Context, FastMCP
from triage_analyzer import (
    _calculate_file_hashes,
    _calculate_risk_assessment,
    _classify_file_fallback,
    _detect_engine,
    _detect_packer_yara,
    _entropy,
    _estimate_packer,
    _get_magic_hex,
    _is_apk_zip,
    _read_file,
    _recommend_pipeline,
    _triage_file,
)

mcp = FastMCP("RE_Unified_Server")


# Note: triage_file is NOT exposed as an @mcp.tool() to prevent bypassing the canonical host pipeline ITriagePipeline.
def triage_file(path: str, ctx: Context = None) -> dict[str, Any]:
    """[DEPRECATED in Phase 1] Phân tích tĩnh file đầu vào qua MCP (Legacy Reference).
    Canonical Phase 1 deterministic triage uses ITriagePipeline and direct_triage.py without MCP.
    """

    def report_progress(msg: str):
        if ctx:
            ctx.info(msg)

    result = _triage_file(path, progress_cb=report_progress)

    # Tự động tạo Executive Markdown Report
    try:
        if ctx:
            ctx.info("Đang tổng hợp Executive Markdown Report...")
        from report_generator import generate_markdown_report

        result["markdown_report"] = generate_markdown_report(result)
    except Exception as e:
        result["markdown_report"] = f"Lỗi khi tạo Markdown Report: {e}"

    return result


@mcp.tool()
def md5_file(path: str) -> dict[str, Any]:
    """[Phase 1] Tính toán mã băm MD5 và kích thước tệp tin."""
    if not os.path.exists(path) or not os.path.isfile(path):
        return {"error": f"Tệp không tồn tại: {path}", "path": path}
    md5, _ = _calculate_file_hashes(path)
    size = os.path.getsize(path)
    return {"path": path, "md5": md5, "size": size}


if __name__ == "__main__":
    mcp.run(transport="stdio")
