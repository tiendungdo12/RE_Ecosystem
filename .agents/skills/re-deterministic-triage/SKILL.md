---
name: re-deterministic-triage
description: "Implement or review RE Platform Core deterministic triage, H1 canonical evidence integration, cancellation and partial failure while preserving PRE-H0 and preventing LLM/internal MCP routing."
---

# Core Deterministic Triage

Read AGENTS.md, STRATEGY H1, architecture/domain/current docs and the actual canonical pipeline/tests. PRE-H0 remains DONE per user report and is the preserved baseline.

- Internal Core analyzers use native/.NET/direct subprocess without LLM or MCP. An external agent can request a high-level triage capability through H3 MCP; the projection does not change internal execution.
- Preserve DIE/Binwalk/Hash/Authenticode/LIEF/AXML behavior unless a scoped verified change requires it. Propagate cancellation and isolate analyzer failures with valid partial results.
- Core application use cases record Run/Evidence through persistence ports. Optional H7 Runtime/AI Harness is not required to record results.
- Keep UI/model dependencies out of required Core execution. Pure Domain has no concrete SQLite; logical Core still owns persistence.
- Execution success is distinct from verified Finding; unknown/error/timeout is not clean/safe.
- Verify the canonical consumer path and prevent obsolete MCP/static-triage routing from returning.
