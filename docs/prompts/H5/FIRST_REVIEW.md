# H5 FIRST REVIEW — independent / read-only

Milestone: **Deep RE Adapters**. Dependencies: H0-H2 Core contracts and selected backend use case; H3 when externally exposed. Own Runtime is optional.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-capability-mcp-policy, re-scorecard-evidence; independent milestone reviews also use re-phase-review.

Use fresh reviewer context. Independently inspect the current diff, canonical consumers/composition and relevant checks against every deliverable/gate below. Review only; do not edit or implement fixes. Verify actual ownership and dependency paths, not names or metadata validators.

## Deliverables

- Scoped specialist RE backend adapters behind narrow Core capability contracts, using supported tool APIs/process interfaces.
- Backend/version/input/output/source provenance and resolvable artifacts for observations/findings.
- Owned-process lifecycle, cancellation/timeout/failure-isolation and unavailable-backend behavior.

## Acceptance gate

- Core adapter/use-case contracts do not become raw backend tool catalogs or require our agent loop.
- Unavailable/malformed/timed-out backend preserves valid partial data and cannot report verified successful analysis.
- All consumer paths keep Core policy and evidence recording; do not implement unrelated tools beyond the approved use case.

Report Critical → High → Medium → Low findings with verified file/line, concrete failure, impact and expected behavior. Separate missing acceptance evidence from defects. PASS requires the gate to be supported; no findings with unverified required scenarios is not a full milestone PASS.
