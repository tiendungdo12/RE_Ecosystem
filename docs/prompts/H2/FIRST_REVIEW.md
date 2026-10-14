# H2 FIRST REVIEW — independent / read-only

Milestone: **Core Capability / Policy / Context**. Dependencies: H0/H1 Core contracts; no agent-loop dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md). Use relevant skills: re-capability-mcp-policy, re-context-evidence; independent milestone reviews also use re-phase-review.

Use fresh reviewer context. Independently inspect the current diff, canonical consumers/composition and relevant checks against every deliverable/gate below. Review only; do not edit or implement fixes. Verify actual ownership and dependency paths, not names or metadata validators.

## Deliverables

- Core versioned capability registry/declarations/routing and typed risk/timeout/output contracts.
- Core backend policy/approval and invocation Evidence for API and projected consumers.
- Core EvidenceResolver/ContextBroker, bounded disclosure/slices and cache scope/version handling; keep agent conversation memory separate.

## Acceptance gate

- Backend denial/approval applies without our Runtime and cannot be bypassed by GUI, transport, model output or another adapter.
- Context retains resolvable source/verification/truncation metadata and respects Case/evidence/policy cache identity.
- No approval channel means no side-effect execution; cancellation/timeout/backend failures are explicit.

Report Critical → High → Medium → Low findings with verified file/line, concrete failure, impact and expected behavior. Separate missing acceptance evidence from defects. PASS requires the gate to be supported; no findings with unverified required scenarios is not a full milestone PASS.
