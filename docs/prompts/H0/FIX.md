# H0 FIX — implementation context

Milestone: **Platform Foundation**. Dependencies: Preserved PRE-H0 baseline; no H7 Runtime dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE_RESET.md](../../ARCHITECTURE_RESET.md). Use relevant skills: re-platform-foundation; independent milestone reviews also use re-phase-review.

Read first-review findings and inspect the current source/diff. Continue in the implementation context, resolve every valid finding while preserving the complete intended deliverable and milestone scope. Give evidence for rejected/duplicate findings; do not shrink the goal to easier checks or expand future milestones.

## Deliverables

- Canonical Core Project/Case/Artifact/Run/Evidence/Finding identities, references and lifecycle contracts.
- Core application repository ports and versioned Infrastructure persistence with backup/recovery and preservation of legacy Projects/chat/triage records.
- Document the actual composition/dependency map across existing projects; keep pure Domain free of concrete storage/UI/protocol/model types.

## Acceptance gate

- Construct/use/persist Core state without LLM, our Agent Runtime, MCP transport or GUI.
- Migration/reference validation preserves recoverable legacy data; missing original provenance remains explicit.
- Preserve PRE-H0 scope; do not introduce H1 scorecard, H3 host or H6 federation/transcript work into H0.

Re-run checks affected by changes; report finding IDs resolved/rejected, source evidence, checks/results and remaining gaps. Update supported docs/status only. Do not claim independent re-review PASS.
