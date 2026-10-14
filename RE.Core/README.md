# RE.Core

> **CURRENT:** Existing C# project with deterministic triage, direct/native/subprocess analyzers, routing and legacy transport/services. This project is not the entire logical Platform Core.

Logical **RE Platform Core** includes domain, state/persistence, evidence/finding, policy/capabilities, verification, context/cache and adapters across Domain/Application/Infrastructure and platform projects. Pure models remain free of storage/UI/protocol dependencies; concrete storage is supplied through ports.

Preserve PRE-H0 analyzer behavior while separating mixed responsibilities incrementally. Core deterministic entry points do not require LLM, our Agent Runtime, GUI or internal MCP calls. An external agent may request a high-level Core capability through the H3 MCP edge; internal analyzer execution remains direct.

The optional H7 AI Harness is **RE Agent Runtime**, not this project or a required H0 state layer. See [Architecture](../docs/ARCHITECTURE.md), [Strategy](../STRATEGY.md), [CURRENT](../docs/CURRENT_STATE.md).
