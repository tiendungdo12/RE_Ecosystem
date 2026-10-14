# H6 FIRST REVIEW — independent / read-only

Milestone: **Optional Command Center / External Session Links**. Dependencies: H0-H3 Core state/host; H5 for selected specialist views. No H7 Runtime requirement.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md). Use relevant skills: re-platform-foundation, re-headless-interoperability; independent milestone reviews also use re-phase-review.

Use fresh reviewer context. Independently inspect the current diff, canonical consumers/composition and relevant checks against every deliverable/gate below. Review only; do not edit or implement fixes. Verify actual ownership and dependency paths, not names or metadata validators.

## Deliverables

- Optional WinUI manual/deterministic control plane over Core Projects/Cases/Artifacts/Runs/Evidence/Findings/reports/policies.
- TARGET ExternalSessionLink storage/views: supplied host, agent, opaque external session ID, Case reference, last seen and available summary/artifact references.
- Supported optional agent actions/status interfaces with explicit availability; keep planner/model/agent loop outside the UI.

## Acceptance gate

- Manual GUI workflows function without our Runtime and observe the same Core records as a headless client.
- Links do not grant transcript ownership, live-running proof or external-control rights; imports retain untrusted/provenance status.
- Validate applicable WinUI theme/accessibility/threading/lifecycle requirements; deeper federation remains H9.

Report Critical → High → Medium → Low findings with verified file/line, concrete failure, impact and expected behavior. Separate missing acceptance evidence from defects. PASS requires the gate to be supported; no findings with unverified required scenarios is not a full milestone PASS.
