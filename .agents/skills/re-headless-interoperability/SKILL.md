---
name: re-headless-interoperability
description: "Implement or review H3 headless Platform host/CLI/MCP and compatible external-agent access to RE Platform Core with WinUI and our H7 Agent Runtime absent."
---

# Platform Host and External Clients

Read AGENTS.md, STRATEGY H3, architecture/interoperability/security/current docs and host composition.

- CLI/MCP server composes the same Core application services and persistence as GUI/manual clients. It is not an AI Harness and must not require our H7 Runtime or model SDK.
- Use explicit canonical handles; protocol session/current IDE project is not a Case ID.
- Project high-level typed/bounded/versioned Core capabilities; internal deterministic analyzers remain direct/native/subprocess.
- Handle startup/stop/reconnect, ownership, working directory, cancellation/timeout/logging/failure. Keep stdio protocol output free of logs.
- Core policy applies to every call. Required approval unavailable means unexecuted, not bypassed.
- Prove state/evidence access with WinUI and our Runtime absent. Track client/agent/version/transport/settings compatibility; SDK/config examples do not prove support.
- H6 supplied ExternalSessionLink metadata grants no transcripts/control. H7 ACP serves a full optional own agent; it is separate from H3 Core capability exposure.
