# H4 RE-REVIEW — fresh / read-only

Milestone: **Portable Workflow Skills / Host Packages**. Dependencies: H3 exposed Core capabilities and relevant implemented H1/H2 services; no own Runtime.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-headless-interoperability; independent milestone reviews also use re-phase-review.

Use fresh reviewer context after fixes. Read all original findings, independently inspect the current diff/consumer paths and verify each correction plus regressions. Review only; no file edits. Check ownership, dependencies and acceptance evidence across the complete gate.

## Deliverables

- Small RE workflow Skill packages using the supported SKILL.md format and progressive disclosure.
- Per-host configuration/package instructions for verified compatible external agents; use only available capabilities or explicit backend preconditions.
- Versioned scope/provenance and validation guidance; separate package instructions from backend permission authority.

## Acceptance gate

- External agents can use the workflow knowledge without our H7 Runtime; Skills do not grant Core permissions.
- Validate each host configuration/manifest instead of claiming universal plugin.json compatibility.
- No credentials in committed configuration; absent required backends are explicit, not invented capabilities.

Report Critical → High → Medium → Low findings with verified file/line and concrete impact/expected behavior; reconcile every original finding. PASS requires all required deliverables/scenarios supported and no unresolved material issue. Report missing/weak evidence explicitly.
