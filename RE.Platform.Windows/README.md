# RE.Platform.Windows

> **CURRENT:** Windows environment/file picker and Authenticode adapters exist; inspect source before claiming other implementations.

This is the Windows-specific outer adapter slice of Platform. Pure Domain and portable Core use cases stay free of Windows/UI types; concrete OS adapters implement their ports. UI-specific HWND/picker services are composed only by the relevant client and must not make headless Core require a window.

Neither OS adapters nor application orchestration are AI Harness. H7 RE Agent Runtime remains optional. See [Architecture](../docs/ARCHITECTURE.md) and [WinUI rules](../AGENTS.md).
