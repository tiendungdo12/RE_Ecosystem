---
name: re-platform-foundation
description: "Implement or review H0 RE Platform Core domain/state/lifecycle, repository ports and persistence migration. Optional H7 Agent Runtime/AI Harness is not part of foundation."
---

# Platform Foundation

Read AGENTS.md, STRATEGY H0, docs/ARCHITECTURE.md, docs/DOMAIN_MODEL.md, docs/CURRENT_STATE.md and the relevant source/composition. TARGET names are not implemented types.

- Logical RE Platform Core owns Project/Case/Artifact/Run/Evidence/Finding, canonical lifecycle and persistence. It spans Domain/Application/Infrastructure; the assembly RE.Core is only one slice.
- Pure Domain excludes UI/SQLite/protocol/model types; application ports and Infrastructure implement storage and adapters. Do not ban persistence from the whole Platform Core.
- Verify Core state/storage use cases without LLM, our H7 Agent Runtime, GUI or MCP transport. A headless deterministic use case is not an AI Harness.
- Preserve stable IDs/provenance and PRE-H0 behavior. Back up/version/recover legacy Projects/chat/triage data; unavailable provenance stays explicit.
- H6 ExternalSessionLink is not an H0 transcript/federation task. Keep H1 scoring, H2 policy/context and H3 host delivery at their gates.
