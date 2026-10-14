# RE.Domain

> **CURRENT:** Triage/analysis DTOs and a few project/tool models exist; the complete canonical investigation model is TARGET.

This is the pure-domain slice of **logical RE Platform Core**. Platform Core also includes application use cases, persistence and adapters in other projects. Domain itself does not import UI, SQLite, protocol SDK or model-provider types.

H0 establishes Project/Case/Artifact/Run/Evidence/Finding identity, references and lifecycle. H1 adds deterministic product/verification contracts; H6 adds TARGET ExternalSessionLink metadata. Run execution and Finding verification remain distinct.

Optional H7 RE Agent Runtime/AI Harness consumes Core contracts; it does not own canonical state. See [Domain Model](../docs/DOMAIN_MODEL.md), [Architecture](../docs/ARCHITECTURE.md) and [CURRENT](../docs/CURRENT_STATE.md).
