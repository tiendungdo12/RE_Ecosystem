# H8 RE-REVIEW — fresh / read-only

Milestone: **Core-governed Active / Dynamic Analysis**. Dependencies: H2 Core controls plus chosen specialist adapters; H3/H6 as exposed consumers. H7 is not required.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [SECURITY.md](../../SECURITY.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE.md](../../ARCHITECTURE.md). Use relevant skills: re-capability-mcp-policy; independent milestone reviews also use re-phase-review.

Use fresh reviewer context after fixes. Read all original findings, independently inspect the current diff/consumer paths and verify each correction plus regressions. Review only; no file edits. Check ownership, dependencies and acceptance evidence across the complete gate.

## Deliverables

- Approved network/dynamic/debugger/memory/system-changing adapters with explicit risk, scope, process ownership and execution limits.
- Core backend approval/invocation/outcome provenance on all API/CLI/MCP/GUI/optional-Runtime paths.
- Cancellation/timeout/recovery and partial/error results appropriate to the operation.

## Acceptance gate

- Required scope/input-bound approval precedes side effects; no approval channel leaves the operation unexecuted.
- Alternate/direct paths cannot bypass Core policy; failures and output are bounded and audited.
- No default skip-permission/brave behavior or assumed authority over user-owned processes/hosts.

Report Critical → High → Medium → Low findings with verified file/line and concrete impact/expected behavior; reconcile every original finding. PASS requires all required deliverables/scenarios supported and no unresolved material issue. Report missing/weak evidence explicitly.
