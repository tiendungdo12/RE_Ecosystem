# H9 FIRST REVIEW — independent / read-only

Milestone: **Evaluation / Federation / Scale**. Dependencies: Implemented Core/host features under evaluation; treat own Runtime as an optional scenario, not baseline dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [CURRENT_STATE.md](../../CURRENT_STATE.md). Use relevant skills: re-phase-review, re-headless-interoperability; independent milestone reviews also use re-phase-review.

Use fresh reviewer context. Independently inspect the current diff, canonical consumers/composition and relevant checks against every deliverable/gate below. Review only; do not edit or implement fixes. Verify actual ownership and dependency paths, not names or metadata validators.

## Deliverables

- Reproducible eval corpus/method and scoped batching/scale work over implemented platform services.
- Measured quality/cost/latency/verifier/adapter metrics with limitations and configuration recorded.
- Justified deeper federation through supported host interfaces; consider A2A only for an explicit inter-agent use case.

## Acceptance gate

- Label metrics MEASURED, SLO/TARGET or HYPOTHESIS and preserve reproducible method/sample/configuration.
- No presumed external transcripts/live state/control or private provider RPC; H6 metadata links do not imply federation support.
- Evaluate Core-only/external-client and optional-runtime paths separately; no quality/privacy/latency guarantees without evidence.

Report Critical → High → Medium → Low findings with verified file/line, concrete failure, impact and expected behavior. Separate missing acceptance evidence from defects. PASS requires the gate to be supported; no findings with unverified required scenarios is not a full milestone PASS.
