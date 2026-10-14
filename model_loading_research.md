> [!CAUTION]
> **HISTORICAL / RESEARCH — terminology clarification, 13 October 2026.** Earlier Harness/platform ownership and implementation/metric claims below are preserved research, not current contracts. [Canonical architecture](docs/ARCHITECTURE.md) assigns state/policy/evidence/persistence to RE Platform Core and reserves AI Harness for optional H7 RE Agent Runtime. The [following architecture conversation](docs/reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md) clarifies the source vision. Use [CURRENT](docs/CURRENT_STATE.md) for actual source status.

# Nghiên cứu: Nâng cấp hệ thống Model & Quota (v6 — tài liệu lịch sử)

> [!CAUTION]
> **TRẠNG THÁI 20/09/2026: HISTORICAL / EXPERIMENTAL — KHÔNG DÙNG LÀM KẾ HOẠCH PRODUCTION.**
> Tài liệu này nghiên cứu private/internal Antigravity Language Server (`GetUserStatus`, `agentapi`, state protobuf...) trong source snapshot cũ. Google document public `agy` headless cho scripting/program output, trong khi Terms áp dụng giới hạn với third-party software truy cập Antigravity service. Vì vậy **không mở rộng private RPC/state-file integration**. Ưu tiên public/documented interface; nếu external agent cần RE capabilities, H3 target là gọi headless RE Harness qua MCP. RE Command Center vẫn là client tùy chọn, không phải host bắt buộc.
> Nguồn: https://antigravity.google/docs/cli/headless/ ; https://antigravity.google/terms


## Mục lục
1. [Danh sách Model từ Server](#1-danh-sách-14-model-từ-server)
2. [Cơ chế 2 tầng Model → Chat](#2-cơ-chế-2-tầng-model--chat)
3. [Luồng gửi Chat hiện tại](#3-luồng-gửi-chat-hiện-tại)
4. [Quota Antigravity](#4-quota-antigravity)
5. [Ollama — Bỏ quota in-app, thêm nút mở web](#5-ollama--bỏ-quota-in-app-thêm-nút-mở-web)
6. [Bug & Vấn đề hiện tại](#6-bug--vấn-đề-hiện-tại)
7. [Chi tiết kỹ thuật cho triển khai](#7-chi-tiết-kỹ-thuật-cho-triển-khai)
8. [Kiến trúc đề xuất nâng cấp](#8-kiến-trúc-đề-xuất-nâng-cấp)
9. [Danh sách Files & Thay đổi cụ thể](#9-danh-sách-files--thay-đổi-cụ-thể)
10. [Flash Lite & Local NPU Model](#10-flash-lite--local-npu-model)

---

## 1. Danh sách 14 Model từ Server

**Nguồn**: `GetUserStatus` RPC → `userStatus.cascadeModelConfigData.clientModelConfigs`
**Test từ**: App gốc Antigravity IDE v2.9.1 đang chạy (PID 32016, HTTP port 54982)

| # | `label` (UI) | `modelId` | `model` (enum) | Tier CLI | Quota Pool |
|---|---|---|---|---|---|
| 1 | **Gemini 3.7 Flash (High)** | `gemini-3.7-flash-high` | `MODEL_PLACEHOLDER_M298` | `flash` | Gemini |
| 2 | **Gemini 3.7 Flash (Medium)** | `gemini-3.7-flash-medium` | `MODEL_PLACEHOLDER_M299` | `flash` | Gemini |
| 3 | **Gemini 3.7 Flash (Low)** | `gemini-3.7-flash-low` | `MODEL_PLACEHOLDER_M300` | `flash` | Gemini |
| 4 | Gemini 3.6 Flash (High) | `gemini-3.6-flash-high` | `MODEL_PLACEHOLDER_M71` | `flash` | Gemini |
| 5 | Gemini 3.6 Flash (Medium) | `gemini-3.6-flash-medium` | `MODEL_PLACEHOLDER_M72` | `flash` | Gemini |
| 6 | Gemini 3.6 Flash (Low) | `gemini-3.6-flash-low` | `MODEL_PLACEHOLDER_M73` | `flash` | Gemini |
| 7 | Gemini 3.5 Flash (High) | `gemini-3-flash-agent` | `MODEL_PLACEHOLDER_M84` | `flash` | Gemini |
| 8 | Gemini 3.5 Flash (Medium) | `gemini-3.5-flash-low` | `MODEL_PLACEHOLDER_M20` | `flash` | Gemini |
| 9 | Gemini 3.5 Flash (Low) | `gemini-3.5-flash-extra-low` | `MODEL_PLACEHOLDER_M187` | `flash` | Gemini |
| 10 | Gemini 3.1 Pro (High) | `gemini-pro-agent` | `MODEL_PLACEHOLDER_M16` | `pro` | Gemini |
| 11 | Gemini 3.1 Pro (Low) | `gemini-3.1-pro-low` | `MODEL_PLACEHOLDER_M36` | `pro` | Gemini |
| 12 | Claude Sonnet 4.6 (Thinking) | `claude-sonnet-4-6` | `MODEL_PLACEHOLDER_M35` | `pro` | Claude/GPT |
| 13 | Claude Opus 4.6 (Thinking) | `claude-opus-4-6-thinking` | `MODEL_PLACEHOLDER_M26` | `pro` | Claude/GPT |
| 14 | GPT-OSS 120B (Medium) | `gpt-oss-120b-medium` | `MODEL_OPENAI_GPT_OSS_120B_MEDIUM` | `pro` | Claude/GPT |

**Default**: `MODEL_PLACEHOLDER_M298` = Gemini 3.7 Flash (High)

**Thứ tự sắp xếp** (từ `clientModelSorts`): Server quyết định, hiện theo thứ tự bảng trên.

**Mỗi model config chứa**:
```json
{
  "label": "Gemini 3.7 Flash (High)",
  "modelId": "gemini-3.7-flash-high",
  "modelOrAlias": { "model": "MODEL_PLACEHOLDER_M298" },
  "supportsImages": true,
  "isRecommended": true,
  "tagTitle": null,
  "quotaInfo": { "remainingFraction": 1.0, "resetTime": "2026-08-22T..." },
  "allowedTiers": ["TEAMS_TIER_PRO", ...],
  "supportedMimeTypes": { ... }
}
```

> [!TIP]
> Google thêm/bớt model bất kỳ lúc nào (đã chứng kiến thêm 3.7 Flash giữa 2 lần test). Hardcode model = **sai**. Phải lấy dynamic từ API.

---

## 2. Cơ chế 2 tầng Model → Chat

### CLI `agentapi` chỉ nhận 3 tier:
```
agentapi new-conversation --model=<flash_lite|flash|pro> "prompt"
```

Test thực tế: `gemini-3.7-flash-high`, `MODEL_PLACEHOLDER_M298`, v.v. đều trả `"no available models found for tier ..."`.

### Server phân biệt model cụ thể nhờ state file:
```
┌──────────────────────────┐     ┌──────────────────────────────────┐
│ UI: User chọn model      │     │ State file:                      │
│ → ghi state file          │────▶│ last_selected_agent_model: M298  │
└──────────────────────────┘     └──────────────────────────────────┘
                                                    │
┌──────────────────────────┐                        │
│ CLI: --model=flash       │────┐                   ▼
└──────────────────────────┘    │   ┌──────────────────────────────┐
                                └──▶│ Server: flash + M298 state   │
                                    │ → Chạy Gemini 3.7 Flash High │
                                    └──────────────────────────────┘
```

---

## 3. Luồng gửi Chat hiện tại

```
User bấm Send
  │
  ▼
ChatViewModel.SendMessageAsync()
  │  modelName = settings.AntigravityModel  (vd: "gemini_3.6_flash_medium")
  │
  ▼
AntigravityLanguageServerClient.GenerateStreamAsync(prompt, systemPrompt, model, workspace)
  │  1. EnsureServerAsync()
  │  2. HasAuthTokenAsync()
  │  3. NormalizeAgentApiModel("gemini_3.6_flash_medium")
  │     → không có '-' → fallback → return "flash"
  │  4. StartConversationViaAgentApiAsync(server, "flash", prompt, workspace)
  │     → CLI: language_server.exe agentapi new-conversation --model=flash
  │  5. StreamAgentStateUpdatesAsync(conversationId) → yield delta text
  ▼
UI hiển thị streaming response
```

> [!CAUTION]
> **Bug**: App không ghi `last_selected_agent_model` vào state file → Server dùng state từ lần cuối chọn trên **Antigravity IDE gốc**, không phải từ RE_Ecosystem.

---

## 4. Quota Antigravity

### Endpoint: `RetrieveUserQuotaSummary`
Trả về `response.groups[]` — mỗi group (Gemini, Claude/GPT) chứa buckets (5h, weekly) với `remainingFraction` + `resetTime`.

Ngoài ra, mỗi model trong `clientModelConfigs` cũng chứa `quotaInfo.remainingFraction` — có thể dùng khi hiển thị quota per-model.

### Vấn đề hiện tại:
| Vấn đề | Chi tiết |
|---|---|
| **Không cache** | `GetUsageAsync()` mỗi lần gọi đều POST RPC mới. Delay 2-4s. |
| **Flicker bug (ChatPage)** | `RefreshUsageAsync()` ẩn `UsageItemsControl` ngay lập tức → xóa cache vừa render. |
| **Chờ cả Ollama** | `Task.WhenAll(antigravityTask, ollamaTask)` — nếu Ollama timeout thì Antigravity cũng chờ. |

### Giải pháp: Cache-first + no flicker (xem mục 8).

---

## 5. Ollama — Bỏ quota in-app, thêm nút mở web

### Tại sao bỏ quota in-app:
- Ollama Cloud **không có API endpoint** nào cho quota/usage/billing
- App hiện chỉ đếm request local (không phản ánh chính xác GPU-time quota thực)
- Xem quota chính xác chỉ qua `https://ollama.com/settings`

### Thay đổi UI:

**Settings page** — bỏ:
- `OllamaViewUsageButton` (nút "View Usage" cũ)
- `InlineUsagePanel` cho Ollama (5h/weekly request count cards)
- `UsedOllamaModelsPanel` trong UsageDialog
- Logic `ApplyOllamaUsageSnapshot()`, `RenderOllamaModels()`, `RenderInlineUsage()` phần Ollama

**Settings page** — thêm:
- Nút "View Usage on Web" → `Launcher.LaunchUriAsync(new Uri("https://ollama.com/settings"))`
- Đặt trong `OllamaExpander`, thay chỗ nút "View Usage" cũ

**ChatPage** — bỏ:
- Phần render Ollama usage trong `RenderUsageGroups()` (lines 284-338): synthetic quota groups + models used
- `_ollamaUsageSnapshot` field
- Ollama branch trong `RefreshUsageAsync()` (`Task.WhenAll` → chỉ gọi Antigravity)

**ChatPage** — thêm:
- Khi provider = Ollama, nút usage mở web thay vì flyout. Hoặc flyout chỉ hiện nút "View on ollama.com".

### Giữ nguyên:
- [`OllamaUsageStore.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.Infrastructure/AI/OllamaUsageStore.cs) — vẫn track local cho internal analytics
- `RecordRequestAsync()` trong `OllamaClient.GenerateStreamAsync` — vẫn ghi log

---

## 6. Bug & Vấn đề hiện tại

### Bug 1: 🔴 Model mismatch — User chọn model A, server chạy model B
- App không ghi `last_selected_agent_model` vào state file
- CLI gửi tier (`flash`/`pro`), server dùng state từ Antigravity IDE gốc
- **Fix**: Ghi state file trước khi gọi agentapi

### Bug 2: 🟡 ChatPage Usage flicker
- `RefreshUsageAsync()` ẩn items ngay → xóa cache trước đó
- **Fix**: Không collapse/show loading, chỉ refresh silent

### Bug 3: 🔴 Hardcode model — thiếu 3.7 Flash, không tự cập nhật
- 14 model → hardcode 11
- **Fix**: Dynamic load từ `GetUserStatus` + cache

### Bug 4: 🟡 NormalizeAgentApiModel — underscore không pass-through
- Model hardcode dùng underscore → luôn fallback thành `"flash"`
- **Fix**: Dùng `modelId` (có dấu `-`) hoặc sửa regex

---

## 7. Chi tiết kỹ thuật cho triển khai

### A. State file — Path và Format

**Path** (app RE_Ecosystem dùng profile cô lập):
```
D:\Projects\ReverseEngineering\RE_Ecosystem\.ai_data\antigravity\default\app\antigravity_state.pbtxt
```
Hoặc packaged mode:
```
%LOCALAPPDATA%\RE_Ecosystem\.ai_data\antigravity\default\app\antigravity_state.pbtxt
```

Lấy runtime: `RuntimeDataPaths.AntigravityProfilePath` + `\app\antigravity_state.pbtxt`

**Format** (protobuf text):
```protobuf
last_selected_agent_model: MODEL_PLACEHOLDER_M298
```

**Logic ghi**:
1. Đọc file hiện tại
2. Regex replace `^last_selected_agent_model:\s*.*$` → `last_selected_agent_model: {modelEnum}`
3. Nếu không tìm thấy → append dòng mới
4. Ghi lại file

---

### B. ModelOption class hiện tại

File: [`ModelOption.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.Presentation/ViewModels/ModelOption.cs)

```csharp
public sealed class ModelOption
{
    public string Name { get; init; } = string.Empty;        // vd: "gemini_3.6_flash_medium"
    public string DisplayName { get; init; } = string.Empty; // vd: "Gemini 3.6 Flash Medium"
    public string MenuName { get; init; } = string.Empty;    // vd: "Medium" (trong submenu)
    public string Provider { get; init; } = string.Empty;    // "Antigravity" | "Ollama"
    public bool IsCloud { get; init; }
    public string ScopeLabel { get; init; } = string.Empty;  // "Cloud" | "Local"
    public string SpeedBadge { get; init; } = string.Empty;  // "Fast" | ""
    public string CurrentSelectionBadge { get; set; } = string.Empty; // "High" | "Medium" | "Low"
    public List<ModelOption> SubModels { get; } = new();
    // Computed:
    public string Details => ...;
    public string ProviderIcon => ...;
    public string ProviderColor => ...;
}
```

**Cần thêm**:
```csharp
public string ModelId { get; init; } = string.Empty;    // "gemini-3.7-flash-high" (từ API)
public string ModelEnum { get; init; } = string.Empty;  // "MODEL_PLACEHOLDER_M298" (từ API)
```

---

### C. GlobalSettings fields hiện tại

File: [`GlobalSettings.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/Core/Settings/GlobalSettings.cs)

```csharp
// Model-related fields:
"ai_provider"              → AiProvider = "Antigravity"
"ollama_port"              → OllamaPort = "11434"
"ollama_model"             → OllamaModel = "deepseek-v4-flash"
"antigravity_model"        → AntigravityModel = "flash"
"is_antigravity_logged_in" → IsAntigravityLoggedIn = false
"antigravity_account_email"→ AntigravityAccountEmail = ""
"last_selected_provider"   → LastSelectedProvider = "Antigravity"
"last_selected_model"      → LastSelectedModel = ""
```

**Cần thêm**:
```csharp
[JsonPropertyName("antigravity_cached_models")]
public string AntigravityCachedModelsJson { get; set; } = "[]";
// Lưu JSON array: [{label, modelId, model, supportsImages, tagTitle}, ...]

[JsonPropertyName("ollama_cached_models")]
public string OllamaCachedModelsJson { get; set; } = "[]";
// Lưu JSON array: ["deepseek-v4-flash", "qwen3:32b", ...]
```

> [!NOTE]
> `System.Text.Json` tự xử lý backward compatibility — field mới có default value, file cũ không có key → dùng default. Không cần migration code.

---

### D. Migration strategy — Model name cũ → mới

| Settings cũ (underscore) | ModelId mới (dash) | Model Enum |
|---|---|---|
| `gemini_3.6_flash_medium` | `gemini-3.6-flash-medium` | `MODEL_PLACEHOLDER_M72` |
| `gemini_3.6_flash_high` | `gemini-3.6-flash-high` | `MODEL_PLACEHOLDER_M71` |
| `claude_sonnet_4.6` | `claude-sonnet-4-6` | `MODEL_PLACEHOLDER_M35` |
| `gpt_oss_120b` | `gpt-oss-120b-medium` | `MODEL_OPENAI_GPT_OSS_120B_MEDIUM` |

**Logic migration**: Khi load settings, nếu `LastSelectedModel` dùng underscore → tìm trong cached models theo fuzzy match (normalize cả 2 về lowercase, bỏ dấu `_` và `-`) → cập nhật sang `modelId` mới.

---

### E. RPC nào dùng cho model list?

| RPC | Dữ liệu | Tốc độ | Dùng cho |
|---|---|---|---|
| `GetUserStatus` | User info + plan + **clientModelConfigs** (14 model) + sort order + default | Chậm hơn (account sync) | Lần đầu + refresh |
| `GetCascadeModelConfigData` | Chỉ **clientModelConfigs** + sort + default | Nhanh hơn (không sync account) | Refresh nhanh |
| `GetAvailableModels` | Tất cả model (bao gồm internal) | Nhanh | Không cần cho UI |

**Khuyến nghị**: Dùng `GetCascadeModelConfigData` cho model list (nhẹ hơn). Dùng `GetUserStatus` chỉ khi cần cả account info.

---

### F. Ollama UI elements cần thay đổi

**Settings** ([`AISettingsPage.xaml`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/Views/Settings/AISettingsPage.xaml)):
- Bỏ: `OllamaViewUsageButton` (line 43), `InlineUsagePanel` Ollama content (lines 62-68), `UsedOllamaModelsPanel` (lines 113-116)
- Thêm: Nút "View Usage on Web" → `Launcher.LaunchUriAsync("https://ollama.com/settings")`

**Settings** ([`AISettingsPage.xaml.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/Views/Settings/AISettingsPage.xaml.cs)):
- Bỏ: `_ollamaUsageSnapshot` (line 26), `ApplyOllamaUsageSnapshot()` (lines 138-156), `RenderOllamaModels()` (lines 200-230), `RenderInlineUsage()` Ollama branch (lines 179-187), Ollama filter trong `RenderUsageGroups()` (lines 235-239), `RefreshUsageAsync("Ollama")` (lines 470-474)
- Thêm: Handler cho nút mở web

**ChatPage** ([`ChatPage.xaml.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/Views/ChatPage.xaml.cs)):
- Bỏ: `_ollamaUsageSnapshot` (line 31), Ollama synthetic groups trong `RenderUsageGroups()` (lines 284-338), `ollamaTask` trong `RefreshUsageAsync()` (lines 232-242)
- Thêm: Khi `AiProvider == "Ollama"` → nút usage mở web thay vì flyout quota

---

## 8. Kiến trúc đề xuất nâng cấp

### A. Dynamic Model Loading (Cache-First)

```
LoadModelsAsync() MỚI:

1. [INSTANT] Đọc cache từ Global_Settings.json
   ├─ antigravity_cached_models → Parse label regex → nhóm parent/sublevel
   │   → Populate AvailableModels + AvailableModelGroups ngay
   └─ ollama_cached_models → Populate ngay

2. [BACKGROUND] Refresh
   ├─ GetCascadeModelConfigData RPC → clientModelConfigs
   │   → So sánh với cache → Khác → update cache + rebuild UI
   └─ GET /api/tags (Ollama)
       → So sánh → update cache + rebuild UI

3. Lần đầu (chưa có cache)
   → Hiện "Loading..." nhẹ trên model picker
   → Background xong → populate + save cache
```

### B. Sửa luồng Chat — Set state trước khi gửi

```
SendMessageAsync() MỚI:

1. Đọc selectedModel.ModelEnum (vd: "MODEL_PLACEHOLDER_M298")

2. Ghi state file:
   Path: RuntimeDataPaths.AntigravityProfilePath + "/app/antigravity_state.pbtxt"
   Regex replace: last_selected_agent_model: MODEL_PLACEHOLDER_M298

3. Xác định tier từ selectedModel:
   - label chứa "Flash" hoặc modelId chứa "flash" → "flash"
   - label chứa "Pro"/"Claude"/"Opus"/"GPT" → "pro"  
   - Mặc định → "flash"

4. Gọi agentapi --model={tier}
```

### C. Quota Display — Cache-First + No Flicker

```
ChatPage.UsageFlyout_Opened:

1. [INSTANT] RenderUsageGroups() — hiện cache cũ
   → KHÔNG ẩn UsageItemsControl
   → KHÔNG hiện LoadingRing

2. [BACKGROUND] RefreshUsageAsync()
   → CHỈ gọi Antigravity (bỏ Ollama task)
   → Xong → RenderUsageGroups() silent update
```

### D. Ollama — Nút mở web thay quota

```
Settings:  Nút "View Usage on Web" → mở https://ollama.com/settings
ChatPage:  Khi provider=Ollama, nút usage → mở web (hoặc hiện flyout với nút link)
```

### E. Nhóm Model động

```
Regex: ^(.+?)\s*\((High|Medium|Low)\)$
  Match   → nhóm vào parent, sublevel menu [High] [Medium] [Low]
  No match → model đơn lẻ (Claude, GPT, etc.)

Thứ tự: Dùng clientModelSorts từ server
```

---

## 9. Danh sách Files & Thay đổi cụ thể (LỊCH SỬ — không triển khai private RPC mới)

### Tầng Infrastructure

#### [MODIFY] [`AntigravityLanguageServerClient.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.Infrastructure/AI/AntigravityLanguageServerClient.cs)
- Thêm `GetCascadeModelsAsync()` — gọi `GetCascadeModelConfigData`, parse `clientModelConfigs` → trả về list model config objects
- Thêm `SetModelStateAsync(string modelEnum)` — ghi `last_selected_agent_model` vào state file
- Sửa `NormalizeAgentApiModel()` — nhận `ModelOption`, xác định tier từ label/modelId thay vì string match cũ

#### [MODIFY] [`OllamaClient.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.Infrastructure/AI/OllamaClient.cs)
- Thêm xử lý HTTP `429` → throw custom exception cho UI hiện cảnh báo

---

### Tầng Presentation

#### [MODIFY] [`ModelOption.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.Presentation/ViewModels/ModelOption.cs)
- Thêm `ModelId` (string) — `"gemini-3.7-flash-high"`
- Thêm `ModelEnum` (string) — `"MODEL_PLACEHOLDER_M298"`

---

### Tầng WinUI

#### [MODIFY] [`GlobalSettings.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/Core/Settings/GlobalSettings.cs)
- Thêm `AntigravityCachedModelsJson` (string, default `"[]"`)
- Thêm `OllamaCachedModelsJson` (string, default `"[]"`)

#### [MODIFY] [`ChatViewModel.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/ViewModels/ChatViewModel.cs)
- **Xóa**: Toàn bộ hardcode Antigravity models (~60 dòng)
- **Thêm**: Cache-first load + background refresh + dynamic parse/nhóm
- **Sửa**: `SendMessageAsync()` — gọi `SetModelStateAsync()` trước `GenerateStreamAsync()`
- **Sửa**: Migration logic cho model name cũ (underscore → modelId)

#### [MODIFY] [`ChatPage.xaml.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/Views/ChatPage.xaml.cs)
- **Bỏ**: `_ollamaUsageSnapshot`, Ollama synthetic groups trong `RenderUsageGroups()`, `ollamaTask` trong `RefreshUsageAsync()`
- **Fix flicker**: Bỏ collapse/show loading trong `RefreshUsageAsync()`
- **Thêm**: Khi Ollama → nút usage mở web

#### [MODIFY] [`AISettingsPage.xaml`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/Views/Settings/AISettingsPage.xaml)
- **Bỏ**: `OllamaViewUsageButton`, `UsedOllamaModelsPanel`
- **Thêm**: Nút "View Usage on Web" trong OllamaExpander

#### [MODIFY] [`AISettingsPage.xaml.cs`](file:///D:/Projects/ReverseEngineering/RE_Ecosystem/RE.WinUI/Views/Settings/AISettingsPage.xaml.cs)
- **Bỏ**: `ApplyOllamaUsageSnapshot()`, `RenderOllamaModels()`, Ollama branch trong `RenderInlineUsage()` và `RenderUsageGroups()`
- **Thêm**: Handler mở web `Launcher.LaunchUriAsync("https://ollama.com/settings")`
- **Sửa**: `LoadOllamaModelsAsync()` — cache-first

---

## 10. Flash Lite & Local NPU Model

### A. Tier `flash_lite` trong Antigravity — Dùng để làm gì?

`flash_lite` là tier nhẹ nhất của Antigravity, dùng cho các task **không cần suy luận phức tạp**:

| Tính năng | Mô tả |
|---|---|
| **Repository mapping** | Scan & map codebase trong 1-2s để cung cấp context cho model chính |
| **Autocomplete** | Gợi ý code inline nhanh, hoàn thành boilerplate |
| **Orchestration nhẹ** | Quyết định abort/ignore/tiếp tục trong workflow agent |
| **Tóm tắt nhanh** | Checkpoint tóm tắt, tiêu đề conversation |

Trên app **Antigravity IDE gốc**, tier này được dùng nội bộ (background) — user không chọn trực tiếp.

### B. RE_Ecosystem có cần `flash_lite` không?

**Hiện tại: Không bắt buộc.** App RE_Ecosystem dùng Antigravity chủ yếu cho chat (cần model mạnh). Tuy nhiên có thể tận dụng cho:

| Task tiềm năng | Phù hợp? | Ghi chú |
|---|---|---|
| Tự động đặt tiêu đề chat | ✅ Rất phù hợp | Tiết kiệm quota model chính |
| Tóm tắt nội dung dài | ✅ Phù hợp | Tóm tắt document/conversation |
| Phân loại câu hỏi user | ✅ Phù hợp | Routing sang provider phù hợp |
| Gợi ý prompt / autocomplete | ✅ Phù hợp | UX tốt hơn |
| Chat chính | ❌ Không | Chất lượng quá thấp cho chat |

**Kết luận**: Có thể tận dụng cho các task hỗ trợ nhẹ. Nhưng **không ưu tiên** trong lần nâng cấp này — tập trung vào dynamic model loading + fix bugs trước.

---

### C. NPU của máy — Phân tích khả năng

**Phần cứng đã kiểm tra**:
```
CPU:    Intel Core Ultra 9 185H (16 cores, Meteor Lake)
NPU:    Intel AI Boost — 11 TOPS (INT8)
Status: OK, driver hoạt động bình thường
GPU:    Intel Arc iGPU (tích hợp)
Platform TOPS: 35 TOPS (CPU + GPU + NPU combined)
```

### D. NPU 11 TOPS có chạy được LLM không?

> [!WARNING]
> **Kết luận: KHÔNG khuyến nghị dùng NPU 11 TOPS để chạy LLM cho app.**

**Lý do chi tiết:**

| Yếu tố | Đánh giá |
|---|---|
| **11 TOPS quá thấp** | Entry-level cho AI. Copilot+ PC yêu cầu **40+ TOPS**. NPU này chỉ bằng ~1/4. |
| **LLM bị bottleneck bởi memory bandwidth** | LLM cần đọc hàng GB weight từ RAM. NPU chia sẻ system RAM → bandwidth thấp → chậm hơn CPU/iGPU. |
| **Tốc độ dự kiến** | Với model 1-3B params: ~2-5 tokens/s trên NPU vs ~10-20 tokens/s trên iGPU/CPU. Chậm đến mức user thấy rõ lag. |
| **Software support** | Ollama **không hỗ trợ NPU**. Cần OpenVINO/IPEX-LLM riêng — phức tạp integration. |
| **Phi Silica (Windows built-in)** | Yêu cầu NPU 40+ TOPS hoặc dGPU (RTX 30+/RX 9060+). **11 TOPS NPU không đủ**. |

### E. Thay thế: Dùng Intel Arc iGPU thì sao?

Intel Core Ultra 9 185H có **Intel Arc iGPU** tích hợp — mạnh hơn NPU đáng kể cho LLM:

| Phương án | Khả thi? | Tốc độ dự kiến | Complexity |
|---|---|---|---|
| **Ollama + iGPU** (không cần NPU) | ✅ Có thể | ~10-20 tok/s (model 1-3B) | Thấp — Ollama hỗ trợ sẵn |
| **ONNX Runtime + DirectML** | ✅ Có thể | ~15-25 tok/s (model 1-3B) | Trung bình — cần NuGet package |
| **OpenVINO + NPU** | ⚠️ Phức tạp | ~2-5 tok/s | Cao — cần pipeline riêng |
| **Phi Silica (Windows AI API)** | ❌ Không đủ NPU | N/A | N/A |

### F. Nếu muốn tải sẵn model nhẹ — cách nào tốt nhất?

**Phương án khuyến nghị: Ollama + model nhỏ chạy trên CPU/iGPU**

```
Ollama đã cài → pull model nhẹ → auto-load khi app khởi động
```

| Model | Params | RAM cần | Tốc độ (CPU) | Tốc độ (iGPU) | Phù hợp cho |
|---|---|---|---|---|---|
| `phi3:mini` | 3.8B | ~2.5 GB | ~8-12 tok/s | ~15-20 tok/s | Tóm tắt, phân loại, tiêu đề |
| `qwen2.5:1.5b` | 1.5B | ~1.2 GB | ~15-25 tok/s | ~25-35 tok/s | Task siêu nhẹ, autocomplete |
| `tinyllama:1.1b` | 1.1B | ~0.8 GB | ~20-30 tok/s | ~30-40 tok/s | Nhanh nhất, chất lượng thấp |

**Ưu điểm Ollama**:
- Đã tích hợp sẵn trong app (OllamaClient)
- Không cần thêm dependency nào
- Chạy được trên CPU/iGPU mà không cần NPU
- Model quản lý bằng `ollama pull` — user đã quen

**Phương án thay thế: ONNX Runtime GenAI (tương lai)**

Nếu muốn nhúng model vào app mà không cần Ollama:
```
NuGet: Microsoft.ML.OnnxRuntimeGenAI.WinML
Model: Phi-3-mini-4k-instruct-onnx (DirectML variant)
```
- Tự động chọn hardware tốt nhất (iGPU > CPU)
- Không cần Ollama server chạy nền
- **Phức tạp hơn** — cần ship model files (~2GB) cùng app

### G. Đề xuất cho app RE_Ecosystem

> [!NOTE]
> **Ưu tiên lần nâng cấp này**: Không bao gồm local model. Tập trung vào dynamic model loading + fix bugs + bỏ Ollama quota.

**Roadmap tương lai** (khi xong lần nâng cấp chính):

| Phase | Nội dung | Phương án |
|---|---|---|
| **Hiện tại** | Dynamic models, fix bugs, bỏ Ollama quota | Xem mục 8-9 |
| **Tương lai gần** | Tự đặt tiêu đề chat bằng `flash_lite` | Gọi Antigravity `--model=flash_lite` cho task tóm tắt |
| **Tương lai xa** | Nhúng model nhẹ local (không cần internet) | Ollama `qwen2.5:1.5b` hoặc ONNX Runtime GenAI |

---

## Tham khảo: Kết quả test Endpoint

| Endpoint | Status | Dùng cho |
|---|---|---|
| `HasAuthToken` | ✅ 200 | Kiểm tra đăng nhập |
| `GetUserStatus` | ✅ 200 | User info + model configs + quota (nặng) |
| `GetCascadeModelConfigData` | ✅ 200 | Chỉ model configs + sort + default (nhẹ) |
| `RetrieveUserQuotaSummary` | ✅ 200 | Quota groups (Gemini, Claude/GPT) |
| `GetAvailableModels` | ✅ 200 | Tất cả model kể cả internal |
| `GetCascadeModelConfigs` | ✅ 200 | Trả `{}` (empty) |
| `SetUserSettings` | ❌ unimplemented | Không dùng được — phải ghi state file trực tiếp |
| `GetModels` / `ListModels` | ❌ 404 | Không tồn tại |
