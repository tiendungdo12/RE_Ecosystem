# Direct Triage Runner and Legacy MCP Server

> [!CAUTION]
> **Trạng thái 13/10/2026:** `triage_file` MCP route là **LEGACY**, không thuộc canonical deterministic triage. Direct subprocess runner vẫn được pipeline sử dụng. PRE-H0 triage được user xác nhận DONE. Xem [`../docs/CURRENT_STATE.md`](../docs/CURRENT_STATE.md) và [`../docs/INTEROPERABILITY.md`](../docs/INTEROPERABILITY.md).

## Vai trò hiện tại

Tool được đăng ký trong MCP catalog (`@mcp.tool()`):

- `md5_file(path: str)` — tính MD5 của file, dùng cho verification/integrity check nội bộ.

Lưu ý: `triage_file` **không** còn là MCP tool. Hàm vẫn tồn tại trong `server.py` làm reference/test helper nhưng không có decorator `@mcp.tool()`; không thể gọi qua MCP transport. Canonical triage path chạy trực tiếp qua `ITriagePipeline` -> `TriageService` -> `DirectTriageSubprocessAnalyzer` (subprocess gọi `direct_triage.py`).

Source hiện tại dùng `fastmcp`, `lief`, AXML parser và malware rules nội bộ. Dependency phải được khóa bằng requirements/lock file đồng bộ với setup/CI; README cũ mô tả “chỉ standard library + MCP” không còn đúng.

Input:

- local file path.

Output hiện có thể chứa:

- file existence/size;
- MD5/SHA256;
- magic/type/platform/architecture;
- entropy/sections/import summary;
- Android metadata khi applicable;
- signals/recommended pipeline.

## Current deterministic path and legacy boundary

```text
PRE-H0 baseline (reported DONE):
File -> ITriagePipeline (TriageService) -> direct analyzers (Hash, Authenticode, DIE, Binwalk) + DirectTriageSubprocessAnalyzer (direct_triage.py) -> TriageResult

H3 (TARGET):
External agent -> MCP -> headless Platform host -> Core services/policy -> direct or specialist adapters
H7 (optional TARGET):
IDE -> ACP -> RE Agent Runtime (AI Harness) -> Core services/policy
```

`TriageService` implements `ITriagePipeline`; `TriageOrchestrator` is an obsolete compatibility wrapper. The canonical pipeline does not use the registered MCP `triage_file` route. `direct_triage.py` is the subprocess runner. `server.py` retains legacy/reference MCP code; do not add canonical static triage to that route. Target capability MCP for external agents belongs to H3 and projects Core services through a separate Platform host. The host is not AI Harness; our optional H7 Agent Runtime and WinUI are not prerequisites.
