---
name: re-architecture-docs
description: "Reconcile RE_Ecosystem architecture/roadmap/project docs/skills/prompts with approved source clarification: Platform Core owns platform services; optional Agent Runtime is AI Harness; Command Center is control plane."
---

# Architecture Documentation

Use for material architecture/roadmap/ownership changes, not ordinary wording edits. Read AGENTS.md, documentation index, architecture source chain, relevant contracts, source references and actual diff.

- Apply New_Strategy plus its user-approved following clarification. Where terms were ambiguous, the clarification governs Core/Harness ownership; preserve transcript source without treating it as current implementation.
- Platform Core owns domain/state/storage/evidence/capability/policy/verification/context/cache/adapters. Logical Core spans projects. AI Harness means optional H7 Agent Runtime; Command Center is an optional client.
- H3 Platform host is a transport/service process, not agent loop. External agents use their own harness to call Core, without our Runtime/GUI.
- Distinguish Core evidence-context services from agent planning/conversation memory. Keep Core backend policy common to all consumers.
- Update canonical docs, SRS, project guides, eight RE skills, milestone prompts and inventory consistently; do not perform blind Harness-to-Core replacement in legitimate agent/history contexts.
- Keep CURRENT source-grounded, TARGET explicit and historical bodies preserved. ExternalSessionLink is H6 supplied metadata, not guaranteed transcript/control.
- Include Core-only, external-client, manual-GUI and optional-Runtime removal acceptance scenarios. Metadata/link validators alone do not prove architecture alignment.
- Label metrics MEASURED, SLO/TARGET or HYPOTHESIS; preserve baseline and supported-interface rules.
