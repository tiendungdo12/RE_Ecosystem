# H0 FIRST REVIEW — independent / read-only

Milestone: **Platform Foundation**. Dependencies: Preserved PRE-H0 baseline; no H7 Runtime dependency.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [ARCHITECTURE_RESET.md](../../ARCHITECTURE_RESET.md). Use relevant skills: re-platform-foundation; independent milestone reviews also use re-phase-review.

Use fresh reviewer context. Independently inspect the current diff, canonical consumers/composition and relevant checks against every deliverable/gate below. Review only; do not edit or implement fixes. Verify actual ownership and dependency paths, not names or metadata validators.

## Deliverables

- Canonical Core Project/Case/Artifact/Run/Evidence/Finding identities, references and lifecycle contracts.
- Core application repository ports and versioned Infrastructure persistence with backup/recovery and preservation of legacy Projects/chat/triage records.
- Document the actual composition/dependency map across existing projects; keep pure Domain free of concrete storage/UI/protocol/model types.

## Acceptance gate

- Construct/use/persist Core state without LLM, our Agent Runtime, MCP transport or GUI.
- Migration/reference validation preserves recoverable legacy data; missing original provenance remains explicit.
- Preserve PRE-H0 scope; do not introduce H1 scorecard, H3 host or H6 federation/transcript work into H0.

Report Critical → High → Medium → Low findings with verified file/line, concrete failure, impact and expected behavior. Separate missing acceptance evidence from defects. PASS requires the gate to be supported; no findings with unverified required scenarios is not a full milestone PASS.
