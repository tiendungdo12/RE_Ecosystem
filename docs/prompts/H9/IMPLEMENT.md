# H9 IMPLEMENT

Milestone: **Evaluation / Federation / Scale**. Dependencies: Implemented Core/host features under evaluation; treat own Runtime as an optional scenario, not baseline dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [CURRENT_STATE.md](../../CURRENT_STATE.md). Use relevant skills: re-phase-review, re-headless-interoperability; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Reproducible eval corpus/method and scoped batching/scale work over implemented platform services.
- Measured quality/cost/latency/verifier/adapter metrics with limitations and configuration recorded.
- Justified deeper federation through supported host interfaces; consider A2A only for an explicit inter-agent use case.

## Acceptance gate

- Label metrics MEASURED, SLO/TARGET or HYPOTHESIS and preserve reproducible method/sample/configuration.
- No presumed external transcripts/live state/control or private provider RPC; H6 metadata links do not imply federation support.
- Evaluate Core-only/external-client and optional-runtime paths separately; no quality/privacy/latency guarantees without evidence.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
