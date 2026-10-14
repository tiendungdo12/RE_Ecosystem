# H6 FIX — implementation context

Milestone: **Optional Command Center / External Session Links**. Dependencies: H0-H3 Core state/host; H5 for selected specialist views. No H7 Runtime requirement.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md). Use relevant skills: re-platform-foundation, re-headless-interoperability; independent milestone reviews also use re-phase-review.

Read first-review findings and inspect the current source/diff. Continue in the implementation context, resolve every valid finding while preserving the complete intended deliverable and milestone scope. Give evidence for rejected/duplicate findings; do not shrink the goal to easier checks or expand future milestones.

## Deliverables

- Optional WinUI manual/deterministic control plane over Core Projects/Cases/Artifacts/Runs/Evidence/Findings/reports/policies.
- TARGET ExternalSessionLink storage/views: supplied host, agent, opaque external session ID, Case reference, last seen and available summary/artifact references.
- Supported optional agent actions/status interfaces with explicit availability; keep planner/model/agent loop outside the UI.

## Acceptance gate

- Manual GUI workflows function without our Runtime and observe the same Core records as a headless client.
- Links do not grant transcript ownership, live-running proof or external-control rights; imports retain untrusted/provenance status.
- Validate applicable WinUI theme/accessibility/threading/lifecycle requirements; deeper federation remains H9.

Re-run checks affected by changes; report finding IDs resolved/rejected, source evidence, checks/results and remaining gaps. Update supported docs/status only. Do not claim independent re-review PASS.
