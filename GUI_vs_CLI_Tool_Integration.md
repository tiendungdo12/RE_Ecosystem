# GUI, CLI and External Agent Integration

> **Status:** TARGET design for RE Platform Core, optional Command Center and optional H7 Agent Runtime. [CURRENT](docs/CURRENT_STATE.md), [gates](STRATEGY.md).

## Standard paths

```text
External agent / own harness → MCP → Platform host → RE Platform Core
CLI / RE Command Center → Core application services
Compatible IDE → ACP → optional RE Agent Runtime → Core services
```

The Platform host supplies Core services and transport. It is not an AI Harness. Core library use requires no MCP, model, Runtime or GUI; external protocol access needs the appropriate independently launchable host.

## Interface choice

| Need | Interface |
|---|---|
| Internal deterministic analysis | Core native/.NET/direct-subprocess adapters |
| Script state/results | H3 CLI over Core services |
| Give an existing agent RE capabilities | H3 MCP server projecting Core capabilities |
| Human/manual workflows | Optional H6 Command Center over Core |
| Deep/active tools | H5/H8 narrow adapters under Core policy |
| Portable workflow knowledge | H4 Skills and tested host packages |
| Own agent loop in a compatible IDE | Optional H7 Runtime and ACP |

H3 must access Core while both WinUI and our Runtime are absent. H6 must show the same state and run manual/deterministic workflows without an agent. A tested IDE/agent/version/transport/settings matrix is required before declaring a client supported.

## Lifecycle, sessions and control

Track owned process startup/stop/logs/working directory, cancellation, timeout, disconnect/reconnect and failure. Do not kill a specialist GUI process opened by the user. Core policy and evidence apply regardless of client.

H6 ExternalSessionLink displays only supplied host/agent/session/Case/last-seen and available summary/artifact references. No automatic transcript, live status or control authority; H9 deeper federation is separate. Command Center may invoke supported own/external-agent actions, but it does not need to implement their agent loop.
