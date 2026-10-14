# Documentation Index

> **Updated:** 14 October 2026. Inventory regenerated from the actual repository: **118 Markdown files**, **8 RE skills**, **40 milestone role prompts**. CURRENT source, TARGET design, source transcripts and historical research have distinct roles.

## Canonical documents

| Document | Responsibility |
|---|---|
| [Overview](../README.md) | Two product blocks / three components and standard consumer paths |
| [Architecture](ARCHITECTURE.md) | Platform Core ownership, optional Runtime/Harness and optional control plane |
| [Domain](DOMAIN_MODEL.md) | Core state/provenance/persistence; H6 ExternalSessionLink |
| [Strategy](../STRATEGY.md) | H0-H9 outputs/dependencies/gates |
| [Interoperability](INTEROPERABILITY.md) | H3 Platform host, Skills/client packages, optional H7 ACP |
| [Security](SECURITY.md) | Core/backend authority for every consumer |
| [CURRENT](CURRENT_STATE.md) | Source facts vs unimplemented TARGET |
| [PRE-H0 validation](VALIDATION_PRE_H0.md) | Measured publication regression/build/UI evidence |
| [Decision log / source chain](ARCHITECTURE_RESET.md) | Original vision and subsequent terminology correction |
| [SRS](../SRS_Progress_Tracking.md) | Requirement IDs, scope and evidence-backed progress |
| [AGENTS](../AGENTS.md) | Repository/WinUI rules and architecture ownership |

## Architecture source chain

[New_Strategy.md](../New_Strategy.md) and the [handoff](<../RE_Ecosystem — Full Context - Handoff sang tài khoản khác.md>) preserve the vision input. The [following conversation](reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md) is copied verbatim and clarifies ambiguous terminology: Core owns platform state/policy/etc.; AI Harness means optional Agent Runtime. Canonical docs express that approved interpretation; transcript examples are not inventories of existing source types or mandates to move folders.

Historical research/knowledge-base/tool/model-loading/SRS originals retain their body. Their notices point to current contracts. Generated reports and unrelated specialized skills remain preserved.

## Project/support guides

Domain/Application/Infrastructure/Core/Windows READMEs map existing projects into logical Platform Core and outer adapters. Presentation/WinUI/SecondUI are optional clients. Database/prerequisite/GUI-vs-CLI/mcp_server/scripts/CLI guides distinguish existing tooling from target host delivery. Detection-source material produces signals, not confirmed verdicts.

## Eight RE engineering skills

| Skill | Use |
|---|---|
| [re-platform-foundation](../.agents/skills/re-platform-foundation/SKILL.md) | H0 Core state/persistence |
| [re-deterministic-triage](../.agents/skills/re-deterministic-triage/SKILL.md) | PRE-H0 preservation / H1 direct analysis |
| [re-scorecard-evidence](../.agents/skills/re-scorecard-evidence/SKILL.md) | H1 findings/verifier/SARIF |
| [re-capability-mcp-policy](../.agents/skills/re-capability-mcp-policy/SKILL.md) | Core policy/capability H2/H3/H8 |
| [re-context-evidence](../.agents/skills/re-context-evidence/SKILL.md) | Core evidence context H2 |
| [re-headless-interoperability](../.agents/skills/re-headless-interoperability/SKILL.md) | H3 Core service exposure without GUI/own Runtime |
| [re-phase-review](../.agents/skills/re-phase-review/SKILL.md) | Independent milestone review only |
| [re-architecture-docs](../.agents/skills/re-architecture-docs/SKILL.md) | Architecture/source/docs reconciliation |

Skills guide repository work; they do not duplicate contract ownership or prove target code exists. Existing Windows/Microsoft/WinDbg skills keep their specialized scope.

## Prompt packs

[Guide](prompts/README.md). H0-H9 each contain IMPLEMENT, FIRST_REVIEW, FIX and RE_REVIEW, with deliverables/dependencies and acceptance gates. H0-H6 do not require our Agent Runtime; H7 is the optional AI Harness. H8/H9 Core operations do not require H7. Review is fresh-context/read-only. Link/format checks do not prove architecture runtime gates.

## Repository Markdown inventory

| Repository path | Classification |
|---|---|
| `.agents/skills/microsoft-code-reference/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/microsoft-docs/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/microsoft-skill-creator/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/microsoft-skill-creator/references/skill-templates.md` | Existing specialized skill/reference |
| `.agents/skills/re-architecture-docs/SKILL.md` | Active RE engineering skill |
| `.agents/skills/re-capability-mcp-policy/SKILL.md` | Active RE engineering skill |
| `.agents/skills/re-context-evidence/SKILL.md` | Active RE engineering skill |
| `.agents/skills/re-deterministic-triage/SKILL.md` | Active RE engineering skill |
| `.agents/skills/re-headless-interoperability/SKILL.md` | Active RE engineering skill |
| `.agents/skills/re-phase-review/SKILL.md` | Active RE engineering skill |
| `.agents/skills/re-platform-foundation/SKILL.md` | Active RE engineering skill |
| `.agents/skills/re-scorecard-evidence/SKILL.md` | Active RE engineering skill |
| `.agents/skills/windbg-diagnostic-method/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-kernel-bugcheck-triage/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-kernel-irp-lifecycle-triage/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-kernel-lock-deadlock-triage/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-kernel-verifier-triage/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-user-exception-triage/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-user-heap-corruption-investigation/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-user-mutex-held-across-co-await/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-user-ttd-reverse-debugging-triage/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-user-virtual-memory-exhaustion/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/windbg-user-wait-chain-analysis/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/winui-code-review/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/winui-code-review/references/quality-rules.md` | Existing specialized skill/reference |
| `.agents/skills/winui-design/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/winui-design/references/brushes-and-icons.md` | Existing specialized skill/reference |
| `.agents/skills/winui-design/references/layout-review.md` | Existing specialized skill/reference |
| `.agents/skills/winui-design/references/theme-accessibility.md` | Existing specialized skill/reference |
| `.agents/skills/winui-dev-workflow/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/winui-packaging/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/winui-packaging/references/sourcegen-patterns.md` | Existing specialized skill/reference |
| `.agents/skills/winui-session-report/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/winui-setup/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/winui-ui-testing/SKILL.md` | Existing specialized skill/reference |
| `.agents/skills/winui-wpf-migration/SKILL.md` | Existing specialized skill/reference |
| `.junie/plans/hardened-build-error-handling.md` | IDE-specific implementation plan |
| `AGENTS.md` | Canonical document / repository instruction |
| `Database_History_Notes.md` | Project support guide (CURRENT vs TARGET) |
| `GUI_vs_CLI_Tool_Integration.md` | Project support guide (CURRENT vs TARGET) |
| `He_Sinh_Thai_Reverse_Engineering_2_0_Windows_Android_Auto_Pipeline.md` | Historical/research reference |
| `Malware_API_Blacklist_Sources.md` | Detection/rule source reference |
| `New_Strategy.md` | Architecture source input; clarification governs |
| `Prerequisites.md` | Project support guide (CURRENT vs TARGET) |
| `RE.Application/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `RE.Core/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `RE.Domain/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `RE.Infrastructure/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `RE.Platform.Windows/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `RE.Presentation/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `RE.SecondUI/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `RE.WinUI/AgentConfigs/IDENTITY.md` | App-agent guidance |
| `RE.WinUI/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `RE.WinUI/Reports/knowledge.db_unknown.md` | Generated report sample (preserved) |
| `RE.WinUI/Reports/peg.exe_186c666ec83e3ea10ee346d7da89c6a4.md` | Generated report sample (preserved) |
| `RE.WinUI/Reports/peg_049A0000.dll_c491311c6ec05edf6d9f596d55151e2e.md` | Generated report sample (preserved) |
| `README.md` | Canonical document / repository instruction |
| `RE_AI_Agent_Knowledge_Base_2026-09-20.md` | Historical/research reference |
| `RE_Ecosystem — Full Context - Handoff sang tài khoản khác.md` | Architecture source input; clarification governs |
| `SRS_Progress_Tracking.md` | Canonical document / repository instruction |
| `STRATEGY.md` | Canonical document / repository instruction |
| `Tool_Integration_Analysis.md` | Historical/research reference |
| `docs/ARCHITECTURE.md` | Canonical document / repository instruction |
| `docs/ARCHITECTURE_RESET.md` | Canonical document / repository instruction |
| `docs/CLI_WORKFLOW_GUIDE.md` | Historical WinUI CLI examples with current notice |
| `docs/CURRENT_STATE.md` | Canonical document / repository instruction |
| `docs/DOCUMENTATION_INDEX.md` | Canonical document / repository instruction |
| `docs/DOMAIN_MODEL.md` | Canonical document / repository instruction |
| `docs/INTEROPERABILITY.md` | Canonical document / repository instruction |
| `docs/SECURITY.md` | Canonical document / repository instruction |
| `docs/prompts/H0/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H0/FIX.md` | Milestone role prompt |
| `docs/prompts/H0/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H0/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H1/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H1/FIX.md` | Milestone role prompt |
| `docs/prompts/H1/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H1/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H2/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H2/FIX.md` | Milestone role prompt |
| `docs/prompts/H2/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H2/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H3/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H3/FIX.md` | Milestone role prompt |
| `docs/prompts/H3/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H3/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H4/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H4/FIX.md` | Milestone role prompt |
| `docs/prompts/H4/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H4/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H5/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H5/FIX.md` | Milestone role prompt |
| `docs/prompts/H5/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H5/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H6/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H6/FIX.md` | Milestone role prompt |
| `docs/prompts/H6/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H6/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H7/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H7/FIX.md` | Milestone role prompt |
| `docs/prompts/H7/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H7/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H8/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H8/FIX.md` | Milestone role prompt |
| `docs/prompts/H8/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H8/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H9/FIRST_REVIEW.md` | Milestone role prompt |
| `docs/prompts/H9/FIX.md` | Milestone role prompt |
| `docs/prompts/H9/IMPLEMENT.md` | Milestone role prompt |
| `docs/prompts/H9/RE_REVIEW.md` | Milestone role prompt |
| `docs/prompts/README.md` | Prompt pack guide |
| `docs/reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md` | Architecture source input; clarification governs |
| `mcp_server/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `model_loading_research.md` | Historical/research reference |
| `research_report.md` | Historical/research reference |
| `scripts/README.md` | Project/developer guide (CURRENT vs TARGET) |
| `docs/VALIDATION_PRE_H0.md` | Measured publication validation |
| `lib/notices/README.md` | Third-party deployment notices |
