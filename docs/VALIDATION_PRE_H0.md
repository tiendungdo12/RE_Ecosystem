# PRE-H0 Publication Validation

> **Classification: MEASURED**, 14 October 2026. Code snapshot: [`038d4dcc3140858a68c28633cdaee7ee0c198d6d`](https://github.com/tiendungdo12/RE_Ecosystem/commit/038d4dcc3140858a68c28633cdaee7ee0c198d6d). PRE-H0 remains the reported completed baseline; this establishes scoped regression evidence, not H0–H9 acceptance.

## Environment and method

Windows x64 host, .NET SDK 10.0.402, WinApp CLI 0.7.1, Windows App SDK 2.5.1, CPython 3.11.9 and the exact 78-package Windows wheel lock (including pip). The clean candidate contained only Git-eligible source/assets/native payload, with no copied workspace runtime or personal database. Bootstrap downloaded SHA256-checked archives and installed hash-locked wheels using standard machine SDK/NuGet/pip caches. This is clean source/dependency preparation on the same configured host, not second-machine reproducibility.

## Results

| Check | Result |
|---|---|
| .NET complete suite in clean candidate | **85 passed, 0 failed, 0 skipped**: 67 PRE-H0 plus 18 publication-safety cases; final run 1m40s |
| Python script in bootstrapped candidate | **56 regression PASS markers**, script exit 0; PE/APK smoke output also produced |
| Final clean WinUI x64 Debug build | Succeeded, 0 errors; **4000 warnings** with bootstrapped content. PRI/resource warnings dominate; analyzer/source warnings remain |
| SecondUI Debug build | Succeeded, 0 errors |
| Local isolated UI | Four assertions passed: dynamic title, preferences after restart, key save outside JSON, key deletion/ciphertext removal with unchanged source template |
| Bootstrap | Clean install and repeat succeeded; wrong archive SHA256 rejected while existing runtime remained unchanged |
| Staged content | Pattern scan found no provider-token/private-key/JWT/credential-assignment candidates; private runtime paths absent, settings template clean, published artifact hashes matched |
| Git whitespace | Passed; Markdown hard line breaks retained using the documented `.gitattributes` rule |
| Original data | Root knowledge.db SHA256 unchanged; personal settings preserved outside source before template sanitization |

[Sanitized machine-readable evidence](validation/PRE_H0_2026-10-14.json) records suite totals and UI assertions. Full TRX/build/CLI logs remain local to avoid publishing personal paths or unrelated machine data.

## Reproduce

```powershell
pwsh -NoProfile -File scripts/Setup-Runtime.ps1
dotnet test App_Test/App_Test.csproj
.\Python_Runtime\python.exe mcp_server/test_triage.py
dotnet build RE.WinUI/RE.WinUI.csproj -c Debug -p:Platform=x64
dotnet build RE.SecondUI/RE.SecondUI.csproj -c Debug
pwsh -NoProfile -File scripts/Verify-Publication-UI.ps1
git diff --cached --check
```

The UI helper launches a local app using an isolated user-data directory and synthetic credentials; it stops only processes it launched. Windows Sandbox was unavailable and was not enabled. An initial shallow automation selector wait timed out although deeper inspection confirmed the updated title; the helper uses explicit tree assertions. The automation produced four passing assertions and stopped its test app. Tool-runner sessions were interrupted after those assertions because a launcher/automation session remained open; this report does not claim a zero exit status for that outer runner. The initial cleanup lease-release command was removed, and the helper explicitly focuses the target before mouse input.

## Scope and limitations

- Canonical triage remains direct/native/.NET/subprocess, without LLM or internal MCP. Guard tests use fake invocation hooks and never patch a binary or send an active network request.
- DPAPI tests cover round-trip/delete, ciphertext corruption, migration, sequential saving and failed-write preservation. API key UI input is masked; persistent JSON stores metadata/references only.
- Temporary PRE-H0 policy is fail-closed for active/unknown tool calls. Scope-bound approvals, canonical audit Evidence and full H2/H8 policy services remain TARGET.
- [Artifact manifest](../lib/artifacts.json) contains measured SHA256 and supplied source refs; [notices](../lib/notices/README.md) include manufacturer SPDX. Native binaries were not independently rebuilt. Optional/unverified artifacts are excluded from Git.
- Secret scans are pattern checks, not a guarantee about encoded/binary secrets. No Release/AOT, full Light/Dark/High Contrast/accessibility, new H0 migration, H3 host or H7 runtime acceptance is claimed.
- Existing Antigravity private-provider bridge is experimental. No supported production integration or measured research productivity gain is claimed.
