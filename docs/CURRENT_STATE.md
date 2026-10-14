# Current Implementation Snapshot

> **Classification:** CURRENT source inspection and TARGET separation, 13 October 2026. PRE-H0 is DONE per user report; no fresh independent milestone PASS is claimed.

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

## Gaps

- Full canonical Core domain/lifecycle/storage, scorecard/SARIF/verifier and H2 policy/context services need implementation.
- Core composition still has mixed legacy/provider/protocol responsibilities; the target dependency boundary is not fully proven.
- H3 headless Platform host must prove access with WinUI and our Runtime absent.
- H6 ExternalSessionLink and manual GUI/Core integration remain TARGET; no transcript federation/control access is assumed.
- H7 optional own Runtime and ACP, H8 governed active adapters and H9 federation/evaluation remain TARGET unless their source/acceptance evidence is added.
- Host documentation describes viable integration routes, not tested RE_Ecosystem compatibility for every IDE/agent.

## Updating CURRENT

For an implemented claim, cite actual type/service, composition root, consumer path, persistence where relevant, and acceptance evidence matching that claim. Keep reported status, inspected source, runtime results and TARGET design separate. Do not equate model/provider availability with a completed native AI Harness.
