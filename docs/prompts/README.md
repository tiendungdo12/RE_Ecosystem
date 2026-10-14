# Milestone Prompt Packs

H0-H9 each have four role files: IMPLEMENT, FIRST_REVIEW, FIX and RE_REVIEW. Read the selected [Strategy gate](../../STRATEGY.md), actual [CURRENT](../CURRENT_STATE.md), linked contracts and relevant skills before implementation/review.

Every prompt contains concrete deliverables, dependencies and acceptance gates. H0-H6 provide/use **RE Platform Core** without requiring our Agent Runtime; H3 is a Platform host, not an AI Harness. H7 introduces optional RE Agent Runtime/Harness and ACP. H8/H9 Core operations do not acquire a required H7 dependency.

First/re-review uses fresh context and is read-only. Fix continues in implementation context and addresses valid findings without reducing the intended scope. Metadata/link/build success alone is not a full milestone PASS. PRE-H0 remains the preserved user-reported DONE baseline and keeps its original prompt set.

Core-only, external-client, GUI/manual, Runtime-removal and external-session availability scenarios are defined in [Strategy](../../STRATEGY.md) and [Architecture](../ARCHITECTURE.md). Update CURRENT only with matching source/runtime evidence.
