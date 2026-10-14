# H5 RE-REVIEW — fresh / read-only

Milestone: **Deep RE Adapters**. Dependencies: H0-H2 Core contracts and selected backend use case; H3 when externally exposed. Own Runtime is optional.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-capability-mcp-policy, re-scorecard-evidence; independent milestone reviews also use re-phase-review.

Use fresh reviewer context after fixes. Read all original findings, independently inspect the current diff/consumer paths and verify each correction plus regressions. Review only; no file edits. Check ownership, dependencies and acceptance evidence across the complete gate.

## Deliverables

- Scoped specialist RE backend adapters behind narrow Core capability contracts, using supported tool APIs/process interfaces.
- Backend/version/input/output/source provenance and resolvable artifacts for observations/findings.
- Owned-process lifecycle, cancellation/timeout/failure-isolation and unavailable-backend behavior.

## Acceptance gate

- Core adapter/use-case contracts do not become raw backend tool catalogs or require our agent loop.
- Unavailable/malformed/timed-out backend preserves valid partial data and cannot report verified successful analysis.
- All consumer paths keep Core policy and evidence recording; do not implement unrelated tools beyond the approved use case.

Report Critical → High → Medium → Low findings with verified file/line and concrete impact/expected behavior; reconcile every original finding. PASS requires all required deliverables/scenarios supported and no unresolved material issue. Report missing/weak evidence explicitly.
