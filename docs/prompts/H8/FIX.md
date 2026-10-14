# H8 FIX — implementation context

Milestone: **Core-governed Active / Dynamic Analysis**. Dependencies: H2 Core controls plus chosen specialist adapters; H3/H6 as exposed consumers. H7 is not required.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [SECURITY.md](../../SECURITY.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE.md](../../ARCHITECTURE.md). Use relevant skills: re-capability-mcp-policy; independent milestone reviews also use re-phase-review.

Read first-review findings and inspect the current source/diff. Continue in the implementation context, resolve every valid finding while preserving the complete intended deliverable and milestone scope. Give evidence for rejected/duplicate findings; do not shrink the goal to easier checks or expand future milestones.

## Deliverables

- Approved network/dynamic/debugger/memory/system-changing adapters with explicit risk, scope, process ownership and execution limits.
- Core backend approval/invocation/outcome provenance on all API/CLI/MCP/GUI/optional-Runtime paths.
- Cancellation/timeout/recovery and partial/error results appropriate to the operation.

## Acceptance gate

- Required scope/input-bound approval precedes side effects; no approval channel leaves the operation unexecuted.
- Alternate/direct paths cannot bypass Core policy; failures and output are bounded and audited.
- No default skip-permission/brave behavior or assumed authority over user-owned processes/hosts.

Re-run checks affected by changes; report finding IDs resolved/rejected, source evidence, checks/results and remaining gaps. Update supported docs/status only. Do not claim independent re-review PASS.
