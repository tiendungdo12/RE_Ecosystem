# RE Platform Interoperability

> **Status:** TARGET interfaces by milestone. See [CURRENT_STATE](CURRENT_STATE.md). Agent Runtime/Harness is optional; H3 serves Core independently.

## Three consumer paths

```text
External agent / harness → MCP → Platform host → RE Platform Core
CLI / Command Center → Platform Core services
Compatible IDE → ACP → optional RE Agent Runtime (H7) → RE Platform Core
```

External agents use their own harness. An H3 headless Platform host exposes Core capabilities and storage-backed use cases without our H7 Runtime or WinUI. It may be launched as an MCP-server subprocess by a compatible client. Core library use does not require any MCP transport.

MCP client/host/server roles are protocol roles, not synonyms for AI Harness, Case or RE_Ecosystem state ownership. The Platform host is our server-side composition process. Canonical RE IDs remain explicit across clients.

## Interface responsibilities

| Interface | Responsibility | Gate |
|---|---|---|
| Core application API | Domain/state, analysis, policy, evidence/context and verification use cases | H0 onward |
| CLI | Scriptable local access to Core services, independent of GUI/Runtime | H3 |
| MCP server | Small typed/versioned projection of Core capabilities for external clients | H3 |
| Agent Skills | Portable workflow knowledge using `SKILL.md`; no own-runtime requirement | H4 |
| Host packages/config | Host-specific launch/setup/skills validated per client | H4 |
| Specialist adapters | Narrow internal Core integration; not raw tool catalogs | H5/H8 |
| ACP agent endpoint | Editor/client access to a full optional RE Agent Runtime | H7 |
| Federation/A2A | Justified later session/agent integration | H9+ |

MCP is not an internal deterministic-analyzer transport. ACP does not define the RE domain. H4 Skills are workflow knowledge; H7 adds optional own-runtime execution/planning. A proposed `plugin.json/mcp.json` tree is not universal host support. [Agent Skills specification](https://agentskills.io/specification).

## External host compatibility

VS Code and JetBrains AI Assistant document MCP configuration; this establishes a supported integration route, not proof that this repository already implements all required interfaces. Validate the actual IDE/agent/version/transport/settings combination. [VS Code MCP](https://code.visualstudio.com/docs/agent-customization/mcp-servers) · [JetBrains AI Assistant MCP](https://www.jetbrains.com/help/ai-assistant/mcp.html).

H3 starts with local stdio and explicit startup, working directory, logs, cancellation, timeout, stop and reconnect behavior. Publish setup only for tested combinations. Remote network hosting requires a separate security/deployment gate.

## State, approval and external sessions

CLI/MCP/GUI/Runtime call the same Core services and enforcement. Required approval cannot be bypassed through another transport; no approval channel means no execution. MCP disconnects do not turn protocol state into canonical Case ownership.

H6 [ExternalSessionLink](DOMAIN_MODEL.md#external-session-links-target-h6) records supplied host/agent/session/case/last-seen metadata and available summary/artifact references. Do not presume full transcripts, live execution, control permissions or undocumented provider access. Command Center may display supplied data and invoke supported actions; H9 handles deeper federation.

## Acceptance

Prove an H3 Core operation with both WinUI and RE Agent Runtime absent, then display the same canonical state through H6 UI without an own-agent loop. H7 ACP must be tested separately. Disabling optional Runtime must leave Core data and external MCP/CLI access usable.
