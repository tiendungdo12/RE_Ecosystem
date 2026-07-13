# Static Triage MCP Server

Block 3.1 starts the real RE triage layer.

This server intentionally uses only the Python standard library plus the MCP SDK. It does not depend on LIEF or DIE yet, so the black-box contract is simple and easy to test.

## Tool

`triage_file(path: str)`

Input:

- Local file path.

Output:

- File existence and size.
- MD5 and SHA256.
- Magic bytes.
- Basic file type and platform.
- Basic architecture when PE/ELF headers are available.
- Shannon entropy.
- Signals and recommended pipeline.

## Test

```powershell
cd D:\RE_Ecosystem\RE_CommandCenter\Tools\MCP_Static_Analysis\triage_server
python -m pip install -r requirements.txt
.\test_mcp_stdio.ps1
```

Expected output includes `triage_file` in tools/list and a structured JSON result for this README.
