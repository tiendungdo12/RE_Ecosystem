# H4 FIRST REVIEW — independent / read-only

Milestone: **Portable Workflow Skills / Host Packages**. Dependencies: H3 exposed Core capabilities and relevant implemented H1/H2 services; no own Runtime.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-headless-interoperability; independent milestone reviews also use re-phase-review.

Use fresh reviewer context. Independently inspect the current diff, canonical consumers/composition and relevant checks against every deliverable/gate below. Review only; do not edit or implement fixes. Verify actual ownership and dependency paths, not names or metadata validators.

## Deliverables

- Small RE workflow Skill packages using the supported SKILL.md format and progressive disclosure.
- Per-host configuration/package instructions for verified compatible external agents; use only available capabilities or explicit backend preconditions.
- Versioned scope/provenance and validation guidance; separate package instructions from backend permission authority.

## Acceptance gate

- External agents can use the workflow knowledge without our H7 Runtime; Skills do not grant Core permissions.
- Validate each host configuration/manifest instead of claiming universal plugin.json compatibility.
- No credentials in committed configuration; absent required backends are explicit, not invented capabilities.

Report Critical → High → Medium → Low findings with verified file/line, concrete failure, impact and expected behavior. Separate missing acceptance evidence from defects. PASS requires the gate to be supported; no findings with unverified required scenarios is not a full milestone PASS.
