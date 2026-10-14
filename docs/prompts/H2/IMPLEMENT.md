# H2 IMPLEMENT

Milestone: **Core Capability / Policy / Context**. Dependencies: H0/H1 Core contracts; no agent-loop dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md). Use relevant skills: re-capability-mcp-policy, re-context-evidence; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Core versioned capability registry/declarations/routing and typed risk/timeout/output contracts.
- Core backend policy/approval and invocation Evidence for API and projected consumers.
- Core EvidenceResolver/ContextBroker, bounded disclosure/slices and cache scope/version handling; keep agent conversation memory separate.

## Acceptance gate

- Backend denial/approval applies without our Runtime and cannot be bypassed by GUI, transport, model output or another adapter.
- Context retains resolvable source/verification/truncation metadata and respects Case/evidence/policy cache identity.
- No approval channel means no side-effect execution; cancellation/timeout/backend failures are explicit.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
