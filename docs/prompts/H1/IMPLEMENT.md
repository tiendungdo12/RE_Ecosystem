# H1 IMPLEMENT

Milestone: **Core Deterministic Product Lane**. Dependencies: H0 canonical records and preserved PRE-H0 path; no own Runtime.

Read [AGENTS](../../../AGENTS.md), [STRATEGY](../../../STRATEGY.md), [CURRENT](../../CURRENT_STATE.md), [DOMAIN_MODEL.md](../../DOMAIN_MODEL.md), [SECURITY.md](../../SECURITY.md). Use relevant skills: re-deterministic-triage, re-scorecard-evidence; independent milestone reviews also use re-phase-review.

Implement only this milestone after inspecting current source, project references, composition roots and local changes. Use existing contracts rather than imagined TARGET classes. Preserve unrelated user changes and PRE-H0. This prompt authorizes the scoped implementation and relevant verification, not future milestones.

## Deliverables

- Record deterministic pipeline execution as Core Artifact/Run/Evidence/Finding data with useful partial results.
- Implement the scoped deterministic analyses, scorecard/mappings/verifier and reproducible SARIF output over canonical records.
- Evaluate scanner/rule/Windows compatibility before adopting YARA-X; document unsupported formats/backend limitations.

## Acceptance gate

- Internal triage uses direct/native/subprocess, no LLM/internal MCP route; external MCP projection does not change this.
- Cancellation/timeout/analyzer failure cannot imply safe/verified; important Findings resolve provenance and required missing evidence blocks accepted completion.
- Analyze/store/verify/export without own Runtime or UI; preserve PRE-H0 behavior.

Implement all scoped deliverables, run relevant meaningful build/checks, and report exact results/limits. Update CURRENT/project docs only when source and acceptance evidence justify it; do not call the milestone complete from a build alone.
