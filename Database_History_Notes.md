# Database History and Platform Core Persistence

> **CURRENT:** KnowledgeStore resolves root `knowledge.db`; existing tables include Projects, chat sessions/history and triage history. Full canonical Core storage remains TARGET. [Snapshot](docs/CURRENT_STATE.md).

## Ownership

**Persistence/state belongs to RE Platform Core.** Application repository ports and Infrastructure implementations serve Project/Case/Artifact/Run/Evidence/Finding. Pure Domain classes do not import SQLite or manage files. Optional Runtime agent memory is separate and cannot replace canonical evidence; consumers use the same Core records.

Target deployed state is under `%LOCALAPPDATA%/RE_Ecosystem/`, independent of WinUI/Runtime. User-selected case export/bundles provide portability; exact format, encryption and retention are future contracts, not current capabilities.

## H0 migration

Back up/version the source DB; preserve Projects/chat/triage history and recovery on failure. Define retry/idempotency/reference validation and incompatible-schema/corruption outcomes. Imported chat/model reports are not automatically Findings or verified Evidence; unavailable original provenance stays explicit.

Core migration/storage must work without GUI, LLM, Agent Runtime or MCP transport. H6 TARGET ExternalSessionLink storage holds only supplied session metadata/material references, not guaranteed transcripts. This edit does not relocate/migrate the actual DB.

## Publication settings and test isolation

Publication preparation does not migrate the normal root database. It is excluded from Git along with SQLite journals and retains the original history. Settings move separately to per-user storage; secrets use DPAPI references. The explicit `RE_ECOSYSTEM_USER_DATA_ROOT` developer/test directory redirects settings and legacy DB for isolated UI testing. Canonical H0 storage/migration remains TARGET.
