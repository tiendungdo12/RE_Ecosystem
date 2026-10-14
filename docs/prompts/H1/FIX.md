# H1 FIX — implementation context

Milestone: **Core Deterministic Product Lane**. Dependencies: H0 canonical records and preserved PRE-H0 path; no own Runtime.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-deterministic-triage, re-scorecard-evidence; independent milestone reviews also use re-phase-review.

Read first-review findings and inspect the current source/diff. Continue in the implementation context, resolve every valid finding while preserving the complete intended deliverable and milestone scope. Give evidence for rejected/duplicate findings; do not shrink the goal to easier checks or expand future milestones.

## Deliverables

- Record deterministic pipeline execution as Core Artifact/Run/Evidence/Finding data with useful partial results.
- Implement the scoped deterministic analyses, scorecard/mappings/verifier and reproducible SARIF output over canonical records.
- Evaluate scanner/rule/Windows compatibility before adopting YARA-X; document unsupported formats/backend limitations.

## Acceptance gate

- Internal triage uses direct/native/subprocess, no LLM/internal MCP route; external MCP projection does not change this.
- Cancellation/timeout/analyzer failure cannot imply safe/verified; important Findings resolve provenance and required missing evidence blocks accepted completion.
- Analyze/store/verify/export without own Runtime or UI; preserve PRE-H0 behavior.

Re-run checks affected by changes; report finding IDs resolved/rejected, source evidence, checks/results and remaining gaps. Update supported docs/status only. Do not claim independent re-review PASS.
