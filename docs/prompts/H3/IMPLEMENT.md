# H3 IMPLEMENT

Milestone: **Headless Platform Host / CLI / MCP**. Dependencies: H0-H2 Core services/policy; explicitly exclude H7 Runtime and WinUI.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-headless-interoperability, re-capability-mcp-policy; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Independent Platform host/CLI/MCP server composition over the same Core application services, persistence and adapters used by other clients.
- High-level typed Core capability projections with explicit canonical handles and bounded responses.
- Local stdio startup/working-directory/log/stop/reconnect documentation and tested client/agent/version/transport/settings compatibility.

## Acceptance gate

- With both WinUI and our Agent Runtime absent, launch the Platform host and create/read a Case/Run and retrieve Evidence; server requires no LLM.
- CLI/MCP preserve Core policy/provenance and state, including denial/approval, timeout/cancel/disconnect outcomes.
- Protocol sessions do not own Cases; no log text on stdio protocol output and no untested Rider/VS Code support claim.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
