# H4 IMPLEMENT

Milestone: **Portable Workflow Skills / Host Packages**. Dependencies: H3 exposed Core capabilities and relevant implemented H1/H2 services; no own Runtime.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [INTEROPERABILITY.md](../../INTEROPERABILITY.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-headless-interoperability; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Small RE workflow Skill packages using the supported SKILL.md format and progressive disclosure.
- Per-host configuration/package instructions for verified compatible external agents; use only available capabilities or explicit backend preconditions.
- Versioned scope/provenance and validation guidance; separate package instructions from backend permission authority.

## Acceptance gate

- External agents can use the workflow knowledge without our H7 Runtime; Skills do not grant Core permissions.
- Validate each host configuration/manifest instead of claiming universal plugin.json compatibility.
- No credentials in committed configuration; absent required backends are explicit, not invented capabilities.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
