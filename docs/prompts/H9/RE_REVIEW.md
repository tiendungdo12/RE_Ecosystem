# H9 RE-REVIEW — fresh / read-only

Milestone: **Evaluation / Federation / Scale**. Dependencies: Implemented Core/host features under evaluation; treat own Runtime as an optional scenario, not baseline dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [CURRENT_STATE.md](../../CURRENT_STATE.md). Use relevant skills: re-phase-review, re-headless-interoperability; independent milestone reviews also use re-phase-review.

Use fresh reviewer context after fixes. Read all original findings, independently inspect the current diff/consumer paths and verify each correction plus regressions. Review only; no file edits. Check ownership, dependencies and acceptance evidence across the complete gate.

## Deliverables

- Reproducible eval corpus/method and scoped batching/scale work over implemented platform services.
- Measured quality/cost/latency/verifier/adapter metrics with limitations and configuration recorded.
- Justified deeper federation through supported host interfaces; consider A2A only for an explicit inter-agent use case.

## Acceptance gate

- Label metrics MEASURED, SLO/TARGET or HYPOTHESIS and preserve reproducible method/sample/configuration.
- No presumed external transcripts/live state/control or private provider RPC; H6 metadata links do not imply federation support.
- Evaluate Core-only/external-client and optional-runtime paths separately; no quality/privacy/latency guarantees without evidence.

Report Critical → High → Medium → Low findings with verified file/line and concrete impact/expected behavior; reconcile every original finding. PASS requires all required deliverables/scenarios supported and no unresolved material issue. Report missing/weak evidence explicitly.
