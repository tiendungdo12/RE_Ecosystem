# RE_Ecosystem Platform Roadmap

> **Status:** Canonical TARGET H0-H9 roadmap, terminology clarified 13 October 2026. [CURRENT_STATE](docs/CURRENT_STATE.md) records actual source. PRE-H0 remains DONE per user report.

## Product definition and source chain

**RE Platform = RE Platform Core + protocol adapters + optional RE Agent Runtime (AI Harness). RE Command Center is the optional Windows control-plane product using Platform.**

Core owns domain/state, persistence, Evidence/Finding, Capability/Policy, Verification, Context/Cache, deterministic analysis and tool adapters. Harness means the optional H7 planner/model/agent-loop runtime, not Core application use cases or a required state layer.

The [original vision](New_Strategy.md) and [following terminology clarification](docs/reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md) form the architecture input chain. The clarification governs ambiguous Core/Harness wording; [decision log](docs/ARCHITECTURE_RESET.md) records the correction.

Logical Platform Core spans Domain/Application/Infrastructure and adapters. The existing assembly `RE.Core` is a narrower implementation slice with transitional responsibilities. Keep current project names and migrate incrementally; no source-tree move or `RE.Harness` project is required at H0.

## Milestones and acceptance gates

| Milestone | Scope/deliverable | Acceptance gate |
|---|---|---|
| **PRE-H0 (DONE per user)** | Preserve direct deterministic triage and its regression coverage. | No retroactive scope expansion or assumed fresh review PASS. |
| **H0 — Platform Foundation** | Canonical Project/Case/Artifact/Run/Evidence/Finding identities, lifecycle and Core repository ports; versioned persistence and legacy-data migration contract. | Core state/use cases/persistence work without LLM, Agent Runtime, MCP transport or GUI. Domain types stay pure; DB migration preserves recoverable legacy data. |
| **H1 — Core Deterministic Lane** | Connect PRE-H0 triage to canonical records; scoped deterministic analyses, findings, scorecard, mappings, verifier and SARIF. Evaluate scanner/rule compatibility for YARA-X. | No LLM/internal MCP route; cancellation/partial failures explicit; evidence-linked important Findings; required missing evidence blocks accepted completion; deterministic export; preserve PRE-H0 behavior. |
| **H2 — Core Capability/Policy/Context** | Versioned declarations/registry, backend policy and approval, EvidenceResolver, bounded ContextBroker/cache and disclosure controls. | Core enforces every consumer path without an agent loop. Denial/approval cannot be bypassed; context keeps source handles and distinguishes truncation/cache scope. |
| **H3 — Headless Platform Host** | CLI/MCP server composes Core application services, repositories and adapters; explicit canonical handles; documented local startup/stop/reconnect. | Launch and execute a Case/Run/Evidence operation with both WinUI and our Agent Runtime absent. CLI/MCP call the same Core services/policy; no model required by server. |
| **H4 — Portable Skills/Host Packages** | Standard workflow `SKILL.md` packages and host-specific configs for verified compatible clients. | Skills can guide external agents without our H7 Runtime; no permission authority or universal plugin-manifest assumption; tested client/version/transport matrix and no committed credentials. |
| **H5 — Deep RE Adapters** | Scoped specialist backends (IDA/Ghidra/Binary Ninja/JADX/Capstone) behind Core capability contracts. | Proven backend/version/source Evidence, cancellation/timeout and failure isolation; no raw catalog leakage or false success when a backend is missing. |
| **H6 — Optional Command Center** | Manual/deterministic views/actions over Core state; Projects/Cases/Runs/Evidence/Findings/reports/policies; supplied ExternalSessionLink metadata/status/artifacts. | GUI works without our Runtime; GUI/headless use the same Core records. A session link grants neither transcript ownership nor remote-control access. Respect UI/theme/accessibility/threading rules. |
| **H7 — Optional Agent Runtime / ACP** | Separate planner/model router/agent loop, skill/goal execution, budgets/memory/retries and justified subagents; ACP adapter for compatible IDEs. | Runtime consumes Core policy/evidence/context APIs; agent memory is not canonical state. Disable/remove Runtime without losing Core data, manual GUI or external MCP/CLI access. Validate ACP separately. |
| **H8 — Core-governed Active Analysis** | Scoped network/dynamic/debugger/memory/system-changing adapters with risk, ownership, timeout and approval. | Backend Core enforcement covers direct/routed calls from GUI/external/own agents; required approval precedes side effects; audit provenance and cancellation/failure behavior. |
| **H9+ — Evaluation/Federation/Scale** | Reproducible evals, quality/cost/latency metrics and justified deeper external-session federation/batching; A2A only for a demonstrated need. | Corpus/method/limits accompany metrics; supported host interfaces/data availability are explicit; no assumed transcript/control access or unmeasured guarantees. |

## Required independence scenarios

1. **Core only:** Artifact → deterministic analysis → persisted Run/Evidence/Finding → verification/Case state with no LLM, Agent Runtime, MCP transport or UI.
2. **External agent:** MCP/CLI accesses Core via H3 Platform host while WinUI and own Runtime are absent; explicit IDs and backend policy remain authoritative.
3. **GUI/manual:** H6 shows the same Core records and runs manual/deterministic workflows without an agent loop.
4. **Optional Runtime:** H7 uses Core services; disabling it preserves state and other consumer paths.
5. **External sessions:** H6 stores/display supplied host/agent/session/case/last-seen and available summaries/artifacts; no guaranteed transcript, live status or control. H9 federation is separate.

These are future implementation gates. A documentation diagram, link check, compilation or model/provider connection does not prove them.

## Shared rules

- Domain invariants stay pure; Core application services own use cases/policy, Infrastructure implements ports. Protocol adapters translate requests at the edge; they do not own state.
- Runtime/GUI/external clients depend on Core. Core essential services do not depend on our Runtime, model SDK or GUI; Core library analysis does not require MCP.
- Tool/model/binary/network/session-import text is untrusted. Findings need provenance and explicit verification; Run outcome is separate.
- Core controls minimal disclosure and evidence context. Runtime/external harness controls planning/conversation/model budgets.
- Preserve PRE-H0, optional CPU-only operation and supported-interface integration. GNN/local LLM/GPU/A2A remain optional.
- Maintain CURRENT/TARGET/EXPERIMENTAL/HISTORICAL and MEASURED/SLO/TARGET/HYPOTHESIS.

## Prompt workflow and sources of truth

[Prompt packs](docs/prompts/README.md) contain IMPLEMENT, FIRST_REVIEW, FIX and RE_REVIEW for each H0-H9. Each names concrete deliverables, relevant contracts and the same milestone gate. Fresh first/re-review is read-only; fix stays in the implementation context. PRE-H0 keeps its original prompts.

[Documentation Index](docs/DOCUMENTATION_INDEX.md) links canonical architecture/domain/security/interoperability/current docs, project guides, eight RE skills and the source chain. Historical transcripts preserve decisions; they are not inventories of implemented types.
