# H7 IMPLEMENT

Milestone: **Optional RE Agent Runtime / AI Harness / ACP**. Dependencies: Stable H0-H3 Core APIs and H3-H6 observed use cases. Core/GUI/external access must remain independent.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-capability-mcp-policy, re-context-evidence; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Separate optional RE Agent Runtime: planner, model routing and agent loop, skill/goal execution, retries/budgets/memory and justified subagent support.
- Runtime integration consuming Core capability/policy/evidence/verification/context APIs; agent planning/conversation state remains separate.
- ACP adapter and setup/launch/lifecycle for verified compatible IDE clients; optional Command Center agent-action client integration.

## Acceptance gate

- Disable/remove Runtime and its provider config without losing Core state, manual GUI or external MCP/CLI operation.
- Agent tool calls cannot bypass Core policy or promote model text to verified Findings; stopping/budget conditions are explicit.
- Validate actual ACP client/runtime version/config and failure/cancellation behavior independently of H3 MCP.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
