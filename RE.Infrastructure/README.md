# RE.Infrastructure

> **CURRENT:** KnowledgeStore and existing provider/MCP/triage/tool registrations exist; some dependencies are transitional.

Concrete persistence/tool/platform adapters implement **Platform Core** ports. Core persistence is required functionality, not H7 agent memory. Existing provider/chat/bridge code is not proof of a completed RE Agent Runtime; separate optional provider/runtime composition from the required Core path.

H0 must back up/version/preserve solution-root `knowledge.db` before implementing the planned deployed `%LOCALAPPDATA%/RE_Ecosystem/` default. Core storage serves all consumers and is not owned by GUI, MCP session or Runtime.

Bound external processes/outputs, timeout/cancellation and provenance; protect secrets and enforce Core policy on every adapter path. Internal deterministic analysis remains direct; H3 MCP projects Core capabilities at the edge.

See [Architecture](../docs/ARCHITECTURE.md), [Domain](../docs/DOMAIN_MODEL.md), [Security](../docs/SECURITY.md), [CURRENT](../docs/CURRENT_STATE.md).
