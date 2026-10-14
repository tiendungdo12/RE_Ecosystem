# Architecture Reset Decision Log

> **Status:** Approved terminology correction for the existing H0-H9 roadmap, 13 October 2026. This records TARGET ownership and migration; it does not claim implementation.

## Architecture source chain

The vision comes from [New_Strategy.md](../New_Strategy.md) and its following conversation, preserved **verbatim** in [Architecture terminology clarification](reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md). The user identified that continuation as the precise Core/Harness/Command Center definition. Its clarification takes precedence over ambiguous earlier wording.

The previous documentation incorrectly called Core state/policy/application services “RE Harness”. That interpretation is superseded here. **Harness now means optional RE Agent Runtime (H7), never the required H0-H6 state/policy layer.** Historical/source transcripts are decision input, not current source inventories.

## Product decision

RE Platform consists of **RE Platform Core + protocol adapters + optional RE Agent Runtime**. RE Command Center is the optional Windows control-plane product using Platform. Core owns state, persistence, evidence, policy, capability, verification, context/cache, deterministic analysis and tool adapters.

External agents use their own harness and reach Core through the H3 Platform host/MCP server. They do not require our Runtime or Command Center. A Platform host process/needed adapter dependencies still have to be available for remote/client transport; Core library use requires no MCP.

## Decisions

| ID | Decision | Consequence |
|---|---|---|
| ADR-R0-01 | PRE-H0 is DONE per user report; preserve scope | No retroactive feature expansion |
| ADR-R0-02 (clarified) | Core owns platform services; Harness means optional Agent Runtime | Remove Harness ownership of state/policy/persistence from H0-H6 |
| ADR-R0-03 | Keep incremental migration on current solution | Logical Core spans Domain/Application/Infrastructure/adapters; no immediate namespace/project moves |
| ADR-R0-04 | Deterministic lane and Run/Evidence persistence are Core functions | No agent loop, LLM or internal MCP route required |
| ADR-R0-05 | H3 is a headless Platform host, not AI Harness | External clients use explicit canonical handles without H7 Runtime |
| ADR-R0-06 | Skills/host packages and own-agent execution are distinct | H4 portable knowledge; H7 optional Runtime |
| ADR-R0-07 | Command Center is a client with optional agent interactions | Manual/deterministic workflow needs no AI Harness |
| ADR-R0-08 | ExternalSessionLink stores supplied metadata only | H6 links/status/artifacts; no assumed transcript/control; deeper federation H9 |

## Migration map

| Responsibility | Target owner | Gate |
|---|---|---|
| Domain/state/lifecycle and persistence ports | Platform Core Domain/Application | H0 |
| SQLite and migration implementations | Platform Core Infrastructure | H0 |
| Deterministic analysis, findings/scorecard/verifier | Platform Core | PRE-H0 baseline / H1 |
| Capability/policy/context/evidence resolver | Platform Core | H2 |
| CLI/MCP composition | Platform host / protocol adapters | H3 |
| Workflow skills and compatible client packages | Client integration / Core capability consumers | H4 |
| Specialist RE adapters | Core adapters | H5/H8 |
| GUI/manual workflows and ExternalSessionLink views | Optional Command Center using Core | H6 |
| Planner/model/agent loop, goals/subagents, agent memory | Optional RE Agent Runtime = AI Harness | H7 |
| Evaluation and justified federation | Platform services / consumer integrations | H9 |

The source tree suggested in the continuation is an organization proposal. Existing `RE.Core` is not the whole logical Platform Core. `RE.AgentRuntime` is the proposed optional-runtime component name; this edit creates no project or code.

## Compatibility and proof

Core must analyze/store/verify without UI, Runtime, model or MCP transport. H3 must serve clients with both WinUI and Runtime absent. H6 must show the same Core state and allow manual/deterministic work without an agent. H7 must be removable without losing Core data or external-agent access. Validate Rider/VS Code and ACP separately per supported agent/version/config.

Back up/version/preserve existing `knowledge.db`; imported historical records with missing provenance stay historical/incomplete. This documentation change does not migrate data or create schema.
