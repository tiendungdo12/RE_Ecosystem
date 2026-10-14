# RE Platform Core Domain and State

> **Status:** TARGET contracts. H0/H1 establish canonical investigation state; ExternalSessionLink is a TARGET H6 contract. Verify actual types in source before using them.

## Ownership

**RE Platform Core owns domain meaning, canonical state/lifecycle, persistence, evidence, capability/policy, verification and context.** Optional Agent Runtime/Harness consumes those services and owns only agent-loop/planning/conversation concerns. Domain types stay pure; persistence implementations live behind application ports in Infrastructure.

```text
Project
  └─ Case
      ├─ Artifact
      ├─ Run
      │   ├─ Evidence
      │   └─ Finding (evidence references)
      └─ ExternalSessionLink (H6, supplied external-session metadata)
```

| Concept | Responsibility/invariant |
|---|---|
| Project | Workspace grouping; not an agent-session identity |
| Case | Stable investigation scope independent of GUI, model or protocol session |
| Artifact | Input/derived output with preserved identity/hash when available |
| Run | One attempted execution against explicit inputs, with lifecycle/outcome; deterministic runs need no agent |
| Evidence | Preserved observation/source artifact with provenance and stable handles |
| Finding | Claim linked to supporting/contradicting/contextual Evidence and explicit verification state |

H0 defines IDs/references/lifecycle/storage contracts. H1 connects PRE-H0 triage output and deterministic product analyses to these records.

## Execution and verification

Finding states are `hypothesis`, `supported`, `verified`, `contradicted`, `unknown`. Run execution status is separate. Analyzer success or model text does not automatically verify a claim. Timeout, partial failure, cancellation and required missing evidence cannot be represented as accepted clean completion.

Core's verifier validates contract/schema, references and evidence requirements. Core remains usable for manual/deterministic investigation when no Agent Runtime is installed.

## Evidence and context

Evidence records source tool/capability name/version, input/artifact identity/hash, time, output hash and source location where available. Mark missing metadata unavailable; never fabricate it for historical imports. Important Findings have traceable evidence links.

Core's EvidenceResolver and ContextBroker return policy-permitted summaries/slices with stable handles to raw evidence. Raw evidence stays in Core storage; truncation is explicit. Runtime/external harness conversation memory and planning context are not canonical evidence.

## Capability and policy (H2)

Core capability declarations record identifier/version, typed input/output, backend, risk, timeout and execution characteristics. Backend decisions are Allow, Deny or RequireApproval with reason/scope. Required approval precedes the exact operation. Every consumer, including optional Runtime and UI, uses this same enforcement and provenance boundary.

## Persistence

Core application ports and Infrastructure repositories implement canonical state persistence. Existing root `knowledge.db` stores Projects/chat/triage history, not a completed Evidence Ledger. H0 migration must version/backup/preserve it, handle upgrade failure and distinguish historical records with incomplete provenance. Pure Domain classes do not import SQLite or manipulate files.

## External session links (TARGET H6)

`ExternalSessionLink` links supplied external-session metadata to canonical Case identity:

| Field | Meaning |
|---|---|
| `host` | External product/host identity |
| `agent` | Agent identity when supplied |
| `external_session_id` | Opaque host-owned session reference; not a Case/Run ID |
| `case_id` | Canonical Core Case reference |
| `last_seen` | Time that session metadata was observed/provided, not proof of ongoing execution |
| summary/artifact references | Optional supplied/imported material, with source/availability explicit |

Core stores the link. Command Center displays available metadata. There is no automatic full-transcript ownership, remote-control authority, or guaranteed live status. Imported summaries/artifacts remain untrusted until the applicable evidence/verification checks. Full federation is H9; this type does not expand H0 scope.
