---
name: re-phase-review
description: "Independently review RE_Ecosystem PRE-H0 regressions or H0-H9 milestone changes against Platform Core ownership, optional Runtime/control plane, source diff and complete acceptance evidence. Review only."
---

# Independent Milestone Review

Read AGENTS.md, selected STRATEGY gate, architecture/current and relevant contracts. Use fresh reviewer context and inspect actual diff, composition, consumers and meaningful checks; do not implement fixes.

Review correctness/regressions, lifecycle/cancellation/failures, layering, policy/provenance, scope and claimed compatibility.

- Core owns state/persistence/evidence/capability/policy/context/verification; optional H7 Runtime owns AI loop/planning/model memory.
- H0-H6 use cases and H3 host do not require own Runtime; manual UI can use Core directly.
- Check Domain purity vs logical Core's application/storage/adapter responsibilities; do not equate the assembly RE.Core with the whole platform.
- Session links contain only supplied data; no implied transcript/control/canonical-state authority.
- Verify each gate through actual consumer evidence; absent required evidence is an unresolved verification gap, not PASS.

Report Critical → High → Medium → Low, each with verified file/line, concrete failure, impact and expected behavior. Separate findings from verification gaps. No invented findings or feature implementation.
