# H0 RE-REVIEW — fresh / read-only

Milestone: **Platform Foundation**. Dependencies: Preserved PRE-H0 baseline; no H7 Runtime dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE_RESET.md](../../ARCHITECTURE_RESET.md). Use relevant skills: re-platform-foundation; independent milestone reviews also use re-phase-review.

Use fresh reviewer context after fixes. Read all original findings, independently inspect the current diff/consumer paths and verify each correction plus regressions. Review only; no file edits. Check ownership, dependencies and acceptance evidence across the complete gate.

## Deliverables

- Canonical Core Project/Case/Artifact/Run/Evidence/Finding identities, references and lifecycle contracts.
- Core application repository ports and versioned Infrastructure persistence with backup/recovery and preservation of legacy Projects/chat/triage records.
- Document the actual composition/dependency map across existing projects; keep pure Domain free of concrete storage/UI/protocol/model types.

## Acceptance gate

- Construct/use/persist Core state without LLM, our Agent Runtime, MCP transport or GUI.
- Migration/reference validation preserves recoverable legacy data; missing original provenance remains explicit.
- Preserve PRE-H0 scope; do not introduce H1 scorecard, H3 host or H6 federation/transcript work into H0.

Report Critical → High → Medium → Low findings with verified file/line and concrete impact/expected behavior; reconcile every original finding. PASS requires all required deliverables/scenarios supported and no unresolved material issue. Report missing/weak evidence explicitly.
