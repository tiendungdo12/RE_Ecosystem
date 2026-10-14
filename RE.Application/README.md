# RE.Application

> **CURRENT:** Conversation search, model selection and usage-summary services exist. Complete canonical Core use cases remain TARGET.

This project implements the **application/use-case slice of RE Platform Core**, not an AI Harness. Core use cases manage canonical lifecycle/storage ports, deterministic execution, capability policy and bounded evidence context. Agent loop, model planning/conversation memory and own skill/goal execution belong to optional H7 RE Agent Runtime.

H0/H1 establish canonical state and deterministic-recording workflows; H2 adds Core policy/capability/context; H3 CLI/MCP and H6 GUI call the same services. Pure domain/application contracts do not depend on WinUI or concrete SQLite/protocol classes.

See [Architecture](../docs/ARCHITECTURE.md), [Domain](../docs/DOMAIN_MODEL.md), [Strategy](../STRATEGY.md) and [CURRENT](../docs/CURRENT_STATE.md).
