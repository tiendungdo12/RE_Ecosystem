# H3 RE-REVIEW — fresh / read-only

Milestone: **Headless Platform Host / CLI / MCP**. Dependencies: H0-H2 Core services/policy; explicitly exclude H7 Runtime and WinUI.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-headless-interoperability, re-capability-mcp-policy; independent milestone reviews also use re-phase-review.

Use fresh reviewer context after fixes. Read all original findings, independently inspect the current diff/consumer paths and verify each correction plus regressions. Review only; no file edits. Check ownership, dependencies and acceptance evidence across the complete gate.

## Deliverables

- Independent Platform host/CLI/MCP server composition over the same Core application services, persistence and adapters used by other clients.
- High-level typed Core capability projections with explicit canonical handles and bounded responses.
- Local stdio startup/working-directory/log/stop/reconnect documentation and tested client/agent/version/transport/settings compatibility.

## Acceptance gate

- With both WinUI and our Agent Runtime absent, launch the Platform host and create/read a Case/Run and retrieve Evidence; server requires no LLM.
- CLI/MCP preserve Core policy/provenance and state, including denial/approval, timeout/cancel/disconnect outcomes.
- Protocol sessions do not own Cases; no log text on stdio protocol output and no untested Rider/VS Code support claim.

Report Critical → High → Medium → Low findings with verified file/line and concrete impact/expected behavior; reconcile every original finding. PASS requires all required deliverables/scenarios supported and no unresolved material issue. Report missing/weak evidence explicitly.
