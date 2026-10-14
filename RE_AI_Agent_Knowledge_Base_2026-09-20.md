> [!CAUTION]
> **HISTORICAL / RESEARCH — terminology clarification, 13 October 2026.** Earlier Harness/platform ownership and implementation/metric claims below are preserved research, not current contracts. [Canonical architecture](docs/ARCHITECTURE.md) assigns state/policy/evidence/persistence to RE Platform Core and reserves AI Harness for optional H7 RE Agent Runtime. The [following architecture conversation](docs/reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md) clarifies the source vision. Use [CURRENT](docs/CURRENT_STATE.md) for actual source status.

---
# RE / AI Agent / Windows Native / Student Pack — Knowledge Base tổng hợp

**Ngày tổng hợp:** 2026-09-20  
**Ngôn ngữ:** Tiếng Việt, giữ nguyên các thuật ngữ kỹ thuật tiếng Anh  
**Mục tiêu:** Một tài liệu triển khai duy nhất gom toàn bộ tri thức, quyết định, nuance, rủi ro và hướng kiến trúc đã xuất hiện trong chuỗi trao đổi; ưu tiên phục vụ RE Command Center, workflow AI coding, Windows-native development, và khai thác GitHub Student Developer Pack.

> **Quy ước trạng thái**
>
> - **[Verified 2026-09-20]**: đã đối chiếu lại với tài liệu web hiện tại.
> - **[Design conclusion]**: kết luận/khuyến nghị kiến trúc rút ra từ toàn bộ thảo luận.
> - **[Historical]**: điều đúng tại thời điểm trao đổi trước nhưng có thể đã đổi.
> - **[Needs re-check]**: sản phẩm/pricing/ToS thay đổi nhanh, cần kiểm tra lại trước khi ra quyết định dài hạn.

---

## 0. Executive summary — các quyết định “canonical” nên giữ

1. **Stack phát triển cá nhân hiện phù hợp nhất**
   - **Rider**: IDE chính cho C#/WinUI/C++/native interop và debug.
   - **JetBrains Air**: task orchestration, worktree/isolation, agent A implement → agent B review.
   - **Antigravity + Google AI Pro**: agent/harness chính từ hệ Google.
   - **Codex + ChatGPT Plus**: agent/harness thứ hai độc lập, dùng để code/review/research.
   - **Zed**: optional, thiên về ACP/MCP interoperability test bench; không phải công cụ bắt buộc hằng ngày.
   - **Full Visual Studio**: chỉ cài khi thật sự cần tooling WinUI/WDK sâu; VS Build Tools + WDK + WinDbg + CLI build có thể đủ cho workflow hiện tại.

2. **Chiến lược subscription**
   - Baseline hợp lý: **Google AI Pro + ChatGPT Plus**, mua theo tháng.
   - Chỉ lên **Google AI Ultra** hoặc **ChatGPT Pro** khi quota trở thành bottleneck thực tế.
   - Không mua thêm Claude/Grok/Kimi/Mistral/Trae chỉ vì “model diversity”. Chỉ thêm khi một harness thứ ba giải quyết một bottleneck lặp lại mà hai hệ hiện tại không giải quyết được.
   - OpenCode đáng thử vì có thể dùng ChatGPT Plus/Pro trực tiếp theo docs hiện tại; không đồng nghĩa mọi provider consumer OAuth đều được phép.

3. **RE Command Center nên được định vị là một `model-agnostic RE agent harness / RE agent runtime + command center`**
   - Model là “brain” có thể thay.
   - Harness là toàn bộ app/runtime: planner, agent loop, context, memory/state, tool router, permissions, evidence store, verification, subagents, UI, sessions, logging.
   - `Agent ≈ model + harness + current goal/context`.
   - Giá trị dài hạn nằm ở **RE workflow + evidence + tool integration + verification**, không nằm ở việc khóa vào một model.

4. **MCP không nên là internal bus cho mọi thứ**
   - Tool do chính app kiểm soát và gọi thường xuyên: ưu tiên **native call / typed interface / narrow RPC**.
   - MCP dùng mạnh ở **interop boundary**: để Antigravity/Codex/Claude/Goose/Osaurus/IDE khác gọi RE tools.
   - Tool surface phải hẹp, typed, task-specific để giảm context/token/tool confusion.
   - Tư tưởng: **Internally use the narrowest/cheapest interface; externally expose MCP for interoperability.**

5. **Review độc lập**
   - Task quan trọng kiểu `Native bridge thay đổi ABI`:
     `Agent A implement -> Air fresh Agent B review -> Agent A fix -> human review`.
   - Air review chạy task riêng, fresh context, có thể chọn agent/model khác; không thay human review.

6. **Antigravity ToS là vùng phải rất cẩn thận**
   - Terms hiện ghi rõ third-party software/tool/service dùng để access Antigravity bằng Antigravity login là vi phạm.
   - Nhưng `agy` headless docs chính thức lại document scripting/programmatic stdout/JSON và cached credentials.
   - Vì vậy **official `agy` headless automation nội bộ** có cơ sở kỹ thuật là supported scripting, nhưng **Terms vẫn rộng**; không nên tự tuyên bố “100% compliant”.
   - Đường sạch nhất khi public product:
     - Antigravity gọi **MCP server của RE Command Center**, hoặc
     - dùng **official Antigravity SDK / Gemini API / Google Cloud path** phù hợp licensing.
   - Không copy/read/reuse OAuth token trực tiếp, không reverse-engineer endpoint, không biến Google AI consumer subscription thành provider backend công khai.

7. **Project principles**
   - Evidence-driven, not chat-driven.
   - Static hypothesis phải có đường xác minh bằng xref/decompiler/debugger/network/sandbox khi cần.
   - Mọi claim nên có provenance.
   - Untrusted binary strings/decompiled comments phải được coi là untrusted input vì prompt-injection vào RE agent là rủi ro thật.
   - Driver/malware actions phải tách trust boundary, không để coding agent mặc định có quyền load driver/thay boot config.

---

# 1. Bối cảnh người dùng và môi trường triển khai

## 1.1 Công việc và project

Bạn tập trung vào **Reverse Engineering / Malware Analysis / Windows Internals**, đang xây một ứng dụng Windows **WinUI native** tên làm việc là **RE Command Center**.

Các thành phần đã nhắc tới:

- Multiple AI Providers.
- MCP client/server.
- IDA integration.
- Burp integration.
- File triage.
- Hashes như MD5.
- Detect It Easy được bạn tự build DLL để tích hợp.
- Native DLL.
- Kernel-driver security/testing workflows.
- Agent orchestration.
- Independent review.
- Skills.
- Có ý định tự viết các MCP/tool adapters thay vì bê nguyên các MCP khổng lồ để tối ưu token và harness.

Hướng sản phẩm không phải “chatbox gắn vào IDA”, mà là **RE workbench + agent harness chuyên ngành**.

## 1.2 Toolchain hiện tại

- Windows là primary platform.
- Rider thay cho full Visual Studio vì muốn nhẹ hơn.
- Vẫn dùng **Visual Studio Build Tools**.
- Có/đã dùng: Antigravity 2.0 App, ChatGPT App, JetBrains Air, Zed Preview, VS Code.
- Có JetBrains Student license.
- Có GitHub Student Developer Pack.
- Có thể tự cấu hình build bằng CLI.
- Với driver/native: có thể dùng VS Build Tools + SDK/WDK/EWDK + WinDbg + VM/test machine thay vì bắt buộc cài full Visual Studio.

## 1.3 Máy công ty yếu hơn

Thông số đã thảo luận:

- Intel i7-6700.
- 16 GB RAM.
- 1 TB HDD.
- SSD 120 GB, ổ C gần đầy.

Khuyến nghị đã thống nhất:

- Daily lightweight: **VS Code + Antigravity official extension + optional Codex extension + VS Build Tools**.
- Chỉ mở Antigravity App/ChatGPT App khi cần, tránh chạy đồng thời quá nhiều IDE/agent.
- Không cài thêm Air/Zed/Rider/full VS nếu không có nhu cầu thực tế trên máy này.
- Chỉ để một coding agent active phần lớn thời gian.
- Chuyển samples, dumps, symbols, VMs, archives, repo cũ sang HDD; giữ project active/toolchain latency-sensitive trên SSD nếu có thể.
- Dọn Visual Studio Build Tools workloads không dùng.
- Ổ SSD gần đầy là bottleneck lớn hơn chuyện “editor nào nhẹ hơn vài trăm MB”.

---

# 2. Mental model: model, harness, agent, host, editor

## 2.1 Model

Model là “brain”:

- GPT family.
- Gemini family.
- Claude.
- Grok.
- Kimi.
- Mistral.
- Open/local model.

Model **không tự là coding agent hoàn chỉnh**.

## 2.2 Harness

Harness là phần biến model thành một hệ có thể làm việc:

```text
MODEL
  +
context management
  +
tools / shell / file editing
  +
permissions
  +
planning / agent loop
  +
memory / state
  +
verification
  +
skills / instructions
  +
subagents
  +
execution environment
  =
AGENTIC HARNESS
```

Osaurus mô tả rất sát tư tưởng này: model là interchangeable, còn memory/tools/context/identity là lớp đáng sở hữu.

## 2.3 Agent

Một cách tư duy hữu ích:

```text
Agent = Model + Harness + Goal + Current Context/State
```

Không phải định nghĩa tiêu chuẩn tuyệt đối, nhưng rất thực dụng để thiết kế.

## 2.4 Host / client / control plane

Ví dụ:

- Rider/Zed/Air có thể là nơi **host** một agent qua integration/ACP.
- Harness vẫn giữ phần planning/tool/runtime/auth của nó.
- Host cung cấp UI, code context, diff, worktree, review surface, editor intelligence.

Do đó cùng một Codex/Antigravity trong Rider và Air **không hoàn toàn giống hiệu quả**, vì host khác nhau.

---

# 3. Protocols và thuật ngữ quan trọng

## 3.1 ACP — Agent Client Protocol

Hiểu ngắn:

> **ACP = editor/client/app ↔ coding agent**

Nó giải quyết bài toán: không cần mỗi editor viết một integration riêng cho từng agent.

```text
Before:
Editor A <-> Agent X custom integration
Editor A <-> Agent Y custom integration
Editor B <-> Agent X custom integration
...

ACP:
Editor/Client <-> ACP <-> Agent
```

Zed công khai ACP như open standard; ACP Registry giúp một agent đăng ký một lần rồi xuất hiện ở nhiều ACP-compatible clients.

Ví dụ agent có ACP: Codex CLI, Claude Code, OpenCode, Gemini CLI, Kimi Code, v.v. tùy thời điểm.

## 3.2 MCP — Model Context Protocol

Hiểu ngắn:

> **MCP = agent/model host ↔ tools/data/context**

Ví dụ:

```text
Agent
  |
  +-- MCP -> IDA
  +-- MCP -> Burp
  +-- MCP -> database
  +-- MCP -> browser
  +-- MCP -> custom RE server
```

MCP không phải agent orchestrator; nó là interface/tool protocol.

## 3.3 A2A — Agent2Agent Protocol

Hiểu ngắn:

> **A2A = agent ↔ agent qua network protocol**

A2A dùng HTTP(S), JSON-RPC và hỗ trợ task/artifact/streaming; đây là peer-agent interoperability, không phải “chỉ CLI”.

A2A không đồng nghĩa Air. Air có thể orchestrate nhiều agents mà không cần các coding-agent apps expose A2A endpoint.

## 3.4 LSP — Language Server Protocol

Hiểu ngắn:

> **LSP = editor ↔ language intelligence server**

Ví dụ C# language server có thể cung cấp:

- go to definition,
- references,
- diagnostics,
- autocomplete,
- symbol info,
- rename support.

LSP không phải AI. AI agent có thể hưởng lợi từ language intelligence, nhưng LSP và agent protocol là hai lớp khác nhau.

## 3.5 `AGENTS.md`

`AGENTS.md` là file instructions cho agent ở repo/project:

- cách build,
- cách test,
- coding conventions,
- forbidden actions,
- repo layout,
- expected review rules.

Nó **không phải file “setup model”**. Nó là repository-level operational guidance.

## 3.6 Skills / `SKILL.md`

Skill là reusable package of instructions/knowledge/scripts.

Xu hướng chung hiện nay là **progressive disclosure**:

- Session start chỉ thấy tên + mô tả.
- Khi task match mới load toàn bộ `SKILL.md`.
- Giảm token/context noise.

Air và Antigravity đều dùng `.agents/skills/<skill>/SKILL.md`; nhiều agent khác cũng đi theo Agent Skills standard.

Khuyến nghị cho project:

```text
.agents/
  skills/
    winui-development/
    native-interop/
    mcp-development/
    re-triage/
    ida-analysis/
    burp-analysis/
    driver-build/
    security-review/
```

Tư duy đúng:

> Không phải “cài skills cho Air”, mà là **cài skills cho project**, để nhiều agent dùng chung.

## 3.7 Spec-driven development

Không cần hiểu học thuật:

```text
requirements
 -> design/spec
 -> tasks
 -> implementation
 -> verification
```

Nó giúp agent không “code trước rồi mới đoán requirement”.

## 3.8 Subagent

Main agent có thể delegate task hẹp cho agent con:

- explore repo,
- code một module,
- review,
- plan,
- research.

Subagent tốt vì giữ context phụ không làm bẩn main context.

## 3.9 Worktree

`git worktree` cho nhiều checkout cùng repo:

```text
main repo
  +-- worktree/task-A
  +-- worktree/task-B
```

Agent A và B có file tree riêng, giảm chuyện đạp lên nhau.

## 3.10 First-class login support

“First-class login” không chỉ là “OAuth tồn tại”.

Nó nghĩa là host/product tích hợp provider/harness như một citizen chính thức:

- UI login rõ ràng.
- Auth lifecycle do integration quản.
- Quota/account/model state được hiểu.
- Không cần unofficial adapter/wrapper.

Air hiện [Verified 2026-09-20] hỗ trợ Codex với ChatGPT account, Gemini CLI với Google account, Junie, và custom ACP agents; **Antigravity first-class chưa xuất hiện trong official supported-agent list hiện tại**, nên coi kế hoạch “Air first-class Antigravity” là future/needs re-check.

---

# 4. JetBrains Air — vai trò và workflow đúng

## 4.1 Air không chỉ là “chat với agent”

Air thiên về:

- task delegation,
- agent per task/model,
- worktree/isolation,
- parallel work,
- diff review,
- fresh-session automated review,
- human approval.

## 4.2 `Agent A implement -> Agent B review -> human review`

Ví dụ task quan trọng:

```text
"Native bridge thay đổi ABI"
```

Workflow:

```text
AIR
 |
 +-- Task implementation
 |      |
 |      +-- Agent A (e.g. Antigravity/Codex)
 |
 +-- diff
 |
 +-- Review with Agent
        |
        +-- fresh Agent B session
             focus:
             - calling convention
             - struct packing
             - alignment
             - ownership
             - lifetime
             - marshaling
             - exception boundary
             - ABI compatibility
 |
 +-- comments
 |
 +-- implementation agent fixes
 |
 +-- YOU review cuối
```

Điểm quan trọng: Air docs nói review là **separate task, fresh session, own context**, và có thể dùng agent/model khác. Nó giúp independent second pair of eyes, không thay human review.

## 4.3 Rider vs Air

```text
Rider
= IDE + debugger + deep code navigation + refactor + manual control

Air
= task/agent orchestration + worktree + independent review
```

Hai cái bổ sung nhau.

## 4.4 Air vs Zed

```text
Zed
= editor-first + ACP cockpit + nhiều agent + MCP/interop

Air
= task-first orchestrator + worktrees + review workflow
```

Nếu chỉ giữ một thứ bổ sung cho Rider trong workflow hiện tại: **Air có giá trị hơn Zed**.

---

# 5. Rider, Visual Studio, VS Code và XAML Hot Reload

## 5.1 Rider

Rider không phải IDE “chỉ dành cho game”. Nó là general .NET IDE, phù hợp:

- C#/.NET,
- WinUI project editing,
- managed/native workflows ở mức đáng kể,
- code navigation,
- refactor,
- debugging.

## 5.2 Visual Studio

Visual Studio vẫn có lợi thế ở hệ Microsoft chính thức:

- WinUI tooling,
- XAML runtime design tools,
- WDK/driver integration,
- Driver Verifier/deploy/signing workflows,
- XAML Hot Reload.

Bạn không bắt buộc phải cài full VS nếu tự build bằng CLI, nhưng full VS vẫn là “deepest official Windows tooling”.

## 5.3 XAML Hot Reload

XAML Hot Reload cho phép sửa XAML trong lúc app đang chạy và thấy UI cập nhật mà không restart toàn bộ app.

```text
Không Hot Reload:
edit XAML -> stop -> build -> run -> inspect

Có Hot Reload:
edit XAML -> running UI updates
```

Nó tăng tốc UI iteration; không phải requirement để build WinUI.

## 5.4 CLI-first build

Một hướng tốt cho project:

```text
build.ps1
  -Target App
  -Target Native
  -Target Driver
  -Target All

test.ps1
```

Scripts cần:

- deterministic,
- exit code đúng,
- output machine-readable nếu có thể,
- agent chạy được không phụ thuộc IDE.

---

# 6. Antigravity: extension, ACP, CLI, Skills, models, ToS

## 6.1 Rider extension vs ACP-hosted Antigravity

Không nên gọi là “y hệt”.

```text
Antigravity harness
   +-- Rider extension
   |     +-- Rider UI/context/integration
   |
   +-- ACP host
         +-- host-specific UI/workflow/context
```

Cùng family harness/model/account có thể cho capability cốt lõi gần nhau, nhưng host context khác nên UX và effectiveness khác.

## 6.2 Official IDE extensions

Google hiện có official Antigravity IDE extensions cho nhiều editor/IDE, trong đó có JetBrains và Zed.

Điều này khác với unofficial tool lấy consumer OAuth để làm provider riêng.

## 6.3 Skills

Antigravity mặc định `.agents/skills` và support `SKILL.md` standard.

Đây là lý do nên đưa skills về repo thay vì chỉ nằm trong một app riêng.

## 6.4 MCP

Antigravity support custom MCP servers. Hướng sạch:

```text
Official Antigravity
      |
      +-- MCP -> RE Command Center / RE MCP server
```

## 6.5 `agy` headless

Docs chính thức [Verified 2026-09-20]:

```text
agy -p "..."
--output-format text|json|stream-json
```

Headless mode được mô tả cho:

- scripts,
- CI,
- machine-readable output,
- dùng output trong program thay vì terminal UI.

Docs cũng nói headless dùng **cached credentials** nếu đã authenticate.

## 6.6 ToS tension — cực quan trọng

Terms lại ghi:

- không dùng Service “in connection with products not provided by us”;
- using third-party software/tools/services to access Service (ví dụ OpenClaw với Antigravity OAuth) là breach.

Vì vậy tồn tại tension:

```text
CLI docs:
official agy can be scripted/programmatically consumed

vs

Terms:
third-party software access to Antigravity is prohibited broadly
```

Kết luận thận trọng:

- Internal personal automation dùng **official `agy` documented headless interface** gần với supported scripting hơn OAuth-wrapper abuse.
- Nhưng không thể khẳng định chỉ vì “app tự spawn `agy` giống mình tự mở terminal” thì chắc chắn không vi phạm; Terms không phân biệt đơn giản theo việc có browser login hay không.
- Mở `cmd.exe` rồi SendKeys `agy` không làm hợp lệ hơn `Process.Start`.
- Không nên:
  - đọc/copy OAuth token,
  - reverse-engineer private endpoint,
  - impersonate official client,
  - expose consumer Antigravity subscription làm backend public cho user khác.
- Khi ship product:
  - dùng official SDK/API/Google Cloud path, hoặc
  - để Antigravity là client gọi MCP của bạn.
- Nếu business decision phụ thuộc compliance: xin clarification từ Google; đây không phải legal advice.

---

# 7. Codex / ChatGPT và subscription strategy

## 7.1 ChatGPT Plus

[Verified 2026-09-20] ChatGPT Plus hiện niêm yết **$20/month**, API billing riêng.

Plus cho:

- broader model/tool access,
- higher usage,
- reasoning,
- file analysis,
- deep research where available,
- Codex access theo plan/product availability.

## 7.2 ChatGPT Pro

Trong giai đoạn trao đổi có tiers Pro cao hơn, phù hợp người dùng chạy Codex dài/nặng. Tại 2026-09-20, OpenAI đang có thay đổi về Pro tiers và tạm dừng new sign-up cho một tier $200; pricing/limits nên check lại trước khi upgrade.

Decision rule:

```text
Plus đủ -> ở Plus
Codex/Air heavy daily + quota cản việc lặp lại -> cân nhắc Pro
```

## 7.3 Google AI Pro / Ultra

Antigravity docs hiện:

- Pro: quota cao, refresh theo 5-hour window tới weekly limit.
- Ultra: quota cao hơn, weekly limits cao hơn.
- Google đã công bố $100 Ultra tier (5x Pro capacity) và $200 top Ultra tier trong 2026.

Baseline:

```text
Google AI Pro
+
ChatGPT Plus
```

được đánh giá là sweet spot tốt cho bạn.

## 7.4 Lợi ích ngoài coding của Google AI Pro

Google AI Pro còn gói nhiều Google services/AI benefits. **Lưu ý cập nhật:** trong trao đổi cũ có nhắc 2 TB; trang Google One hiện tại 2026-09-20 đang liệt kê **5 TB hoặc 10 TB tùy specific AI Pro membership**, cùng nhiều AI benefits. Đây là ví dụ cho thấy benefit plan thay đổi nhanh, không nên hard-code con số cũ.

## 7.5 Có nên đổi Google AI Pro -> Claude/Grok?

Không mặc định.

Lý do giữ Google + OpenAI:

- hai provider families,
- hai harness,
- hai quota pools,
- Google benefits ngoài coding,
- Antigravity có nhiều model choices theo thời điểm,
- ChatGPT/Codex mạnh cho research/coding.

Chỉ đổi khi benchmark trên **repo thật của bạn** cho thấy một harness khác thắng rõ và lặp lại.

---

# 8. Các coding agent / harness đã thảo luận

## 8.1 OpenCode

[Verified 2026-09-20]

- 75+ providers, local models.
- Official docs hiện cho **ChatGPT Plus/Pro** sign-in.
- GitHub Copilot subscription cũng có path.
- OpenCode docs cảnh báo Anthropic cấm một số plugin consumer-subscription paths; không được suy diễn “provider nào cũng login consumer subscription được”.
- Rất đáng thử vì bạn có ChatGPT Plus mà không nhất thiết mua subscription mới.

## 8.2 Kimi Code

- ACP qua `kimi acp` (JSON-RPC stdin/stdout).
- Main agent + built-in subagents:
  - coder,
  - explore,
  - plan.
- Custom agents, tool allowlists, MCP tool matching.
- Dùng isolated context cho subagents.
- Có thể là harness thử nghiệm tốt, nhưng chưa có lý do mặc định thay Codex/Antigravity.

## 8.3 Grok Build

- Coding agent.
- TUI.
- Headless.
- ACP.
- Windows PowerShell install.
- Có thể dùng browser auth/API key tùy mode.
- Đáng theo dõi; subscription thứ ba chỉ nên mua nếu benchmark cho workload của bạn thắng rõ.

## 8.4 Mistral Vibe

- Unified agent cho Work / Code / Chat.
- Skills theo Agent Skills standard.
- Có tool approval.
- Hợp nếu muốn ecosystem Mistral/open models hoặc workflow unified productivity + code.

## 8.5 TRAE

Đã thảo luận như một all-in-one AI IDE/agent với autonomous/SOLO-style workflow, overlap đáng kể với Antigravity. Do đó không có lý do cài/mua chỉ để “thêm một agent” nếu chưa có bottleneck.

## 8.6 Orca

Orca là **Agent Development Environment / orchestrator cho CLI agents** hơn là model.

- Worktree-native.
- Mỗi task có worktree/branch/files/agent terminals riêng.
- Agent picker launch CLI process.
- Permission defaults có thể rất aggressive (`yolo`/bypass flags).

Với RE/native/kernel project:
- đổi sang manual/scoped permissions,
- không coi disposable worktree là security sandbox cho driver/malware.

Orca đáng xem như alternative/experiment cho Air, nhưng không nên dùng unofficial Antigravity-login path trái Google Terms.

## 8.7 GitHub Copilot App

[Verified 2026-09-20]

GitHub Copilot App:

- Windows/macOS/Linux.
- Agent-native desktop.
- Sessions từ issue/PR/prompt.
- Parallel sessions/workspaces/worktrees.
- Diff + terminal/browser review.
- Available on every Copilot plan including Student.
- BYOK cũng được hỗ trợ.

Với Copilot Student:
- model selection hiện là **Auto only**.
- unlimited code completions.
- AI-credit allowance có giới hạn.
- Vì bạn đã trải nghiệm nhanh hết allowance, nên dùng làm **bonus/secondary agent**, không phải primary.

## 8.8 Junie

Junie là JetBrains coding agent/harness. Air có integration riêng. Đã thảo luận ở vai trò agent đơn lẻ; Air vẫn là orchestration layer rộng hơn.

---

# 9. Zed, ACP và vai trò test bench

Zed phù hợp khi muốn:

- editor nhẹ,
- ACP host,
- test nhiều external agents,
- MCP/ACP interoperability,
- xem agent logs/context.

Với RE Command Center:

```text
Zed != dependency của app
```

App phải tự implement MCP/tool interfaces. Zed chỉ là một **external compatibility client/test bench**.

---

# 10. RE Command Center -> RE Agent Harness

## 10.1 Khi nào app chỉ là workbench?

Nếu bỏ model/agent loop và app chỉ có:

```text
WinUI UI
IDA integration
Burp integration
DIE DLL
PE metadata
hash
tabs
```

thì đúng hơn là **RE workbench / RE tool platform**.

## 10.2 Khi nào trở thành harness?

Khi toàn app có:

```text
RE Command Center
 |
 +-- Session manager
 +-- Model provider abstraction
 +-- Planner
 +-- Agent loop
 +-- Context manager
 +-- Memory / state
 +-- Tool router
 +-- Permission policy
 +-- Evidence store
 +-- Verification engine
 +-- Skills
 +-- Subagents
 +-- Review
 +-- UI
 +-- Logging / tracing
```

Lúc này toàn khối là **RE agent harness**; model là pluggable engine.

## 10.3 Kiến trúc canonical

```text
                    USER
                      |
                      v
          +-------------------------+
          |   WinUI Command Center  |
          +------------+------------+
                       |
                       v
          +-------------------------+
          |      RE AGENT CORE      |
          |-------------------------|
          | planner / task graph    |
          | context broker          |
          | memory/state            |
          | permissions             |
          | evidence/provenance     |
          | verifier                |
          | subagents/reviewer      |
          +-----------+-------------+
                      |
          +-----------+-----------+
          |                       |
          v                       v
   +-------------+          +-------------+
   | Model Router|          | Tool Router |
   +------+------+          +------+------+
          |                       |
   GPT/Gemini/...     +-----------+-------------------+
                      |           |                   |
                      v           v                   v
                    IDA         Burp                Native
                    RPC/MCP     RPC/MCP            DIE/PE/etc
```

## 10.4 Suggested specialist agents

```text
Supervisor / Orchestrator
  |
  +-- TriageAgent
  +-- StaticAnalysisAgent
  +-- DynamicAnalysisAgent
  +-- Network/WebAgent
  +-- EvidenceVerifier
  +-- ReportAgent
```

Mỗi specialist chỉ thấy tools cần thiết.

## 10.5 RE workflow nên evidence-driven

Ví dụ:

```text
Goal: "triage binary này"

hash + PE metadata
  ->
packer/compiler/signature
  ->
imports/sections/entropy
  ->
hypothesis
  ->
IDA decompile/xrefs
  ->
dynamic verification nếu cần
  ->
evidence correlation
  ->
report
```

Mọi kết luận quan trọng nên gắn:

```text
Claim
  +-- source/tool
  +-- address/function/request
  +-- raw/normalized evidence
  +-- confidence
  +-- verification status
```

## 10.6 Tool design để giảm token

Đừng expose “toàn bộ IDA” cho mọi turn.

Ví dụ `TriageAgent` chỉ có:

```text
hash_file
detect_packer
pe_headers
section_entropy
imports
signatures
strings_summary
```

`StaticAnalysisAgent`:

```text
get_binary_overview
get_function
get_callers
get_callees
get_xrefs
get_strings_for_function
rename_symbol
add_comment
```

`WebAgent`:

```text
burp_history
get_request
send_request
inspect_response
```

Đây là harness engineering thật sự.

---

# 11. Native/RPC vs MCP — nguyên tắc kiến trúc

## 11.1 Không MCP hóa mọi thứ

Nếu DIE là DLL do chính bạn build:

```text
RE Command Center -> direct DLL call
```

thường tốt hơn:

```text
RE Command Center -> MCP -> local wrapper -> DLL
```

nếu không cần interoperability boundary.

## 11.2 Khi dùng narrow RPC

Với IDA/Ghidra/Burp, custom plugin/daemon có thể trả compact typed results:

```text
get_function_summary(address)
get_xrefs(address)
get_call_graph(root, depth)
```

Ưu điểm:

- ít schema hơn,
- ít token hơn,
- deterministic hơn,
- dễ cache,
- dễ version.

## 11.3 Khi dùng MCP

MCP rất tốt ở external boundary:

```text
Antigravity
Codex
Claude
Goose
Osaurus
Zed
other hosts
     |
     +-- MCP -> RE Command Center
```

Khi đó sản phẩm của bạn trở thành RE capability provider cho hệ sinh thái.

---

# 12. RE/cyber harness reference projects

## 12.1 Rikugan

Rikugan là reference rất gần concept của bạn:

- IDA Pro + Binary Ninja.
- Own agent loop.
- Own context manager.
- In-process tool orchestration.
- Plan mode.
- Subagents.
- Session persistence.
- MCP client support.
- 60+ RE tools.

Điểm kiến trúc đáng học:

> Core host tools không cần đi qua MCP; MCP là extension path.

## 12.2 `ghidra-rpc` — Cellebrite Labs

- Ghidra chạy persistent daemon.
- CLI trả structured JSON.
- Coding agent chỉ cần shell là drive được RE.
- Load binary một lần, session “warm”.

Pattern:

```text
LLM agent -> narrow CLI/RPC -> persistent Ghidra daemon
```

## 12.3 `re-mcp`

- Headless MCP server.
- IDA + Ghidra backends.
- Multi-database.
- Supervisor/worker architecture.

Pattern:

```text
external agent -> MCP -> RE backends
```

## 12.4 ReAgent / Agentic Malware Analysis / Malyze

Các project đã thảo luận cho thấy các pattern:

- triage/static/dynamic specialist agents,
- hypothesis -> evidence -> verify,
- nhiều RE tools,
- MCP/CLI integration,
- IOC/YARA/network/static/dynamic pipelines.

Bài học quan trọng không phải copy tool list, mà là **evidence orchestration**.

## 12.5 CAI

Cập nhật [Verified 2026-09-20]:

- CAI open-source repo **đã archived**.
- Từng là cybersecurity AI framework có agents/tools/handoffs/tracing/HITL/MCP.
- Successor theo repo là **Cybersecurity Superintelligence (CSI)**.

Do đó CAI nên được xem là **reference architecture/research artifact**, không phải dependency active nên chọn mới.

---

# 13. General-purpose Windows agent harnesses đáng nghiên cứu

## 13.1 Osaurus

[Verified 2026-09-20]

Windows:

- Windows 11 x64 + ARM64 beta.
- **Native Windows runtime, no Electron**.
- Local-first.
- Local models hoặc cloud provider.
- Skills.
- Memory.
- Files/tools.
- MCP in/out.
- No-account local path.
- 12 GB VRAM requirement cho local model class được nêu trên Windows page.

Vì sao cực hợp để bạn nghiên cứu:

- triết lý model interchangeable,
- native-first,
- local-first,
- stable tool/plugin ABI,
- memory/skills/tool layer thuộc về user,
- gần triết lý RE Command Center.

Không có nghĩa nên lấy Osaurus làm dependency; nên **mổ architecture/UX**.

## 13.2 Hermes Agent

- Windows native install path.
- Persistent learning/memory orientation.
- Skills progressive disclosure.
- MCP.
- Per-server tool filtering.
- Subagents.
- General-purpose.

Điều nên học:

- skill lifecycle,
- filtered tool exposure,
- memory persistence.

Rủi ro:

- tool surface rộng,
- general assistant chứ không RE-specific,
- phải giữ permission boundary.

## 13.3 OpenClaw

- Native Windows Hub bằng WinUI.
- Hub tách khỏi Gateway.
- Windows node capabilities.
- local/remote/SSH gateway.
- local MCP server mode.

Điều đáng học cho RE Command Center:

- **native WinUI shell <-> separate runtime/gateway**.
- Process isolation.
- Capability exposure.
- Diagnostics/control plane.

## 13.4 Goose

- Open-source general agent.
- Desktop/CLI/API.
- MCP sâu.
- ACP support.
- Recipes.
- Subagents.
- Sandbox/security controls.

Điều đáng học:

- embeddability/API,
- open standards,
- reusable workflow recipes.

## 13.5 Jan

Điểm cực đáng chú ý:

- **Smart MCP tool routing**.
- Khi nhiều MCP servers, nó thu hẹp server/tools cần query cho từng turn.
- Có thể dùng lightweight routing model.
- Mục tiêu: giảm context size/token usage.

Đây là xác nhận mạnh cho quyết định tự thiết kế tool router RE hẹp của bạn.

## 13.6 LM Studio Bionic

Cập nhật so với trao đổi cũ:

- Bionic là app riêng cho coding/research/docs với local/open cloud models.
- Có Code projects, Work projects, checkpoint, inline diff.
- Đã thêm Skills.
- Blog hiện nói local agentic work trên “Mac and PC”; Windows availability thay đổi nhanh nên re-check download page trước khi phụ thuộc.
- Đáng theo dõi nếu muốn local/open-model harness.

## 13.7 Open Interpreter

- Windows desktop.
- Chọn workspace folder.
- Có thể điều khiển real desktop apps.
- Computer-use/GUI automation.

Với RE:
- chỉ dùng làm **fallback** khi target không có API/RPC/MCP.
- GUI automation kém deterministic, dễ vỡ khi UI đổi, tốn context.

## 13.8 AnythingLLM / OpenHands Agent Canvas

- AnythingLLM: local knowledge/agent/RAG-oriented.
- OpenHands Agent Canvas: control surface cho agentic work, local/browser/Docker/VM/cloud backends.

Hữu ích để tham khảo UX/control plane; overlap khá nhiều với Air/Orca nếu mục tiêu chỉ là coding orchestration.

---

# 14. GitHub Student Developer Pack

## 14.1 Expiry 2028

GitHub docs nói student validation **lasting 2 years**, sau đó revalidate.

Vì UI của bạn ghi:

```text
Your benefits will expire on July 02, 2028
```

và bắt đầu khoảng 02/07/2026, đây đúng là **2 năm**.

Nhưng:

- từng partner offer có thời hạn riêng;
- offer có thể bị partner thay đổi/remove;
- re-verification không reset mọi time-limited offer.

## 14.2 85 -> 83 tools

Từ snapshot cũ được tìm thấy trong trao đổi, delta từng được đối chiếu:

Các item không còn trong list 83:
- DigitalOcean.
- GitHub Community Exchange.
- SlideCoach.
- DailyBot.
- GitHub Certification Voucher.

Các item mới:
- Boot.dev.
- Clerk.
- Microsoft 365.

Số học:

```text
85 - 5 + 3 = 83
```

Nhưng không nên hiểu máy móc “5 cái bị xóa vĩnh viễn”:
- có item bị sunset thật,
- có item đổi classification,
- có item hết campaign/voucher,
- catalog/changelog có thể lệch nhau theo thời điểm.

## 14.3 83 tools — bảng đầy đủ


| Nhóm | Tool | Chức năng dễ hiểu | Với RE/RE Command Center | Với anh ấy (Spring Boot/React/Kotlin) |
|---|---|---|---|---|
| Domain / DNS / hosting | **Namecheap** | Domain, DNS, SSL, hosting; Student Pack: .me + SSL theo offer. | Có thể dùng cho landing page/docs của RE Command Center. | Có thể dùng cho portfolio/project Spring/React. |
| Cloud | **Microsoft Azure** | Cloud Microsoft: VM, storage, database, Functions, App Service, DevOps. | Lab/service phụ trợ, storage, test infra; không dùng cloud thay VM malware cô lập. | Deploy Spring Boot, DB, storage; credit student rất thực dụng. |
| Domain / DNS | **Name.com** | Domain registrar/hosting; Student Pack có domain chọn lọc miễn phí. | Domain docs/portfolio. | Domain portfolio/demo app. |
| Productivity | **Microsoft 365** | Word/Excel/PowerPoint/OneDrive và AI/benefit tùy eligibility. | Viết tài liệu, báo cáo, spec. | Tài liệu, học tập, báo cáo. |
| Domain / DNS | **.TECH** | Domain .TECH. | Có thể dùng cho project/portfolio, không thiết yếu. | Tương tự. |
| Knowledge / PM | **Notion** | Notes, wiki, project planning, Education plan + AI allowance theo offer. | RE notebook, architecture log, research log. | Project docs, study notes. |
| Source control | **GitHub** | Git hosting, issues, PR, Actions; Student có GitHub Pro. | Nền tảng chính cho repo, CI, issue, review. | Nền tảng chính. |
| AI coding | **GitHub Copilot** | Autocomplete, chat, coding agents; Student hiện auto model selection và AI-credit allowance riêng. | Agent phụ/free; không nên là primary nếu nhanh hết allowance. | Autocomplete/quick fixes rất hữu ích. |
| Learning | **DataCamp** | Khóa data/Python/SQL/ML. | Phụ nếu học data/ML malware. | Phụ. |
| Learning | **Boot.dev** | Backend, DevOps, Python, Go, TS. | Phụ cho backend/tooling. | Khá hợp backend. |
| IDE | **JetBrains** | Student license cho Rider/IntelliJ/PyCharm… renew hàng năm. | Rider là IDE chính. | IntelliJ IDEA rất hợp Spring/Kotlin. |
| Cloud / PaaS | **Heroku** | Deploy app/backend nhanh; offer credit theo tháng trong 24 tháng. | Service phụ trợ/demo. | Rất hợp deploy Spring Boot demo. |
| Learning | **Codedex** | Interactive learning Python/web/Git/CLI. | Phụ. | Phụ. |
| Database | **MongoDB** | NoSQL + Atlas + Compass + University/certification. | Chỉ dùng nếu app thực sự cần document DB. | Chỉ dùng nếu kiến trúc cần MongoDB. |
| Learning | **Educative** | Khóa Java/backend/system design/Python/ML. | System design, interview, backend knowledge. | Rất hợp Java/system design. |
| Testing | **Testmail** | Email test API, mailbox giả. | Ít dùng trừ app có email. | Hữu ích cho registration/reset/verification. |
| Remote admin | **Termius** | SSH/SFTP client đồng bộ nhiều thiết bị. | SSH Linux lab/server. | SSH VPS/backend. |
| Cloud dev | **GitHub Codespaces** | Dev environment cloud gắn repo. | Tốt cho code/backend/tooling; không thay Windows driver lab. | Tốt cho Spring/React. |
| Cloud | **Microsoft Azure (13–17)** | Azure benefit riêng nhóm tuổi 13–17. | Chỉ phù hợp nếu đúng điều kiện tuổi. | Tương tự. |
| Editor | **Visual Studio Code** | Editor nhẹ, extension ecosystem lớn. | Tốt cho máy công ty yếu, scripts/MCP. | Tốt cho React/Java nếu muốn nhẹ. |
| Observability | **Datadog** | Metrics/logs/traces/infra monitoring. | Chỉ khi có service/backend production. | Hữu ích khi deploy backend. |
| Backend platform | **Appwrite** | Auth, DB, storage, functions/hosting. | Có thể dùng cho prototype; overlap nếu tự viết backend. | Có thể overlap Spring Boot. |
| AI / data cloud | **Camber** | Agentic data science + CPU/GPU/storage + MCP. | Thử long-running agent/data workflows. | Phụ nếu có data/ML. |
| Learning | **FrontendMasters** | JS/TS/frontend chuyên sâu. | Phụ. | Rất hợp React. |
| Payments | **Stripe** | Payments/billing API; Student credit phí. | Chỉ khi app có thanh toán. | Chỉ khi app có billing/payment. |
| Secrets | **1Password** | Password/API key/SSH key + developer tools. | Rất nên dùng để quản secrets cá nhân. | Rất nên dùng. |
| Hosting | **GitHub Pages** | Static hosting từ repo. | Docs/landing page. | Portfolio/docs/frontend tĩnh. |
| Auth / SaaS | **Clerk** | Authentication, user management, billing. | Chỉ khi không muốn tự auth. | Có thể overlap Spring Security. |
| Dev bundle | **Microsoft Visual Studio Dev Essentials** | Visual Studio Community + cloud/training benefits tùy thời điểm. | Hữu ích vì Windows/WinUI/WDK ecosystem. | Ít quan trọng hơn IntelliJ. |
| Templates | **Notion Template Collection** | Templates học tập/project/portfolio. | Phụ. | Phụ. |
| Productivity | **PomoDone** | Pomodoro/time tracking. | Tùy thói quen. | Tùy thói quen. |
| Community | **GitHub Campus Experts** | Training xây cộng đồng sinh viên. | Không phục vụ trực tiếp RE. | Không phục vụ trực tiếp stack. |
| Web design | **Bootstrap Studio** | GUI builder Bootstrap. | Ít liên quan. | Có thể prototype UI web. |
| Error tracking | **Sentry** | Crash/error/performance telemetry. | Rất hữu ích cho app/backend nếu có telemetry. | Rất hữu ích Spring/React. |
| Design assets | **Icons8** | Icons/illustrations/photos. | Có thể dùng UI WinUI. | Có thể dùng React/mobile. |
| Learning | **Scrimba** | Interactive frontend/Python. | Phụ. | Hợp React ở mức học. |
| IoT | **Arduino** | Arduino Cloud/hardware benefits. | Chỉ khi làm hardware/IoT. | Chỉ khi làm IoT. |
| Git tooling | **GitLens** | Git blame/history/graph trong VS Code. | Hữu ích khi nhiều agent tạo thay đổi. | Hữu ích. |
| Data notebooks | **Deepnote** | Cloud/Jupyter-compatible notebooks. | Phụ cho analysis/data. | Phụ. |
| Scraping | **Zyte** | Scrapy Cloud/crawling. | Có thể phục vụ OSINT/research hợp pháp. | Phụ. |
| Testing | **BrowserStack** | Browser/device cloud. | Ít liên quan WinUI; có thể test web surfaces. | Rất hợp React và mobile/KMP. |
| Cloud dev | **LocalStack** | Giả lập AWS local. | Chỉ khi app dùng AWS. | Rất hữu ích nếu Spring dùng AWS. |
| Observability | **New Relic** | APM/logs/metrics. | Chỉ khi có backend/service. | Hữu ích production. |
| Design assets | **IconScout** | Icons/3D/Lottie. | Có thể dùng UI. | Có thể dùng UI. |
| Code quality | **CodeScene** | Behavioral code analysis, hotspots, code health, tech debt. | Rất đáng thử để tránh AI-generated codebase xuống cấp. | Rất đáng thử cho Spring/React/Kotlin. |
| Testing | **LambdaTest** | Cross-browser/device testing. | Ít liên quan core WinUI. | Hợp React. |
| Web QA | **Polypane** | Browser dev chuyên responsive/accessibility. | Ít liên quan. | Hợp frontend React. |
| IoT | **Adafruit** | Adafruit IO+/hardware benefit. | Chỉ khi IoT. | Chỉ khi IoT. |
| Git GUI | **GitKraken** | Git GUI/graph/PR/issue. | Hợp workflow nhiều branch/worktree. | Hợp. |
| Design | **Visme** | Presentation/infographic/docs. | Phụ. | Phụ. |
| Productivity | **HazeOver** | Focus app cho macOS. | Không đáng quan tâm trên Windows. | Không đáng quan tâm nếu Windows. |
| Web forms | **Pageclip** | Backend đơn giản cho static forms. | Ít dùng. | Ít dùng nếu đã có Spring Boot. |
| Interview | **AlgoExpert** | Coding interview/DSA. | Phụ cho interview. | Hợp interview. |
| Profiling | **Blackfire** | Performance profiler thiên PHP. | Ít liên quan. | Ít liên quan. |
| Icons | **Octicons** | Open-source GitHub icon set. | UI asset phụ. | UI asset phụ. |
| Visualization | **ToDiagram** | JSON/YAML/CSV/XML -> diagram. | Rất hữu ích visualize MCP schema/dataflow. | Hữu ích visualize API/data. |
| Git GUI | **GitHub Desktop** | Git GUI đơn giản. | Phụ nếu đã dùng Rider/GitKraken. | Phụ. |
| CI | **Travis CI** | Continuous integration. | GitHub Actions thường đủ; vẫn là lựa chọn. | Tương tự. |
| Image optimization | **Imgbot** | Tự tối ưu ảnh trong repo. | Phụ. | Hữu ích frontend. |
| Interview | **InterviewCake** | Interview prep + DSA. | Phụ. | Hợp interview. |
| Cross-platform dev | **Xojo** | Cross-platform desktop/mobile/web tool. | Không cần vì đang WinUI. | Không cần với stack hiện tại. |
| Secrets | **Doppler** | Secrets/env management theo môi trường. | Rất hữu ích cho provider keys/MCP/backend. | Rất hữu ích Spring deployment. |
| HTTP tooling | **Requestly** | Intercept/modify/mock HTTP request/response. | Rất hợp Burp/API/MCP HTTP test. | Rất hợp React + Spring API mocking. |
| Git GUI | **Tower** | Git client chuyên nghiệp. | Phụ. | Phụ. |
| GIS | **CARTO** | Spatial data analysis/maps. | Ít liên quan. | Ít liên quan. |
| Database IDE | **SQLGate** | GUI/IDE quản SQL databases. | Hữu ích nếu app có SQL. | Hữu ích Spring + SQL. |
| Testing | **Codecov** | Code coverage reporting. | Rất hữu ích để buộc AI changes có test. | Rất hữu ích Java/Kotlin/JS/TS. |
| Learning | **GoRails** | Ruby/Rails tutorials. | Không liên quan. | Không liên quan. |
| Web security | **AstraSecurity** | Website firewall/malware scanner. | Không phải RE workstation tool; chỉ web deployment. | Chỉ khi có website public. |
| SQL | **PopSQL** | Collaborative SQL editor. | Phụ. | Hữu ích nếu làm nhiều SQL. |
| Blockchain | **Blockchair** | Blockchain API. | Chỉ khi use case blockchain. | Chỉ khi use case blockchain. |
| Mobile Git | **WorkingCopy** | Git client iPhone/iPad. | Phụ. | Phụ. |
| Static analysis | **DeepScan** | Static analysis JS. | Ít liên quan core. | Có ích React/JS/TS. |
| Analytics | **SimpleAnalytics** | Privacy-friendly web analytics. | Landing/docs nếu cần. | Hợp website. |
| App analytics | **Appfigures** | App Store analytics/ASO. | Ít liên quan hiện tại. | Có ích nếu phát hành mobile. |
| WordPress | **Themeisle** | WordPress theme Neve. | Không liên quan. | Không liên quan. |
| Feature flags | **ConfigCat** | Feature flag service. | Hữu ích nếu app có staged rollout. | Rất hợp backend/frontend rollout. |
| Monitoring | **Honeybadger** | Error/uptime/cron monitoring. | Phụ nếu có service. | Hữu ích backend. |
| Localization | **POEditor** | Localization management. | Hữu ích nếu app đa ngôn ngữ. | Hữu ích React/Kotlin. |
| Java web | **Vaadin** | Java web UI framework. | Không liên quan app WinUI. | Có thể dùng nhưng overlap React. |
| Feature flags | **DevCycle** | Feature flag management. | Hữu ích nếu production rollout. | Rất hợp khi cần rollout an toàn. |
| Password manager | **Dashlane** | Password manager. | Dùng nếu không chọn 1Password. | Dùng nếu không chọn 1Password. |
| Learning | **SymfonyCasts** | PHP/Symfony tutorials. | Không liên quan. | Không liên quan. |

## 14.4 Những tool đáng claim/khai thác nhất cho bạn

Nhóm ưu tiên cao:

```text
JetBrains
GitHub Pro
1Password
Doppler
Requestly
Codecov
CodeScene
Sentry
Termius
ToDiagram
Azure
LocalStack (nếu dùng AWS)
GitKraken/GitLens
Camber (experiment)
```

Không cần “activate 83 tool cho đủ”; chỉ claim offer có giá trị thật và có deadline/term đáng kể.

## 14.5 GitHub Copilot Student

[Verified 2026-09-20]

- Unlimited code completions.
- Models qua **Auto model selection only**.
- Có AI-credit allowance, không phải unlimited agent.
- Credits không carry over.
- User experience của bạn: agent/chat dùng nhanh hết allowance — phù hợp với thiết kế plan hiện tại.

Vì vậy:

```text
Copilot Student
= free bonus / quick agent / autocomplete

không =
primary long-horizon agent
```

## 14.6 GitHub Copilot App

Dùng được trên Student plan và Windows.

Có:

- issue/PR/prompt sessions,
- parallel sessions,
- branch/worktree,
- diff,
- terminal/browser,
- GitHub-native merge flow,
- BYOK path.

Với bạn: đáng cài vì miễn phí, nhưng giới hạn Student làm nó hợp với task nhỏ hơn.

## 14.7 AIIDEList

AIIDEList là **curated directory**, không phải canonical exhaustive registry.

Nó chứa:

- IDE,
- coding agent,
- editor extension,
- developer workflow tool.

Nó tốt để discover/filter Windows/pricing/open-source, nhưng không nên hiểu “toàn bộ AI IDE đang tồn tại”.

Các source discovery nên dùng song song:

- AIIDEList.
- ACP Registry.
- Awesome AI Coding Agents.
- Official docs/changelogs của từng vendor.

---

# 15. Tech debt — khái niệm và ý nghĩa cho cả hai project

## 15.1 Định nghĩa dễ hiểu

Technical debt:

> chọn một cách nhanh/dễ hôm nay làm chi phí sửa đổi/bảo trì tương lai tăng lên.

Không đồng nghĩa “code có bug”.

Code có thể chạy đúng nhưng:

- khó hiểu,
- coupled,
- khó test,
- khó thay,
- sửa một chỗ vỡ nhiều chỗ.

## 15.2 Ví dụ Spring Boot

Bad growth pattern:

```text
OrderController
  +-- validation
  +-- DB
  +-- pricing
  +-- payment
  +-- email
  +-- logging
  +-- mapping
```

Dần thành 2000 dòng.

Healthy direction:

```text
Controller
  -> OrderService
      +-- PricingService
      +-- PaymentService
      +-- NotificationService
      +-- Repository
```

## 15.3 Ví dụ React

Tech debt:

```text
App.jsx
  +-- auth
  +-- users
  +-- products
  +-- cart
  +-- checkout
  +-- dialogs
  +-- theme
  +-- API calls
  +-- 2500 lines
```

Hoặc copy `fetch("/api/user")` ở 17 component.

## 15.4 Kotlin/KMP

Tech debt có thể đến từ:

- nhồi platform-specific assumptions vào `commonMain`,
- `expect/actual` lan tràn không có abstraction rõ,
- UI gọi database/platform API trực tiếp,
- workaround chéo platform khó maintain.

## 15.5 RE Command Center

Ví dụ nguy hiểm:

```text
MainViewModel.cs
  +-- IDA
  +-- Burp
  +-- DIE
  +-- OpenAI
  +-- Gemini
  +-- MCP
  +-- hashing
  +-- database
  +-- agent loop
  +-- 8000 lines
```

Hướng giảm debt:

```text
UI
 -> RE Harness
     +-- Model Router
     +-- Tool Router
     +-- Agent Runtime
     +-- Evidence Store
     +-- Adapters
```

## 15.6 “Tool bloat” cũng là architectural debt

Ví dụ expose 90 MCP tools vào mọi turn:

- tốn context,
- model chọn nhầm,
- schema maintenance nặng,
- version khó,
- test khó.

Thiết kế specialist-specific toolset là cách trả debt trước khi nó phình.

---

# 16. CodeScene, Codecov, Sentry — ba câu hỏi khác nhau

```text
CodeScene:
"Code nào đang trở thành gánh nặng bảo trì?"

Codecov:
"Test đã cover bao nhiêu code?"

Sentry:
"App thật ngoài production đang lỗi ở đâu?"
```

## 16.1 CodeScene

Điểm mạnh là behavioral analysis:

```text
code health
+
Git change frequency
=
hotspot
```

Một file hơi xấu nhưng bị sửa 90 lần/tháng thường đáng ưu tiên hơn file cực xấu nhưng 4 năm không ai chạm.

Đây là lý do CodeScene hợp với codebase có AI agent tạo thay đổi nhanh.

## 16.2 Codecov

Hợp cả:

- Java,
- Kotlin,
- JS/TS,
- .NET tùy pipeline.

Dùng quality gate:

```text
Agent change
 -> tests
 -> coverage
 -> PR check
```

## 16.3 Sentry

Runtime error/performance telemetry:

- Spring Boot.
- React.
- backend/service.
- app telemetry nếu integration phù hợp.

---

# 17. Tool stack cho anh ấy: Spring Boot + React + Kotlin Native/KMP

Ưu tiên:

```text
IntelliJ IDEA (JetBrains Student)
GitHub Pro
CodeScene
Codecov
Sentry
Requestly
Doppler
1Password
BrowserStack
Termius
```

Theo nhu cầu:

```text
deploy -> Azure / Heroku
AWS -> LocalStack
React learning -> FrontendMasters
Java/system design -> Educative
email -> Testmail
feature rollout -> ConfigCat / DevCycle
localization -> POEditor
```

## 17.1 Requestly workflow

React frontend đang cần endpoint Spring chưa xong:

```http
GET /api/users/123
```

Requestly có thể mock JSON để frontend tiếp tục.

Cũng có thể đổi response 200 -> 500 để test error UI.

## 17.2 BrowserStack

Đặc biệt hữu ích nếu Kotlin Native/KMP hướng mobile:

- React web trên browser matrix.
- Android/iOS real device cloud.

## 17.3 Appwrite/Clerk không mặc định cần

Nếu đã có Spring Boot/Spring Security:

- Appwrite backend có thể overlap.
- Clerk auth có thể overlap.

Dùng chỉ khi muốn outsource một capability cụ thể.

---

# 18. Microsoft account / VS Code Settings Sync trên 2 máy

Nội dung nhỏ nhưng đã được hỏi ở đầu thread:

Nếu hai máy login cùng Microsoft/GitHub account trong VS Code:

- Settings Sync có thể sync extensions và global enablement state nếu mục Extensions được bật.
- Nếu máy 2 **không sync Extensions**, máy 2 không tự áp bộ extension từ cloud.
- Extension trên máy 1 không tự bị xóa chỉ vì máy 2 tắt sync Extensions.
- Cloud sync data không tự bị xóa chỉ vì một máy không sync category đó.
- Xóa cloud data là hành động riêng.
- Profiles có thể dùng để giữ bộ extension khác nhau theo machine/context.

Với máy công ty yếu:

```text
sync:
settings
keybindings
snippets

optional:
extensions = off
```

để máy công ty không kéo theo toàn bộ extension stack của máy cá nhân.

---

# 19. Security / trust boundaries cho RE, malware, native và driver

## 19.1 Worktree không phải sandbox bảo mật

Git worktree chỉ cách ly files/branches.

Nó **không** cách ly:

- kernel driver,
- malware sample,
- registry,
- system services,
- network,
- credentials,
- host filesystem ngoài repo.

## 19.2 Driver workflow

Agent có thể:

- edit source,
- build,
- lint,
- run unit tests.

Nhưng các action:

- sign/load driver,
- modify boot config,
- disable protections,
- run dangerous kernel tests,

nên yêu cầu explicit human approval và preferably VM/test machine.

## 19.3 Untrusted content

Strings/comments/decompiled text lấy từ binary có thể chứa prompt-like content.

Harness phải coi chúng là **data**, không phải instructions.

Một pattern:

```text
System/Skill instructions
    >
trusted tool metadata
    >
untrusted binary/web content
```

và không cho untrusted text thay đổi permissions/tool policy.

---

# 20. Roadmap triển khai RE Command Center

## Phase 1 — nền tảng

- Tách UI khỏi agent runtime.
- Model provider interface.
- Tool interface typed.
- Session/state.
- Deterministic build/test scripts.
- Logging/tracing.

## Phase 2 — tool architecture

- DIE direct/native integration.
- IDA narrow adapter/RPC.
- Burp adapter.
- MCP gateway cho external clients.
- Tool schemas ngắn, task-oriented.
- Cache và pagination/chunking cho output lớn.

## Phase 3 — agent loop

- Goal.
- Plan.
- Tool selection.
- Observe.
- Update hypothesis.
- Verify.
- Report.

## Phase 4 — evidence layer

Data model gợi ý:

```text
Finding
  id
  claim
  category
  confidence
  status
  evidence[]
  source_tool
  source_location
  timestamp
  verifier
```

Status:

```text
hypothesis
supported
verified
contradicted
unknown
```

## Phase 5 — specialist agents

- TriageAgent.
- StaticAgent.
- DynamicAgent.
- NetworkAgent.
- Verifier.
- Reporter.

Tool allowlist riêng từng agent.

## Phase 6 — human-in-the-loop

Permission classes:

```text
READ
LOW_RISK_WRITE
EXECUTE
NETWORK
DEBUG
PATCH_BINARY
DRIVER
HOST_CRITICAL
```

Các class nguy hiểm phải approval.

## Phase 7 — interoperability

Expose selected capabilities qua MCP:

```text
get_binary_overview
get_function_summary
get_xrefs
get_call_graph
get_triage_report
query_evidence
```

Để external agent dùng được mà không lộ toàn internal surface.

## Phase 8 — review/evaluation

- Golden samples.
- Repeatable RE tasks.
- Measure:
  - correctness,
  - tool calls,
  - tokens,
  - latency,
  - hallucination rate,
  - verified findings,
  - false positives.
- Cross-model benchmark:
  - GPT,
  - Gemini,
  - Claude,
  - local model.

Mục tiêu là đo **harness + model pair**, không chỉ leaderboard model.

---

# 21. Workflow đề xuất hằng ngày

## Task nhỏ / interactive

```text
Rider
  -> Antigravity extension hoặc Codex
  -> build/test
  -> manual debug
```

## Task lớn

```text
Air
  -> agent task/worktree
  -> implementation
  -> tests
  -> diff
```

## Task high-risk

```text
Air
  -> Agent A implement
  -> Agent B fresh review
  -> A fixes
  -> human review
```

## RE investigation

```text
RE Command Center
  -> Triage
  -> Static
  -> hypothesis
  -> dynamic/network verification
  -> evidence
  -> report
```

---

# 22. Những quyết định không nên đảo lại chỉ vì hype

- Không đổi IDE mỗi khi có agent mới.
- Không mua 3–5 AI subscriptions chỉ để có “nhiều model”.
- Không MCP hóa mọi native component.
- Không dùng worktree như security sandbox.
- Không để coding agent mặc định có driver/system privileges.
- Không phụ thuộc RE Command Center vào Zed/Air/Antigravity.
- Không hard-code business model vào một vendor consumer subscription.
- Không tin kết luận RE chỉ vì model nói tự tin.
- Không dùng một giant prompt/tool list thay cho proper router/skills/context design.
- Không coi Copilot Student là unlimited agent plan.
- Không coi “same model” = “same agent quality”; harness/host matters.

---

# 23. Những điểm đã thay đổi/cần cập nhật so với các lượt trao đổi cũ

1. **Google AI Pro storage**: con số cũ trong chat không nên giữ cố định; current Google One page liệt kê storage cao hơn tùy membership.
2. **CAI**: đã archived, successor là CSI.
3. **LM Studio Bionic**: phát triển nhanh; current blog đã nói local work trên Mac/PC và Skills đã xuất hiện.
4. **AIIDEList**: số tool thay đổi liên tục; current landing page khoảng 250 entries.
5. **Copilot Student**: Auto model selection only là policy hiện tại.
6. **Antigravity Terms**: vẫn cấm third-party access wording rất rộng.
7. **`agy` headless**: docs chính thức hỗ trợ scripting/program output + cached credentials, tạo vùng cần đọc ToS cẩn thận khi embed.
8. **Air**: current official supported agents list có Claude Agent, OpenAI Codex, Gemini CLI, Junie, custom ACP; Antigravity first-class chưa phải official entry tại thời điểm tổng hợp.
9. **OpenCode**: current docs công khai ChatGPT Plus/Pro path; không được suy rộng sang Google Antigravity OAuth.
10. **Osaurus Windows**: đang beta, native Windows 11 x64/ARM64, no Electron.

---

# 24. Tài liệu nguồn chính đã kiểm tra

## Protocols / agents

- JetBrains Air supported agents: https://www.jetbrains.com/help/air/supported-agents.html
- JetBrains Air agentic review: https://www.jetbrains.com/help/air/agentic-review.html
- JetBrains Air Skills: https://www.jetbrains.com/help/air/skills.html
- Zed ACP: https://zed.dev/acp
- Zed ACP Registry: https://zed.dev/blog/acp-registry
- A2A spec: https://a2a-protocol.org/v0.2.6/specification/

## Google Antigravity

- Terms: https://antigravity.google/terms
- FAQ: https://www.antigravity.google/docs/faq/
- CLI headless: https://antigravity.google/docs/cli/headless/
- MCP: https://antigravity.google/docs/mcp
- Skills: https://www.antigravity.google/docs/skills
- Models: https://www.antigravity.google/docs/models/
- Plans: https://www.antigravity.google/docs/plans/
- Pricing: https://antigravity.google/pricing
- SDK tools: https://www.antigravity.google/docs/sdk/tools/
- IDE extensions blog: https://www.antigravity.google/blog/antigravity-ide-extensions

## OpenAI

- ChatGPT Plus: https://help.openai.com/en/articles/6950777-what-is
- ChatGPT Pro tiers: https://help.openai.com/en/articles/9793128-what-is-chatgpt-pro/

## Windows / WinUI

- XAML Hot Reload: https://learn.microsoft.com/visualstudio/xaml-tools/xaml-hot-reload
- Windows AI/Copilot setup: https://learn.microsoft.com/windows/apps/how-tos/ai-setup

## GitHub Student / Copilot

- Student Pack terms: https://docs.github.com/en/education/about-github-education/github-education-for-students/github-terms-and-conditions-for-the-student-developer-pack
- Student Pack catalog: https://education.github.com/pack
- Copilot plans: https://docs.github.com/en/copilot/get-started/plans
- Copilot billing/AI credits: https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/billing
- Student Auto model change: https://github.blog/changelog/2026-06-24-changes-to-model-selection-for-free-and-student-plans/
- Copilot App all plans: https://github.blog/changelog/2026-07-07-github-copilot-app-available-to-all/
- Copilot App repo: https://github.com/github/app

## General agent harnesses

- Osaurus Windows: https://osaurus.ai/windows
- Osaurus philosophy/docs: https://osaurus.ai/about , https://docs.osaurus.ai/
- Hermes docs: https://hermes-agent.nousresearch.com/docs/
- Hermes MCP: https://hermes-agent.nousresearch.com/docs/user-guide/features/mcp
- Hermes Skills: https://hermes-agent.nousresearch.com/docs/user-guide/features/skills
- OpenClaw Windows: https://docs.openclaw.ai/platforms/windows
- Goose: https://block.github.io/goose/
- Jan MCP routing: https://www.jan.ai/docs/desktop/integrations/mcp-servers
- LM Studio Bionic: https://lmstudio.ai/docs/bionic
- Bionic Skills: https://lmstudio.ai/blog/skills
- Open Interpreter desktop: https://www.openinterpreter.com/docs/desktop/install
- OpenHands Agent Canvas: https://github.com/OpenHands/docs/blob/main/openhands/usage/agent-canvas/overview.mdx
- Orca supported agents: https://www.onorca.dev/docs/agents/supported
- Orca worktrees: https://www.onorca.dev/docs/model/worktrees
- OpenCode providers: https://opencode.ai/docs/providers
- Kimi ACP: https://www.kimi.com/code/docs/en/kimi-code-cli/reference/kimi-acp
- Kimi agents/subagents: https://www.kimi.com/code/docs/en/kimi-code-cli/customization/agents
- Mistral Vibe: https://docs.mistral.ai/vibe
- Mistral Skills: https://docs.mistral.ai/vibe/code/cli/skills
- Grok Build: https://docs.x.ai/build/overview

## RE/security references

- Rikugan: https://github.com/buzzer-re/Rikugan
- Rikugan architecture/AGENTS: https://github.com/buzzer-re/Rikugan/blob/main/AGENTS.md
- Cellebrite ghidra-rpc: https://github.com/cellebrite-labs/ghidra-rpc
- RE-MCP: https://github.com/jtsylve/re-mcp
- CAI archive: https://github.com/aliasrobotics/cai

## Other references

- VS Code Settings Sync: https://code.visualstudio.com/docs/configure/settings-sync
- CodeScene (Student Pack listing): https://education.github.com/pack
- AIIDEList: https://aiidelist.com/

---

# 25. Checklist “đọc lại trước khi triển khai”

```text
[ ] RE Command Center không phụ thuộc vendor-specific model
[ ] Model Provider abstraction tách khỏi Agent Runtime
[ ] Internal tools không MCP hóa vô ích
[ ] External MCP surface nhỏ + typed + versioned
[ ] Tool routing theo specialist agent
[ ] Skills đặt ở .agents/skills
[ ] Evidence provenance là first-class data
[ ] Untrusted binary text không được coi là instruction
[ ] Permissions chia risk class
[ ] Driver actions cần human approval
[ ] Build/test deterministic CLI
[ ] Agent A / Agent B review cho ABI/security change
[ ] Benchmark harness + model pair
[ ] Đo token/tool-call/false-positive
[ ] Subscription chỉ upgrade khi quota là bottleneck
[ ] Re-check Google Antigravity Terms trước khi public integration
[ ] Re-check Air first-class Antigravity status trước khi migrate workflow
[ ] Re-check Student Pack offer trước khi redeem vì partner offers thay đổi
```

---

# 26. Một câu định vị sản phẩm nên giữ

> **RE Command Center là một Windows-native, model-agnostic Reverse Engineering Agent Harness: nó quản task/context/tools/evidence/verification và cho phép bất kỳ model phù hợp nào trở thành một RE agent có kiểm soát.**

Định vị này phản ánh đúng nhất toàn bộ hướng kiến trúc đã hình thành qua chuỗi trao đổi.
