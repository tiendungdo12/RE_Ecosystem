# direct_triage.py
"""
Direct CLI Triage Runner for Phase 1 Deterministic Lane.
Khong dung MCP (FastMCP/Stdio/JSON-RPC), khong dung LLM.
Chay doc lap qua subprocess de goi LIEF, AXML parser va Threat Matrix.
"""

import argparse
import json
import os
import sys

# Dam bao thu muc mcp_server nam trong sys.path
_current_dir = os.path.dirname(os.path.abspath(__file__))
if _current_dir not in sys.path:
    sys.path.insert(0, _current_dir)

if sys.stdout.encoding.lower() != "utf-8":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except AttributeError:
        pass

from report_generator import generate_markdown_report
from triage_analyzer import _triage_file


def main():
    parser = argparse.ArgumentParser(description="Direct Static Triage Runner (No MCP/LLM)")
    parser.add_argument("target_path", nargs="?", help="Path to target binary/file")
    parser.add_argument("--md5", dest="md5", default=None, help="Precalculated MD5 digest")
    parser.add_argument("--sha256", dest="sha256", default=None, help="Precalculated SHA256 digest")

    args = parser.parse_args()

    if not args.target_path:
        print(json.dumps({
            "exists": False,
            "error": "No target file path specified.",
            "recommended_pipeline": "manual.review"
        }))
        sys.exit(1)

    target_path = args.target_path
    precomputed_hashes = None
    if args.md5 and args.sha256:
        precomputed_hashes = {"md5": args.md5, "sha256": args.sha256}

    try:
        result = _triage_file(target_path, precomputed_hashes=precomputed_hashes)
        try:
            result["markdown_report"] = generate_markdown_report(result)
        except Exception as report_err:
            result["markdown_report"] = f"Error generating markdown report: {report_err}"

        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)
    except Exception as ex:
        print(json.dumps({
            "path": target_path,
            "exists": False,
            "error": f"Direct triage failed: {str(ex)}",
            "recommended_pipeline": "error.triage_failed"
        }))
        sys.exit(1)


if __name__ == "__main__":
    main()
