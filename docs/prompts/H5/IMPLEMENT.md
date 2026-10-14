# H5 IMPLEMENT

Milestone: **Deep RE Adapters**. Dependencies: H0-H2 Core contracts and selected backend use case; H3 when externally exposed. Own Runtime is optional.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-capability-mcp-policy, re-scorecard-evidence; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Scoped specialist RE backend adapters behind narrow Core capability contracts, using supported tool APIs/process interfaces.
- Backend/version/input/output/source provenance and resolvable artifacts for observations/findings.
- Owned-process lifecycle, cancellation/timeout/failure-isolation and unavailable-backend behavior.

## Acceptance gate

- Core adapter/use-case contracts do not become raw backend tool catalogs or require our agent loop.
- Unavailable/malformed/timed-out backend preserves valid partial data and cannot report verified successful analysis.
- All consumer paths keep Core policy and evidence recording; do not implement unrelated tools beyond the approved use case.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
