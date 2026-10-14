# Prerequisites

> **Status:** Current developer environment vs TARGET product hosting. [CURRENT](docs/CURRENT_STATE.md).

## Current development

C# projects target .NET 10. Windows/Windows App SDK tooling is needed for WinUI and Windows-specific adapters. Existing subprocess adapters use Python; inspect `Python_Runtime` and dependency scripts. MSVC/CMake/Qt/Rust are needed only when building their respective native dependencies; specialist Java/IDA/Ghidra/Burp/Frida dependencies apply only to selected adapters.

For this Windows x64 snapshot, run `pwsh -NoProfile -File scripts/Setup-Runtime.ps1` for pinned Python dependencies and DIE rules. Native payload is in `lib/` with hashes/notices. Rebuilds are separate: `scripts/dev/Build-Native-Deps.ps1 -Mode BuildOnly`; that developer script requires its documented compiler/Qt paths. Command Center build requirements are not mandatory requirements for every Core library consumer.

## Core and host requirements

**Platform Core works without LLM, AI Harness, MCP transport or GUI.** It still needs the selected storage/tool adapter dependencies. H3 external-agent access requires an implemented independently launchable Platform host, compatible client/transport and permitted configuration; it requires neither our H7 Runtime nor WinUI. An external agent may have its own model/account requirements.

GPU/local LLM/Ollama/PyTorch/PyG/cloud-model credentials are not Core prerequisites. H7 own Agent Runtime is optional and has separate provider/config requirements. Publish exact IDE/agent/transport versions only after verification.

## Data/secrets

Current root `knowledge.db` needs H0 backup/versioned migration before the planned per-user `%LOCALAPPDATA%/RE_Ecosystem/` path. Do not delete/relocate legacy data through documentation. Secrets belong in an OS store or suitable provider, not committed JSON/YAML/SQLite.

Settings and credentials are per-user: `%LOCALAPPDATA%/RE_Ecosystem/Settings/`, with API secrets protected by CurrentUser DPAPI and JSON storing references. Root database history is unchanged and ignored by Git. The explicit `RE_ECOSYSTEM_USER_DATA_ROOT` developer/test variable isolates these stores without migrating default data.
