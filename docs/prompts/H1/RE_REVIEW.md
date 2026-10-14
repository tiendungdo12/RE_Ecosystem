# H1 RE-REVIEW — fresh / read-only

Milestone: **Core Deterministic Product Lane**. Dependencies: H0 canonical records and preserved PRE-H0 path; no own Runtime.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-deterministic-triage, re-scorecard-evidence; independent milestone reviews also use re-phase-review.

Use fresh reviewer context after fixes. Read all original findings, independently inspect the current diff/consumer paths and verify each correction plus regressions. Review only; no file edits. Check ownership, dependencies and acceptance evidence across the complete gate.

## Deliverables

- Record deterministic pipeline execution as Core Artifact/Run/Evidence/Finding data with useful partial results.
- Implement the scoped deterministic analyses, scorecard/mappings/verifier and reproducible SARIF output over canonical records.
- Evaluate scanner/rule/Windows compatibility before adopting YARA-X; document unsupported formats/backend limitations.

## Acceptance gate

- Internal triage uses direct/native/subprocess, no LLM/internal MCP route; external MCP projection does not change this.
- Cancellation/timeout/analyzer failure cannot imply safe/verified; important Findings resolve provenance and required missing evidence blocks accepted completion.
- Analyze/store/verify/export without own Runtime or UI; preserve PRE-H0 behavior.

Report Critical → High → Medium → Low findings with verified file/line and concrete impact/expected behavior; reconcile every original finding. PASS requires all required deliverables/scenarios supported and no unresolved material issue. Report missing/weak evidence explicitly.
