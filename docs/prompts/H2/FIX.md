# H2 FIX — implementation context

Milestone: **Core Capability / Policy / Context**. Dependencies: H0/H1 Core contracts; no agent-loop dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md). Use relevant skills: re-capability-mcp-policy, re-context-evidence; independent milestone reviews also use re-phase-review.

Read first-review findings and inspect the current source/diff. Continue in the implementation context, resolve every valid finding while preserving the complete intended deliverable and milestone scope. Give evidence for rejected/duplicate findings; do not shrink the goal to easier checks or expand future milestones.

## Deliverables

- Core versioned capability registry/declarations/routing and typed risk/timeout/output contracts.
- Core backend policy/approval and invocation Evidence for API and projected consumers.
- Core EvidenceResolver/ContextBroker, bounded disclosure/slices and cache scope/version handling; keep agent conversation memory separate.

## Acceptance gate

- Backend denial/approval applies without our Runtime and cannot be bypassed by GUI, transport, model output or another adapter.
- Context retains resolvable source/verification/truncation metadata and respects Case/evidence/policy cache identity.
- No approval channel means no side-effect execution; cancellation/timeout/backend failures are explicit.

Re-run checks affected by changes; report finding IDs resolved/rejected, source evidence, checks/results and remaining gaps. Update supported docs/status only. Do not claim independent re-review PASS.
