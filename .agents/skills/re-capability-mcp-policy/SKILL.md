---
name: re-capability-mcp-policy
description: "Implement or review RE Platform Core capability/backend policy, approval, provenance and MCP projection in H2/H3/H8. These services do not require our AI Harness."
---

# Core Capability, Policy and MCP Edge

Read AGENTS.md, STRATEGY's relevant H2/H3/H8 gate, architecture/security/domain/interoperability docs and actual execution paths.

- Core owns the capability registry/router, typed/versioned contracts, risk/timeout, backend policy and provenance. MCP server is an edge projection; it is not an AI Harness or canonical state.
- Enforce policy in Core/backend for API, CLI, GUI, external agents and optional own Runtime. Prompts/annotations do not grant authority.
- Required explicit approval binds to proposed scope/inputs; no approval channel means no execution.
- Record invocation/backend/version/input/output provenance as Evidence. Protocol/agent sessions do not own Cases.
- Bound results and preserve resolvable sources; handle cancellation, timeout and backend failure explicitly.
- Verify bypass/denial/approval paths without H7 Runtime; adding an agent must not relocate policy authority.
