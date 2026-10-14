# H6 IMPLEMENT

Milestone: **Optional Command Center / External Session Links**. Dependencies: H0-H3 Core state/host; H5 for selected specialist views. No H7 Runtime requirement.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md). Use relevant skills: re-platform-foundation, re-headless-interoperability; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Optional WinUI manual/deterministic control plane over Core Projects/Cases/Artifacts/Runs/Evidence/Findings/reports/policies.
- TARGET ExternalSessionLink storage/views: supplied host, agent, opaque external session ID, Case reference, last seen and available summary/artifact references.
- Supported optional agent actions/status interfaces with explicit availability; keep planner/model/agent loop outside the UI.

## Acceptance gate

- Manual GUI workflows function without our Runtime and observe the same Core records as a headless client.
- Links do not grant transcript ownership, live-running proof or external-control rights; imports retain untrusted/provenance status.
- Validate applicable WinUI theme/accessibility/threading/lifecycle requirements; deeper federation remains H9.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
