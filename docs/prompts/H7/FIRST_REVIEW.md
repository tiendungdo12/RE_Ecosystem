# H7 FIRST REVIEW — independent / read-only

Milestone: **Optional RE Agent Runtime / AI Harness / ACP**. Dependencies: Stable H0-H3 Core APIs and H3-H6 observed use cases. Core/GUI/external access must remain independent.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-capability-mcp-policy, re-context-evidence; independent milestone reviews also use re-phase-review.

Use fresh reviewer context. Independently inspect the current diff, canonical consumers/composition and relevant checks against every deliverable/gate below. Review only; do not edit or implement fixes. Verify actual ownership and dependency paths, not names or metadata validators.

## Deliverables

- Separate optional RE Agent Runtime: planner, model routing and agent loop, skill/goal execution, retries/budgets/memory and justified subagent support.
- Runtime integration consuming Core capability/policy/evidence/verification/context APIs; agent planning/conversation state remains separate.
- ACP adapter and setup/launch/lifecycle for verified compatible IDE clients; optional Command Center agent-action client integration.

## Acceptance gate

- Disable/remove Runtime and its provider config without losing Core state, manual GUI or external MCP/CLI operation.
- Agent tool calls cannot bypass Core policy or promote model text to verified Findings; stopping/budget conditions are explicit.
- Validate actual ACP client/runtime version/config and failure/cancellation behavior independently of H3 MCP.

Report Critical → High → Medium → Low findings with verified file/line, concrete failure, impact and expected behavior. Separate missing acceptance evidence from defects. PASS requires the gate to be supported; no findings with unverified required scenarios is not a full milestone PASS.
