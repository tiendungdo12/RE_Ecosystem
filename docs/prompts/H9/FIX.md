# H9 FIX — implementation context

Milestone: **Evaluation / Federation / Scale**. Dependencies: Implemented Core/host features under evaluation; treat own Runtime as an optional scenario, not baseline dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [CURRENT_STATE.md](../../CURRENT_STATE.md). Use relevant skills: re-phase-review, re-headless-interoperability; independent milestone reviews also use re-phase-review.

Read first-review findings and inspect the current source/diff. Continue in the implementation context, resolve every valid finding while preserving the complete intended deliverable and milestone scope. Give evidence for rejected/duplicate findings; do not shrink the goal to easier checks or expand future milestones.

## Deliverables

- Reproducible eval corpus/method and scoped batching/scale work over implemented platform services.
- Measured quality/cost/latency/verifier/adapter metrics with limitations and configuration recorded.
- Justified deeper federation through supported host interfaces; consider A2A only for an explicit inter-agent use case.

## Acceptance gate

- Label metrics MEASURED, SLO/TARGET or HYPOTHESIS and preserve reproducible method/sample/configuration.
- No presumed external transcripts/live state/control or private provider RPC; H6 metadata links do not imply federation support.
- Evaluate Core-only/external-client and optional-runtime paths separately; no quality/privacy/latency guarantees without evidence.

Re-run checks affected by changes; report finding IDs resolved/rejected, source evidence, checks/results and remaining gaps. Update supported docs/status only. Do not claim independent re-review PASS.
