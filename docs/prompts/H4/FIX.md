# H4 FIX — implementation context

Milestone: **Portable Workflow Skills / Host Packages**. Dependencies: H3 exposed Core capabilities and relevant implemented H1/H2 services; no own Runtime.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-headless-interoperability; independent milestone reviews also use re-phase-review.

Read first-review findings and inspect the current source/diff. Continue in the implementation context, resolve every valid finding while preserving the complete intended deliverable and milestone scope. Give evidence for rejected/duplicate findings; do not shrink the goal to easier checks or expand future milestones.

## Deliverables

- Small RE workflow Skill packages using the supported SKILL.md format and progressive disclosure.
- Per-host configuration/package instructions for verified compatible external agents; use only available capabilities or explicit backend preconditions.
- Versioned scope/provenance and validation guidance; separate package instructions from backend permission authority.

## Acceptance gate

- External agents can use the workflow knowledge without our H7 Runtime; Skills do not grant Core permissions.
- Validate each host configuration/manifest instead of claiming universal plugin.json compatibility.
- No credentials in committed configuration; absent required backends are explicit, not invented capabilities.

Re-run checks affected by changes; report finding IDs resolved/rejected, source evidence, checks/results and remaining gaps. Update supported docs/status only. Do not claim independent re-review PASS.
