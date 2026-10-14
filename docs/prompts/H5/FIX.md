# H5 FIX — implementation context

Milestone: **Deep RE Adapters**. Dependencies: H0-H2 Core contracts and selected backend use case; H3 when externally exposed. Own Runtime is optional.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-capability-mcp-policy, re-scorecard-evidence; independent milestone reviews also use re-phase-review.

Read first-review findings and inspect the current source/diff. Continue in the implementation context, resolve every valid finding while preserving the complete intended deliverable and milestone scope. Give evidence for rejected/duplicate findings; do not shrink the goal to easier checks or expand future milestones.

## Deliverables

- Scoped specialist RE backend adapters behind narrow Core capability contracts, using supported tool APIs/process interfaces.
- Backend/version/input/output/source provenance and resolvable artifacts for observations/findings.
- Owned-process lifecycle, cancellation/timeout/failure-isolation and unavailable-backend behavior.

## Acceptance gate

- Core adapter/use-case contracts do not become raw backend tool catalogs or require our agent loop.
- Unavailable/malformed/timed-out backend preserves valid partial data and cannot report verified successful analysis.
- All consumer paths keep Core policy and evidence recording; do not implement unrelated tools beyond the approved use case.

Re-run checks affected by changes; report finding IDs resolved/rejected, source evidence, checks/results and remaining gaps. Update supported docs/status only. Do not claim independent re-review PASS.
