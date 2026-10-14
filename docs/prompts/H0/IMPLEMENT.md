# H0 IMPLEMENT

Milestone: **Platform Foundation**. Dependencies: Preserved PRE-H0 baseline; no H7 Runtime dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE_RESET.md](../../ARCHITECTURE_RESET.md). Use relevant skills: re-platform-foundation; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Canonical Core Project/Case/Artifact/Run/Evidence/Finding identities, references and lifecycle contracts.
- Core application repository ports and versioned Infrastructure persistence with backup/recovery and preservation of legacy Projects/chat/triage records.
- Document the actual composition/dependency map across existing projects; keep pure Domain free of concrete storage/UI/protocol/model types.

## Acceptance gate

- Construct/use/persist Core state without LLM, our Agent Runtime, MCP transport or GUI.
- Migration/reference validation preserves recoverable legacy data; missing original provenance remains explicit.
- Preserve PRE-H0 scope; do not introduce H1 scorecard, H3 host or H6 federation/transcript work into H0.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
