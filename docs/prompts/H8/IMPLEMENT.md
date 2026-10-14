# H8 IMPLEMENT

Milestone: **Core-governed Active / Dynamic Analysis**. Dependencies: H2 Core controls plus chosen specialist adapters; H3/H6 as exposed consumers. H7 is not required.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [SECURITY.md](../../SECURITY.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE.md](../../ARCHITECTURE.md). Use relevant skills: re-capability-mcp-policy; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Approved network/dynamic/debugger/memory/system-changing adapters with explicit risk, scope, process ownership and execution limits.
- Core backend approval/invocation/outcome provenance on all API/CLI/MCP/GUI/optional-Runtime paths.
- Cancellation/timeout/recovery and partial/error results appropriate to the operation.

## Acceptance gate

- Required scope/input-bound approval precedes side effects; no approval channel leaves the operation unexecuted.
- Alternate/direct paths cannot bypass Core policy; failures and output are bounded and audited.
- No default skip-permission/brave behavior or assumed authority over user-owned processes/hosts.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
