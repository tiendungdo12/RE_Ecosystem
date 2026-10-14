# Scripts Directory Structure

> **Architecture clarification:** These scripts support the current developer/tool environment. H3 Platform host exposes RE Platform Core without our H7 Agent Runtime/AI Harness or Command Center. Native adapter build tools and WinUI build tools are separate from runtime prerequisites; see [Prerequisites](../Prerequisites.md).

## Overview

This directory contains all automation scripts for the RE_Ecosystem project, organized by environment.

```
scripts/
├── README.md              ← This file
├── dev/                   ← Development scripts (build from source, update dependencies)
│   ├── Build-Native-Deps.ps1    ← Check, build, test native DLLs (DIE, Binwalk)
│   ├── Update-DotNet-Deps.ps1   ← Update .NET templates and NuGet packages
│   └── Update-Python-Deps.ps1   ← Update Python packages (LIEF, FastMCP)
└── prod/                  ← Production scripts (deployment, packaging)
    └── (Empty - reserved for future deployment scripts)
```

## Runtime bootstrap

Run `./scripts/Setup-Runtime.ps1` from a fresh checkout to install the hash-locked CPython 3.11 Windows x64 environment and the pinned DIE database. Native payload is included separately; no Qt/MSVC rebuild is performed. The script verifies replacements before promoting them and restores the previous runtime if promotion fails. `requirements.txt` remains developer dependency input; the published runtime uses `requirements-win-x64.lock`.

## Quick Reference

| Script | Purpose | Usage |
|--------|---------|-------|
| `scripts/dev/Build-Native-Deps.ps1` | **Entry point** - Check, build, test native DLLs (DIE, Binwalk) + Python | `.\scripts\dev\Build-Native-Deps.ps1 -Mode Full` |
| `Setup-Prod.ps1` | **Root** - Production deployment guide | `.\Setup-Prod.ps1` |
| `dev/Build-Native-Deps.ps1` | Check → Build → Test → Promote pipeline | `.\scripts\dev\Build-Native-Deps.ps1 -Mode Full` |
| `dev/Update-DotNet-Deps.ps1` | Interactive menu: update .NET packages | `.\scripts\dev\Update-DotNet-Deps.ps1` |
| `dev/Update-Python-Deps.ps1` | Interactive menu: update Python packages | `.\scripts\dev\Update-Python-Deps.ps1` |

## Naming Convention

All PowerShell scripts follow **PascalCase** naming:
- `Verb-Noun-Context.ps1` pattern (e.g., `Build-Native-Deps.ps1`)
- No hyphens in the middle of words
- Consistent capitalization across all files

## Environment Separation

- **`dev/`** — Scripts that require build tools (Qt, CMake, MSVC, Rust). Run only on development machines.
- **`prod/`** — Scripts for production deployment. No build tools required.
