# Platform Security and Trust Boundaries

> **Status:** TARGET backend controls and acceptance criteria; implementation evidence is tracked separately.

## Ownership

- **RE Platform Core:** canonical state, capability execution, backend policy/approval, provenance, verification, evidence resolution and disclosure control.
- **Optional Agent Runtime/Harness:** planner/model loop and conversation state; consumes Core APIs and cannot bypass or widen Core permissions.
- **Optional Command Center/external clients:** request and display actions/results through Core services.
- **Platform host/adapters:** translate transport/OS/tool interfaces and preserve Core enforcement; transport is not authorization.
- **Untrusted data:** binaries, decompiler text, network payload, tool/model output, imported reports and session summaries.

Pure Domain types do not depend on UI/SQLite/protocol/model SDKs. Logical Platform Core includes application use cases and storage/tool adapters behind ports; those implementations are not an AI Harness.

## Approval and provenance

Classify capability risk and enforce policy in Core/backend on API, CLI, MCP, GUI and optional Runtime paths. Patch, active scan/replay, attach/inject, driver/system changes and similar actions require the policy-defined explicit approval for the exact scope/inputs. Record decision, actor, invocation, backend/version and result as provenance. When no approval channel is available, leave the action unexecuted.

Prompts and protocol annotations do not grant authority. Bound outputs, timeouts and process ownership; propagate cancellation and preserve partial failures. Tool text cannot instruct Core to change policy.

## Context, credentials and session links

Core ContextBroker applies minimal disclosure and resolves permitted evidence slices; pseudonymization mappings stay local and sensitive. Agent conversation memory is consumer state and does not replace Evidence. Cloud/local reasoning remains optional.

Do not commit keys/tokens in configs, SQLite or logs; use an OS secret store or suitable provider. ExternalSessionLink metadata does not grant transcript access or remote-control rights. Imported summaries/artifacts remain untrusted and incomplete provenance stays explicit. Use only supported external-host interfaces.

## Acceptance

Core deterministic/state/evidence/persistence/verification must work without LLM, Runtime, GUI or MCP transport. At H3 test backend denial/approval on headless paths while Runtime/WinUI are absent. H6 manual UI workflow uses the same Core controls. H7 tests agent attempts to bypass policy; removing Runtime preserves Core functionality. H8 requires side-effect, timeout/cancel and audit coverage for every supported adapter path.
