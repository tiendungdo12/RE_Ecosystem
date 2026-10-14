> [!IMPORTANT]
> **ARCHITECTURE INPUT / REFERENCE — terminology clarified 13 October 2026.** The original vision below is preserved. Its following conversation is saved [verbatim](docs/reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md) and defines the precise terminology: RE Platform Core owns state/persistence/evidence/capability/policy/verification/context/adapters; optional RE Agent Runtime is AI Harness; Command Center is the optional control plane. Read [canonical architecture](docs/ARCHITECTURE.md), [roadmap](STRATEGY.md) and [CURRENT](docs/CURRENT_STATE.md) for implementation truth. PRE-H0 remains DONE per user report.

---
# RE_ECOSYSTEM — FULL CONTEXT / HANDOFF

## 0. Trạng thái hiện tại của cuộc trao đổi

Dự án đang có tên làm việc **RE_Ecosystem**, với app Windows chính tên **RE Command Center / RE CommandCenter**.

Hiện tại đang ở giai đoạn **PRE-H0** của roadmap cũ.

Quyết định hiện tại:

- **KHÔNG thay đổi PRE-H0 theo vision mới.**
- Tiếp tục review và hoàn thành PRE-H0 đúng scope hiện tại.
- Khi PRE-H0 PASS hoàn toàn, quay lại để thực hiện một bước **Architecture Reset**.
- Sau Architecture Reset mới sửa toàn bộ tài liệu `.md`, chốt roadmap mới H0 trở đi và viết bộ prompt mới.
- Bộ prompt mới phải theo workflow đã dùng trong các chat trước:
  - IMPLEMENT
  - FIRST REVIEW
  - FIX
  - RE-REVIEW
- PRE-H0 dùng tiếp bộ prompt hiện tại, KHÔNG rewrite lại PRE-H0.

---

# 1. Bối cảnh dự án

Đây là dự án tập trung vào:

- Reverse Engineering
- Malware Analysis
- Windows Internals
- Binary Analysis
- Security Analysis
- Native/driver workflows
- AI-assisted RE

Mục tiêu ban đầu:

> Xây một app Windows native kiểu “JetBrains cho Reverse Engineering”, gom deterministic triage, binary analysis, AI analysis, scoring, evidence, dashboard, integration với IDA/Ghidra/Burp/etc.

Sau nhiều lần nghiên cứu/review, định hướng ban đầu đã phát triển thành:

> Không chỉ là một RE workbench có AI, mà là một **RE-native Agent Harness / RE Agent Platform**.

Nhưng sau khi nghiên cứu thêm ecosystem agent hiện tại, định vị tốt hơn là:

> **RE_Ecosystem = model-agnostic RE Agent Platform**
>
> **RE Command Center = first-party Windows-native Control Plane / GUI của platform**

---

# 2. Nguyên tắc cốt lõi đã chốt

## 2.1 Evidence-driven, không chat-driven

Không coi nội dung model trả ra là fact.

Một claim quan trọng phải có:

```text
Claim
  +-- source/tool
  +-- source location
  +-- raw/normalized evidence
  +-- confidence
  +-- verification status
```

Các trạng thái finding:

```text
hypothesis
supported
verified
contradicted
unknown
```

---

## 2.2 Deterministic lane phải độc lập AI

Triage cơ bản không được:

- gọi LLM
- phụ thuộc model
- đi qua MCP chỉ để gọi local tool
- biến task deterministic thành agent loop

Mục tiêu:

```text
file
 -> format detect
 -> deterministic analyzers
 -> score/evidence
```

LLM chỉ xuất hiện khi cần reasoning/deep analysis.

---

## 2.3 Tool output là untrusted data

Strings/comment từ binary, decompiler output, network payload, tool stdout/stderr... đều là **data không đáng tin**, không được coi là instructions.

Không cho binary/decompiler text prompt-inject policy hoặc permissions.

---

## 2.4 Side-effect phải có policy

Các capability như:

- patch binary
- replay request
- active scan
- attach process
- inject
- driver load
- system modification
- boot config changes
- dangerous kernel actions

phải đi qua:

```text
Capability
 -> Policy
 -> Allow / Deny / RequireApproval
 -> Execute
 -> Evidence
```

Không dùng `--dangerously-skip-permissions` hoặc tương đương làm mặc định.

---

## 2.5 Không biến Local AI thành requirement

RE Core phải chạy:

- CPU
- không GPU
- không Local LLM

Cloud LLM là reasoning option.

Local LLM/Ollama là deployment option cho:

- offline
- privacy
- air-gapped
- local-only workflow

SEMA-GUARD/GNN cũng không được trở thành dependency của deterministic core.

---

# 3. Công nghệ hiện tại / môi trường

Primary platform:

- Windows
- WinUI 3
- Windows App SDK
- C# / .NET

Toolchain:

- Rider
- Visual Studio Build Tools
- Windows SDK / WDK/EWDK khi cần
- WinDbg
- VM/test machine cho dangerous native/driver workflows

Đã dùng/nghiên cứu:

- Antigravity 2.0
- ChatGPT App
- JetBrains Air
- Zed
- VS Code
- JetBrains ecosystem
- Osaurus
- Hermes
- Goose
- Codex
- Gemini CLI
- Claude/Claude Code
- các ACP/CLI agents khác

---

# 4. Toolchain RE hiện tại / định hướng

Core deterministic tools đã/đang có:

- Detect It Easy (DIE)
- Binwalk
- LIEF
- Hash
- Authenticode
- Entropy
- AXML parser
- IOC regex
- Secrets detection
- YARA/YARA-X
- API combination detection
- CWE/ATT&CK/MASVS mapping

Deep/agentic tools:

- IDA Pro
- Ghidra
- Binary Ninja
- JADX
- Capstone
- Burp Suite
- Frida
- MemProcFS
- WinDbg/x64dbg
- KsDumper
- Z3/Triton
- các tool khác khi cần

Nguyên tắc:

> Internal implementation dùng interface hẹp nhất/cheap nhất.
>
> Không MCP-hoá mọi thứ.

Ví dụ:

```text
DIE:
RE Core -> direct native/DLL

IDA:
RE Core -> narrow adapter/RPC/plugin

Ghidra:
RE Core -> narrow RPC/daemon

Burp:
RE Core -> narrow adapter/API

External AI:
Agent -> MCP -> RE Platform
```

---

# 5. Architecture CŨ đang được dùng cho PRE-H0

Tài liệu chính hiện tại:

- `AGENTS.md`
- `STRATEGY.md`
- `research_report.md`
- `RE_AI_Agent_Knowledge_Base_2026-09-20.md`
- README/other docs liên quan

`STRATEGY.md` hiện là canonical deployment strategy của roadmap cũ.

Snapshot được tài liệu hóa khoảng 20/09/2026.

Các trạng thái đã ghi trong strategy:

- WinUI 3 + MVVM: có
- DIE/Binwalk native wrapper: có
- Hash/Auth/LIEF/AXML/entropy: có nhưng một số path còn cần hội tụ
- Ollama: có
- Antigravity private Language Server integration: experimental/legacy, không coi là production contract
- MCP client infrastructure: có
- SQLite KnowledgeStore: có
- Case/Run/Evidence/Finding: chưa hoàn thiện
- Scorecard/SARIF/Verifier: chưa hoàn thiện
- GatewayRouter/CapabilityRegistry/PolicyEnforcer: chưa hoàn thiện

---

# 6. PRE-H0 hiện tại

PRE-H0 đang có scope riêng và phải hoàn thành trước.

Nguyên tắc:

- deterministic triage
- direct/native/subprocess
- không LLM
- mục tiêu cuối không qua MCP
- không refactor theo architecture mới giữa chừng
- không thêm Project Platform/ACP/A2A/new MCP/etc vào PRE-H0

Có thể vẫn dùng:

- direct native call
- subprocess cho thư viện không có native API
- tests
- refactor để hội tụ deterministic pipeline
- giữ behavior hiện tại

PRE-H0 phải được coi là:

```text
PRE-H0 COMPLETE
      ↓
baseline/tag/commit
      ↓
Architecture Reset
```

Không chỉnh retroactively PRE-H0 chỉ vì vision mới.

---

# 7. Vì sao cần Architecture Reset sau PRE-H0

Roadmap cũ đặt:

- Scorecard ở H0
- MCP/Deep RE ở H1/H2
- InvestigationGoal ở H2
- Project Workspace tận H3+

Nhưng vision mới cho rằng điều này sai thứ tự.

Lý do:

Nếu RE_Ecosystem trở thành một platform dùng chung cho:

- ChatGPT
- Codex
- Antigravity
- JetBrains
- VS Code
- Zed
- Osaurus
- Hermes
- Goose
- CLI agents
- các host khác

thì **Project/Case/Run/Artifact/Evidence/Finding không thể là feature UI xuất hiện tận H3**.

Chúng phải là canonical domain state ngay từ đầu.

Scorecard cũng phải trở thành output/view trên state đó, không phải “database riêng” của Scorecard.

---

# 8. Vision MỚI

## 8.1 Không nên làm “một chatbot RE khổng lồ”

Không cạnh tranh với:

- ChatGPT
- Osaurus
- Goose
- Hermes
- Codex
- Antigravity
- JetBrains Air
- VS Code

ở phần generic agent/chat.

RE_Ecosystem nên sở hữu:

- RE domain state
- deterministic RE intelligence
- evidence
- provenance
- verification
- capability model
- policy
- context/token optimization
- tool integrations
- project/case management
- RE reporting

---

# 9. Product positioning mới

Nên hiểu:

```text
RE_Ecosystem
    =
RE Agent Platform
```

và:

```text
RE Command Center
    =
first-party Windows Control Plane
```

Command Center không “chứa toàn bộ platform”.

Thay vào đó:

```text
                    External Hosts
  ┌──────────────────────────────────────────┐
  │ ChatGPT / Codex / Antigravity / IDEs    │
  │ Osaurus / Hermes / Goose / Zed / CLI    │
  └──────────────────────┬───────────────────┘
                         MCP / ACP / CLI / A2A
                                  │
                         ┌────────▼─────────┐
                         │  RE PLATFORM     │
                         │                  │
                         │ Project          │
                         │ Case             │
                         │ Run              │
                         │ Artifact         │
                         │ Evidence        │
                         │ Finding          │
                         │ Capability       │
                         │ Policy           │
                         │ Context          │
                         │ Verification     │
                         │ Skills           │
                         │ Evaluation       │
                         └────────┬─────────┘
                                  │
             ┌────────────────────┼────────────────────┐
             │                    │                    │
        deterministic        RE adapters          agent runtime
             │                    │                    │
       DIE/LIEF/etc         IDA/Ghidra/Burp      GPT/Gemini/etc
             │                    │                    │
             └────────────────────┼────────────────────┘
                                  │
                     ┌────────────▼────────────┐
                     │ RE Command Center       │
                     │ WinUI Control Plane     │
                     └─────────────────────────┘
```

---

# 10. “Universal layer” mà user đang nghĩ tới là gì?

Không nên gọi toàn bộ nó là “skills”.

Cần phân biệt:

## Skill

Reusable knowledge/workflow:

```text
packed-pe-analysis
malware-ioc-investigation
android-re-analysis
driver-review
```

## Capability

Một khả năng thực thi có contract rõ:

```text
get_binary_overview
get_function_summary
get_xrefs
search_code
triage_binary
query_evidence
burp_request
```

## MCP

Protocol để external host gọi capabilities.

## Agent Plugin

Package portable gồm các thành phần như:

```text
plugin.json
skills/
mcp.json
```

## Harness

Runtime:

- planner
- context
- memory/state
- permissions
- capability routing
- evidence
- verifier
- agent loop

## ACP

Protocol để một **full agent/harness** có thể chạy trong IDE/client.

## A2A

Protocol cho **agent ↔ agent**.

## Command Center

GUI/control plane.

## Platform

Tất cả những thứ trên.

---

# 11. Các repo/sản phẩm đã làm TỪNG PHẦN

Sau nghiên cứu web + các tài liệu cũ, kết luận là:

## Rikugan

Rất gần phía RE Agent Harness.

Có:

- IDA
- Binary Ninja
- agent loop
- context management
- 60+ RE tools
- plan mode
- subagents
- session persistence
- skills
- MCP

Reference rất quan trọng.

Khác biệt:

Rikugan thiên về:

```text
agent sống trong disassembler
```

trong khi RE_Ecosystem mới muốn:

```text
RE Platform độc lập
    ↓
IDA/Ghidra/ChatGPT/JetBrains/etc đều kết nối vào
```

---

## re-mcp

Rất gần với:

```text
RE capability provider
```

Có:

- IDA
- Ghidra
- MCP
- headless
- multi-database
- supervisor/worker architecture

Concept:

```text
Agent
  ↓
 MCP
  ↓
re-mcp
 ↓   ↓
IDA Ghidra
```

Nhưng chưa phải:

- Case
- Evidence ledger
- Finding/verification
- deterministic triage core
- project workspace
- platform control plane

---

## ghidra-rpc

Cực hợp triết lý token-efficient.

Concept:

```text
Agent
  ↓
CLI/RPC
  ↓
persistent Ghidra daemon
```

Structured JSON, warm state.

Bài học:

- narrow interface
- structured outputs
- persistent backend
- không đưa toàn Ghidra context vào LLM

---

## Goose

Gần ở platform/harness layer.

Có:

- Desktop
- CLI
- API
- multi-provider
- MCP
- ACP
- agent runtime
- subagents/workflows

Bài học:

- tách UI khỏi runtime
- ACP
- client ↔ harness
- provider abstraction
- reusable workflows

Không có RE-specific domain state.

---

## Osaurus

Gần với generic AI capability fabric.

Có:

- local/cloud models
- skills
- memory
- MCP in
- MCP out
- CLI
- native plugins
- plugin ABI

Đáng học:

- native-first
- model interchangeable
- plugin system
- MCP server mode
- skills
- memory

Nhưng không thay thế RE Command Center.

---

## Hermes

Đáng học:

- Skills
- progressive disclosure
- MCP
- per-server tool filtering
- memory
- subagents

Đặc biệt hữu ích cho token/context optimization.

---

## CAI / CSI

CAI là cyber agent architecture reference.

CAI đã archived; successor hiện được nhắc tới là CSI/Cybersecurity Superintelligence.

Đáng học:

- cybersecurity agents
- multi-agent
- tools
- handoff
- tracing
- benchmarking
- MCP
- harness/scaffolding

Nhưng không phải RE project/case/evidence platform.

---

# 12. Kết luận cạnh tranh

Nếu sản phẩm chỉ là:

> “AI agent có IDA/Ghidra/MCP/skills”

thì KHÔNG đủ khác biệt nữa.

Đã có:

- Rikugan
- re-mcp
- ghidra-rpc
- nhiều MCP/IDA/Ghidra projects

Nhưng nếu sản phẩm là:

> RE platform độc lập, case-centric/evidence-centric, sở hữu deterministic intelligence + state + provenance + verification + capability/context/policy layer, rồi project capabilities được expose qua MCP/Skills/Agent Plugins/CLI và full RE agent qua ACP

thì đó là khoảng trống đáng theo đuổi.

---

# 13. Kiến trúc H0+ MỚI được đề xuất

## H0 — Platform Foundation

Mục tiêu:

> RE Core có canonical state độc lập UI/model/protocol.

Domain:

```text
Project
Case
Run
Artifact
Evidence
Finding
```

Persistence, IDs, references, lifecycle.

Không cần:

- LLM
- MCP
- ACP
- WinUI
- IDA
- Ghidra

để test domain core.

---

## H1 — Deterministic Product Lane

Lấy toàn bộ H0 cũ về đây:

- scorecard
- checksec
- IOC
- YARA/YARA-X
- secrets
- API combos
- CWE
- ATT&CK
- MASVS
- SARIF
- deterministic verifier

Pipeline:

```text
Artifact
 ↓
Deterministic analyzers
 ↓
Run
 ↓
Evidence
 ↓
Finding
 ↓
Scorecard
 ↓
SARIF/report
```

---

## H2 — Capability / Policy / Context Kernel

Đây là lớp moat kỹ thuật.

Thành phần:

```text
CapabilityRegistry
PolicyEngine
ContextBroker
EvidenceResolver
Cache
Token Budget
Response aggregation
```

Mục tiêu:

> Không expose 100 tool nếu task chỉ cần 5 capability.

Ví dụ:

```text
Goal:
credential access investigation

Capabilities:
- get_binary_overview
- search_symbols
- get_function_summary
- get_xrefs
- resolve_evidence
```

Context được compile on-demand.

---

# 14. Token Optimization trong architecture mới

Không nên tối ưu token bằng cách “MCP schema ngắn” đơn thuần.

Nên có:

> **Context / Capability Compiler**

Ví dụ:

```text
Goal
 ↓
Skill
 ↓
Policy
 ↓
Capability subset
 ↓
Evidence handles
 ↓
Context budget
```

Raw evidence nằm trong store.

LLM chỉ lấy:

- summary
- relevant slice
- evidence handle

khi cần.

Ví dụ:

```text
resolve_evidence(E-441)
```

thay vì trả toàn bộ decompiler output.

Mục tiêu không được ghi 97%, 95%, 90%... nếu chưa benchmark.

Docs phải phân biệt:

```text
MEASURED
SLO/TARGET
HYPOTHESIS
```

---

# 15. H3 — Headless Runtime + MCP

Sau H0/H1/H2 mới expose platform.

Ví dụ:

```text
ChatGPT
Codex
Osaurus
Hermes
Goose
IDE
   ↓
MCP
   ↓
RE Platform
```

API/capability nên dựa trên explicit handles:

```text
project_id
case_id
run_id
artifact_id
evidence_id
finding_id
```

Không phụ thuộc “current MCP session”.

MCP session không phải canonical RE state.

---

# 16. H4 — Portable RE Package

Không phát minh skill format riêng nếu chuẩn Agent Skills/Agent Plugins đã đủ.

Package mục tiêu:

```text
re-platform-plugin/
├── plugin.json
├── mcp.json
└── skills/
    ├── packed-pe-analysis/
    │   └── SKILL.md
    ├── malware-ioc-investigation/
    │   └── SKILL.md
    └── android-re-analysis/
        └── SKILL.md
```

Skills phải progressive disclosure.

Tư duy:

> Skill thuộc project/platform, không thuộc riêng Air/Antigravity.

---

# 17. H5 — Deep RE Backends

Sau khi capability/kernel ổn định mới tích hợp:

- IDA
- Ghidra
- Binary Ninja
- JADX
- Capstone

Nguyên tắc:

```text
Internal Capability API
    ↓
IDA adapter
Ghidra adapter
Binja adapter
JADX adapter
...
```

Không thiết kế architecture bắt đầu từ “MCP server”.

MCP chỉ là projection của capability API.

---

# 18. H6 — RE Command Center Control Plane

Lúc này WinUI mới trở thành Command Center hoàn chỉnh.

UI:

- Project Workspace
- Cases
- Artifacts
- Runs
- Evidence timeline
- Findings
- Reports
- Agents
- External Sessions
- Evaluations

Nhưng domain/core vẫn tồn tại độc lập nếu UI không chạy.

---

# 19. H7 — Native RE Agent Runtime + ACP

Trước H7 nên để external agents làm harness.

H3-H6 sẽ cho chúng ta học:

- capability nào thật sự cần
- output nào khó hiểu
- tool nào lãng phí token
- policy nào cần
- evidence nào cần
- failure mode nào thực tế

Sau đó mới xây native RE Agent Runtime:

```text
IDE / Zed / ACP host
      ↓
     ACP
      ↓
RE Agent Runtime
      ↓
RE Platform Core
```

Mode thứ nhất:

```text
External Harness Mode
Host agent → MCP → RE Platform
```

Mode thứ hai:

```text
Native Agent Mode
IDE → ACP → RE Agent → RE Platform
```

---

# 20. H8 — Network / Dynamic / High-Risk

Sau khi Policy + Evidence ổn:

- Burp
- Frida
- WinDbg
- x64dbg
- MemProcFS
- dynamic execution
- attach/inject
- active network operations
- patch
- driver workflows

Tất cả có risk classification + approval.

---

# 21. H9+ — Evaluation / Federation / Scale

Có thể thêm:

- replay
- eval corpus
- token/cost metrics
- tool success metrics
- verifier pass rate
- batch/campaign
- plugin SDK
- vector search
- external session federation
- A2A nếu có use case thật

A2A không nên làm chỉ vì “đang có standard”.

---

# 22. Roadmap tổng hợp

```text
CURRENT
   │
PRE-H0
   │
   ▼
ARCHITECTURE RESET
   │
   ▼
H0 — Platform Foundation
   │
   ▼
H1 — Deterministic Product Lane
   │
   ▼
H2 — Capability / Policy / Context
   │
   ▼
H3 — Headless Runtime + MCP
   │
   ▼
H4 — Portable Skills + Agent Plugins
   │
   ▼
H5 — IDA/Ghidra/Deep RE
   │
   ▼
H6 — WinUI Command Center
   │
   ▼
H7 — Native RE Agent + ACP
   │
   ▼
H8 — Network/Dynamic/High Risk
   │
   ▼
H9+ — Eval/Federation/Scale/A2A
```

---

# 23. C# có còn là ngôn ngữ chính không?

Có.

Không rewrite core khỏi C#.

Định hướng:

```text
C#/.NET
    ↓
RE Core
Domain
Application
Policy
Evidence
Verifier
Capability
Context
Persistence
CLI
MCP
WinUI
```

Polyglot chỉ ở edge:

```text
IDA adapter       → Python/C++ tùy API
Ghidra adapter    → Java/Kotlin
Burp adapter      → Java/Kotlin
Frida             → JS/Python
Native perf       → C/C++/Rust nếu thực sự cần
ACP sidecar       → tùy ecosystem
```

Nguyên tắc:

> Polyglot at the boundary, C# in the core.

Không microservice hóa Core một cách vô ích.

---

# 24. YARA / YARA-X

Đã hỏi riêng sau khi trao đổi vision mới.

Kết luận:

**Vẫn cần YARA rules.**

Nhưng YARA không phải core identity của platform.

Nó là deterministic signature detection backend.

Nên có abstraction:

```text
ISignatureScanner
    ↓
YaraXSignatureScanner
```

Output:

```text
SignatureMatch
    ↓
Evidence
    ↓
Finding
```

Metadata provenance:

```text
rule_id
rule_pack
rule_pack_version
rule_pack_hash
scanner_version
artifact_hash
offset/location
timestamp
```

Không nên hard-code rule logic vào RE Core.

Rule sources nên chia:

```text
builtin/
external/
project-custom/
```

Không cần viết hàng nghìn rules ngay.

---

# 25. Nghiêng về YARA-X thay vì YARA classic

Lý do:

- YARA-X hiện là hướng phát triển mới của ecosystem VirusTotal.
- YARA classic vẫn tồn tại/được maintain nhưng feature development mới tập trung ở YARA-X.
- YARA-X có C API.
- Windows có thể build native DLL/C API.
- C# có thể P/Invoke wrapper.

Do đó:

```text
RE.Infrastructure.YaraX
      ↓
yara_x_capi.dll
```

hợp architecture C# hiện tại hơn:

```text
C#
 ↓
Python
 ↓
YARA
```

Nhưng đây chỉ là target mới cho roadmap sau PRE-H0, không được chèn lung tung vào PRE-H0.

---

# 26. Scorecard / YARA relationship

YARA match không tự động = “malware confirmed”.

Ví dụ:

```text
YARA:
VirtualAlloc +
WriteProcessMemory +
CreateRemoteThread
```

chỉ nên tạo:

```text
Suspicious API combination / signature evidence
```

Sau đó:

```text
IDA/Ghidra
 ↓
call graph
 ↓
function semantics
 ↓
verification
```

mới có thể kết luận sâu hơn.

Pipeline triết lý:

```text
deterministic detection
        ↓
     evidence
        ↓
semantic analysis
        ↓
verification
```

---

# 27. Các tài liệu cần sửa SAU PRE-H0

Không sửa ngay lúc này.

Sau PRE-H0 PASS, dự kiến rà lại:

```text
README.md
STRATEGY.md
ARCHITECTURE.md
DOMAIN_MODEL.md
INTEROPERABILITY.md
SECURITY.md
AGENTS.md
.agents/skills/*
```

Các file research:

```text
research_report.md
RE_AI_Agent_Knowledge_Base_2026-09-20.md
```

nên trở thành reference/research, không cạnh tranh vai trò canonical docs.

Phải phân biệt:

```text
CURRENT
TARGET
EXPERIMENTAL
HISTORICAL
```

như AGENTS.md hiện yêu cầu.

Không được để README target làm agent hiểu nhầm class/code đã tồn tại.

---

# 28. Bộ prompt mới sau Architecture Reset

Sau khi user duyệt roadmap mới:

Mỗi H sẽ có một bộ đầy đủ:

```text
H0
 ├─ IMPLEMENT
 ├─ FIRST REVIEW
 ├─ FIX
 └─ RE-REVIEW

H1
 ├─ IMPLEMENT
 ├─ FIRST REVIEW
 ├─ FIX
 └─ RE-REVIEW

...
```

Cho đến Hn/H9 theo roadmap được duyệt.

Workflow:

```text
Antigravity
   ↓
IMPLEMENT Hx
   ↓
build/tests
   ↓
Air/Codex
NEW FRESH REVIEW TASK
   ↓
findings
   ↓
Antigravity
SAME IMPLEMENTATION CONVERSATION
   ↓
FIX
   ↓
Air/Codex
NEW FRESH RE-REVIEW TASK
   ↓
PASS?
   ├─ NO → fix again
   └─ YES → human check/commit
```

Điểm quan trọng:

- Review là fresh context.
- Fix tiếp tục ở implementation conversation nếu context còn sạch.
- Không tạo task mới cho Antigravity chỉ vì mỗi lần fix, trừ khi cần.
- Air review có task/session mới cho mỗi vòng review.
- Không review bằng context cũ đã bị bias.

---

# 29. Air / Antigravity bối cảnh liên quan

Đã có nhiều trao đổi trước về JetBrains Air và Google Antigravity.

Các bài học architecture:

- Air phù hợp orchestration/review/task delegation.
- Fresh reviewer nên ở task/session riêng.
- Antigravity từng có vấn đề về session/history/integration.
- Có private Antigravity language-server/RPC path từng được thử nghiệm.
- Không coi private/undocumented Antigravity RPC là production integration.
- Official/supported interface nên được ưu tiên.
- Public product không nên dựa vào private OAuth/session implementation.

Do đó future RE Platform không nên phụ thuộc vào internal Antigravity API.

Antigravity/JetBrains/ChatGPT/etc nên là external hosts.

---

# 30. Tầm nhìn cuối cùng

Một cách nói ngắn gọn:

> **RE_Ecosystem không cần trở thành một AI coding app thứ N.**
>
> Nó nên trở thành **RE capability + evidence + context + policy platform** mà mọi AI agent/harness có thể sử dụng.

Hoặc:

```text
Any Agent
    ↓
RE Platform
    ↓
Any RE Tool
```

và:

```text
Any RE Tool
    ↓
RE Platform
    ↓
Any Agent
```

RE Command Center chỉ là:

```text
best native Windows control plane
```

chứ không phải điểm vào duy nhất.

---

# 31. Điều user cần làm NGAY BÂY GIỜ

Chỉ làm:

```text
PRE-H0
```

theo roadmap/prompt hiện tại.

Không:

- rewrite docs
- đổi H0
- thêm ACP
- thêm A2A
- thêm Agent Plugins
- rewrite core
- thêm Project Workspace
- thay YARA backend
- làm universal adapter

Cho tới khi:

```text
PRE-H0 = DONE + REVIEW PASS
```

Sau đó quay lại cuộc trao đổi này.

Tại thời điểm đó, công việc tiếp theo là:

```text
1. Inventory all .md
2. Architecture Reset
3. Chốt terminology
4. Viết canonical docs mới
5. Chốt H0-H9
6. Chốt acceptance criteria
7. Tạo bộ prompt IMPLEMENT/REVIEW/FIX/RE-REVIEW
```

---

# 32. Câu định vị canonical đề xuất

Bản đầy đủ:

> **RE_Ecosystem is a model-agnostic Reverse Engineering Agent Platform. It provides deterministic RE capabilities, project/case state, evidence/provenance, verification, policy and context optimization to any compatible LLM, agent or harness through interoperable interfaces such as MCP, Agent Skills/Plugins, CLI and ACP. RE Command Center is its first-party Windows-native control plane for projects, cases, evidence, agents and reports.**

Bản ngắn:

> **RE_Ecosystem = RE Agent Platform.**
>
> **RE Command Center = Windows Control Plane.**

---

# 33. Một lưu ý cực quan trọng

Không được giả định:

- “chưa ai làm”
- “97% token saving”
- “95% automatic detection”
- “<2.5s”
- “P/Invoke <1ms”
- “X% quality retention”

là fact nếu chưa benchmark.

Trong docs phải ghi:

```text
MEASURED
SLO/TARGET
HYPOTHESIS
```

---

# 34. Quyết định hiện tại / decision log

### Đã quyết định

- Giữ C#/.NET làm core.
- Giữ WinUI.
- Giữ deterministic triage.
- Không MCP hóa internal deterministic tools.
- Evidence/provenance là first-class.
- Policy/approval cho high-risk actions.
- Model/provider phải pluggable.
- External hosts dùng MCP.
- Full RE agent có thể dùng ACP sau này.
- Skills dùng progressive disclosure.
- YARA vẫn cần.
- Nghiêng YARA-X.
- PRE-H0 không đổi.
- Review/fix workflow hiện tại giữ nguyên.

### Đã đề xuất nhưng CHƯA IMPLEMENT / CHƯA FINAL APPROVAL

- RE Agent Platform positioning.
- Architecture Reset.
- H0–H9 roadmap mới.
- Project/Case/Run/Artifact/Evidence/Finding canonical model.
- Capability/Policy/Context kernel.
- portable Agent Plugin architecture.
- ACP RE Agent.
- external-session federation.
- A2A.
- YARA-X integration.

---

# 35. Trạng thái chính xác khi chuyển tài khoản

Current state:

```text
PRE-H0 đang được user tiếp tục review/fix.

User CHƯA yêu cầu sửa file MD.

User CHƯA approve để triển khai roadmap mới.

Vision mới đã được phân tích và đề xuất.

NEXT:
wait until user reports PRE-H0 DONE,
then perform Architecture Reset.
```