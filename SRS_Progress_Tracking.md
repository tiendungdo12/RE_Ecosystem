# RE_Ecosystem Requirements and Progress

> **Status:** Tracker for RE Platform Core + optional RE Agent Runtime/AI Harness + optional Command Center. [Strategy](STRATEGY.md) defines gates; [CURRENT](docs/CURRENT_STATE.md) records evidence.

| ID | Requirement | Gate | Status |
|---|---|---|---|
| RE-CORE-01 | Direct deterministic triage independent of LLM/internal MCP/GUI | PRE-H0/H1 | PRE-H0 DONE per user; preserve baseline |
| RE-CORE-02 | Logical Core includes state/application/storage/adapters; pure Domain excludes UI/SQLite/protocol types | H0 | TARGET separation |
| RE-STATE-01 | Core canonical Project/Case/Artifact/Run/Evidence/Finding lifecycle/IDs | H0 | TARGET |
| RE-PERSIST-01 | Core persistence through ports, versioned migration and recoverable legacy history | H0 | TARGET; current root KnowledgeStore is legacy state |
| RE-EVIDENCE-01 | Evidence provenance, Findings/verification and Run outcome kept distinct | H0/H1 | TARGET |
| RE-POLICY-01 | Capability policy/approval and bounded evidence context enforced in Core/backend for every consumer | H2 | TARGET |
| RE-HOST-01 | CLI/MCP Platform host uses Core services without WinUI or our Runtime | H3 | TARGET |
| RE-HOST-02 | Compatibility validated per IDE/agent/version/transport/settings | H3/H4 | TARGET |
| RE-HOST-03 | Portable Skills/host packages do not require our Runtime or grant policy authority | H4 | TARGET |
| RE-CC-01 | Optional GUI/manual workflows over the same Core state as headless clients | H6 | TARGET |
| RE-SESSION-01 | Core ExternalSessionLink stores supplied metadata/available material; no presumed transcript/control | H6 | TARGET |
| RE-AGENT-01 | Optional H7 Runtime/Harness owns agent loop/model/planning memory; consumes Core controls | H7 | TARGET; dedicated complete Runtime not established |
| RE-RISK-01 | Active/dynamic side effects pass Core policy and scope-bound explicit approval | H8 | TARGET |
| RE-EVAL-01 | Reproducible evals and supported federation; metrics labeled with method/limits | H9+ | TARGET / ongoing doc rule |

## Tracking compatibility

Pre-correction RE-HARNESS-01/02/03/04 tracked requirements now named RE-STATE-01 / RE-PERSIST-01 / RE-EVIDENCE-01 / RE-POLICY-01 respectively. This is a documentation-ID mapping; ownership is Core and implementation progress is not promoted.

## Baseline and evidence rules

PRE-H0 is DONE per user report. Inspected pipeline/tests support the direct-path baseline; this edit does not rerun or enlarge it. Keep SOURCE-inspected, user-reported, measured runtime and TARGET claims distinct.

Status values: NOT STARTED, IN PROGRESS, DONE, BLOCKED or DEFERRED. DONE requires source/consumer/migration evidence and the complete gate, including absent Runtime/GUI scenarios where applicable. Model/provider availability or a green build alone does not prove a native AI Harness or full platform.

External session last-seen does not prove ongoing execution. Missing imported provenance remains explicit. Performance/quality needs reproducible fixture/machine/method and MEASURED/SLO/TARGET/HYPOTHESIS labeling.
