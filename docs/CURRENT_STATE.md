# Current Implementation Snapshot

> **Classification:** CURRENT source inspection and TARGET separation, 14 October 2026. PRE-H0 remains DONE per user report; fresh publication regression evidence is linked below, without claiming H0–H9 milestone acceptance.

## Component status

| Component | Source evidence | Status |
|---|---|---|
| Logical RE Platform Core | `RE.Domain` models; `RE.Core` pipeline/wrappers plus legacy MCP/routing; `RE.Application` search/model/usage use cases; `RE.Infrastructure` KnowledgeStore and providers/transports | Existing platform slices; full canonical investigation/policy/evidence services are TARGET |
| RE Agent Runtime / AI Harness | Existing chat/provider/bridge code is present, but the solution has no dedicated `RE.AgentRuntime` project | Full optional H7 planner/agent runtime/ACP is not established |
| RE Command Center | `RE.WinUI` executable and `RE.Presentation` project | Existing UI; H6 canonical control plane and ExternalSessionLink views are TARGET |
| Independent Platform host | Legacy `mcp_server` exists; no dedicated H3 Core host/CLI in solution | H3 service exposure is TARGET; legacy tool server is not proof of platform integration |

Logical Platform Core spans projects; it is not solely the assembly named `RE.Core`. Do not label Application/Infrastructure as AI Harness merely because they orchestrate deterministic state/tool operations.

## PRE-H0 and storage evidence

Source has `ITriagePipeline.RunTriageAsync`, direct/native `TriageService`, subprocess analysis, cancellation/partial-failure handling, and tests including `PeTriage_CanonicalPath_SucceedsWithoutMcpOrLlm` and `AntiRegression_McpStaticTriageRoute_IsBlocked`. `TriageOrchestrator` is obsolete and delegates to the pipeline. These inspected facts support the reported baseline; they do not independently prove every prior review/test gate.

`KnowledgeStore.ResolveDatabasePath()` uses solution-root `knowledge.db`; tables include `Projects`, `ChatSessions`, `ChatHistory`, `TriageHistory`. Canonical Case/Run/Evidence/Finding persistence and migration remain TARGET. `direct_triage.py` is used as a direct subprocess runner; legacy MCP reference code does not turn triage into an agent loop.

## Publication safeguards and measured checks

- `ISecretStore` and Windows `DpapiSecretStore` protect API credentials with CurrentUser DPAPI. `UserSettingsStore` serializes atomic preference writes and migrates plaintext key fields into references. The WinUI settings service uses per-user storage and reports errors without a plaintext fallback.
- `PreH0ToolPolicy` denies unknown/active calls before execution. Manager, executor and legacy transport apply it; raw client creation is internal, and `/md5` uses the guarded manager. This temporary read-only baseline does not implement scope-bound approvals, audit Evidence or the full H2 registry.
- Default settings path is `%LOCALAPPDATA%/RE_Ecosystem/Settings/`. The explicit developer/test `RE_ECOSYSTEM_USER_DATA_ROOT` isolates settings and legacy DB; the default root database/history are unchanged.
- `scripts/Setup-Runtime.ps1` provisions hash-locked Windows x64 CPython/wheels and a pinned DIE database. Repeated setup and bad-hash rejection were checked. Native source refs are supplied, not independently rebuilt.
- [Publication validation](VALIDATION_PRE_H0.md): 85 .NET tests, 56 Python probes, four isolated local UI assertions, WinUI x64 and SecondUI builds. Clean-candidate checks used published payload and a newly provisioned runtime. Warnings remain; no AOT, full accessibility or H3 acceptance is claimed.

## Gaps

- Full canonical Core domain/lifecycle/storage, scorecard/SARIF/verifier and H2 policy/context services need implementation.
- Core composition still has mixed legacy/provider/protocol responsibilities; the target dependency boundary is not fully proven.
- H3 headless Platform host must prove access with WinUI and our Runtime absent.
- H6 ExternalSessionLink and manual GUI/Core integration remain TARGET; no transcript federation/control access is assumed.
- H7 optional own Runtime and ACP, H8 governed active adapters and H9 federation/evaluation remain TARGET unless their source/acceptance evidence is added.
- Host documentation describes viable integration routes, not tested RE_Ecosystem compatibility for every IDE/agent.

## Updating CURRENT

For an implemented claim, cite actual type/service, composition root, consumer path, persistence where relevant, and acceptance evidence matching that claim. Keep reported status, inspected source, runtime results and TARGET design separate. Do not equate model/provider availability with a completed native AI Harness.
