---
name: re-scorecard-evidence
description: "Implement or review H1 RE Platform Core deterministic scorecard, mappings, finding evidence, verifier and SARIF. H0 domain/storage is handled by foundation guidance."
---

# Core Scorecard and Evidence

Read AGENTS.md, STRATEGY H1, docs/DOMAIN_MODEL.md and CURRENT_STATE. Scorecard is a derived output over Core canonical records, not a separate agent/store.

- Keep score/mapping/export rules deterministic; missing/unsupported mappings remain explicit.
- Important Findings reference provenance-bearing Evidence: tool/capability/version, input/artifact identity/hash, location/time when available.
- Run outcome and Finding verification are separate. Missing required evidence prevents accepted completion; model confidence is not verification.
- Verifier/SARIF behavior works without LLM, our Runtime, GUI or internal MCP routing.
- Cover changed contracts, serialization, provenance completeness and partial-failure behavior on the actual path.
