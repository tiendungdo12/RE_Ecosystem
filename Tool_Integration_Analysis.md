> [!CAUTION]
> **HISTORICAL / RESEARCH — terminology clarification, 13 October 2026.** Earlier Harness/platform ownership and implementation/metric claims below are preserved research, not current contracts. [Canonical architecture](docs/ARCHITECTURE.md) assigns state/policy/evidence/persistence to RE Platform Core and reserves AI Harness for optional H7 RE Agent Runtime. The [following architecture conversation](docs/reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md) clarifies the source vision. Use [CURRENT](docs/CURRENT_STATE.md) for actual source status.

# 🔬 Phân Tích Toàn Diện: Chiến Lược Tích Hợp Tool Hệ Sinh Thái RE

> Cập nhật: 2026-07-08 | Đánh dấu lại: 20/09/2026
> **Trạng thái: HISTORICAL RESEARCH / SUPERSEDED.** Phần lớn ma trận phía dưới được viết cho app cũ và có tool/version/placement claims chưa kiểm chứng. Chỉ dùng như nguồn ý tưởng, không làm contract, inventory source hiện tại, hay bằng chứng hiệu năng.
> **Nguồn chuẩn hiện tại:** [`STRATEGY.md`](STRATEGY.md), [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/INTEROPERABILITY.md`](docs/INTEROPERABILITY.md) và source code. RE Core/Harness là platform; Command Center là tùy chọn. Deterministic triage chạy direct/native/subprocess.

---

## PHẦN A: NGUYÊN TẮC CỐT LÕI

### Triết lý thiết kế

1. **Library-first:** Ưu tiên tích hợp dạng thư viện (import/P/Invoke). Chỉ dùng subprocess cho tool Java (bắt buộc).
2. **Source-first:** Quản lý source code thay vì tải binary. Build trong pipeline cài đặt.
3. **[LỊCH SỬ] Một process Python duy nhất:** Kiến trúc cũ gom tool Python vào một MCP server. Kiến trúc hiện tại chuyển **Phase 1 sang direct/native/subprocess không MCP**; MCP dành cho Agentic Lane/interoperability.
4. **Giao tiếp đa ngôn ngữ qua C ABI:** C++, Rust đều expose `extern "C"` → C# gọi qua P/Invoke (overhead thấp; đo end-to-end bằng benchmark).
5. **Mô hình Phân tích Lai (Hybrid Analysis):** Quy trình "Cơ bắp" (Unpacking, Dumping, Triage) do C# thiết lập quy tắc tĩnh (Deterministic) để đảm bảo không làm hỏng máy. Não bộ (Phân tích, rà soát mã giả, tra cứu web) được giao cho Cloud AI (hoặc Local AI) xử lý Agentic thông qua Function Calling/Tool Calling.

### Ma trận giao tiếp đa ngôn ngữ

```
┌───────────────────────────────────────────────────────────────────────────┐
│                       C# / WinUI 3 (.NET 10 Runtime)                      │
│                                 App chính                                 │
├────────────┬────────────┬─────────────┬──────────────────────┬────────────┤
│  P/Invoke  │  P/Invoke  │  C# Direct  │      MCP stdio       │  HTTP/SSE  │
│  (C ABI)   │  (C ABI)   │  (cùng CLR) │      (JSON-RPC)      │   (REST)   │
├────────────┼────────────┼─────────────┼───────────┬──────────┼────────────┤
│ wintrust   │ binwalk    │ Hash (MD5/   │ lief      │ Ghidra   │ IDA Pro    │
│ .dll       │ _core.dll  │ SHA1/SHA256) │ capstone  │ JADX     │ (Plugin)   │
│            │ die_core   │ Dumper      │ frida     │ Burp     │            │
│            │ .dll       │ KsDumper    │ qiling    │ Suite    │            │
│            │            │             │ memprocfs │          │            │
└────────────┴────────────┴─────────────┴───────────┴──────────┴────────────┘
```

**Giải thích cơ chế:**
- **P/Invoke (Platform Invocation):** C# gọi thẳng hàm C trong file `.dll`. Overhead thấp so với process/GUI boundary, nhưng không dùng “nano giây” làm KPI end-to-end. C++/Rust cần thêm `extern "C"` để "giả dạng" thành C.
- **C# Direct:** Tool viết bằng C# → thêm Project Reference → gọi trực tiếp class/method. Không có process/serialization boundary; vẫn phải benchmark work thực tế.
- **MCP stdio:** C# spawn process → giao tiếp qua JSON-RPC trên stdin/stdout. (Ghidra, JADX, Burp Suite, Unified Python).
- **MCP HTTP/SSE:** Các phần mềm GUI khổng lồ (IDA) tự mở port nội bộ. App C# kết nối tới như một Client HTTP, không cần quản lý tiến trình.

---

## PHẦN B: PHÂN TÍCH TỪNG TOOL

### ═══════════════════════════════════════
### NHÓM 1: ĐÃ TỐI ƯU (Không cần thay đổi)
### ═══════════════════════════════════════

#### 1.1 LIEF — Phân tích cấu trúc PE/ELF/MachO

| Thuộc tính | Chi tiết |
|---|---|
| **Ngôn ngữ gốc** | C++ (core) + Python bindings |
| **Tích hợp** | ✅ Python library (`import lief`) |
| **Cài đặt** | `pip install lief` |
| **Cập nhật** | `pip install --upgrade lief` |

> Đã tối ưu. LIEF được sử dụng làm công cụ chính trong file `server.py` để bóc tách cấu trúc file (PE, ELF, MachO). Phiên bản mới đã được lập trình để trích xuất IAT (Import Address Table) nhằm rà soát các hàm API đáng ngờ (vd: VirtualAlloc, LoadLibrary) và thu thập Export Tables để phân tích hành vi tiềm ẩn. LIEF cũng có sẵn API đọc chữ ký số PE (`binary.signatures`) — dùng làm backup cho Authenticode.

---

#### 1.2 Hash (MD5 / SHA256)

| Thuộc tính | Chi tiết |
|---|---|
| **Tích hợp** | ✅ Trực tiếp trong C# (.NET) |
| **Thư viện** | `System.Security.Cryptography` |
| **Cài đặt** | Có sẵn trong .NET Runtime (0ms overhead) |

> Đã tối ưu triệt để. C# gọi trực tiếp API hệ thống, không cần Python hay MCP.

---

#### 1.3 Authenticode — Kiểm tra chữ ký số

| Thuộc tính | Chi tiết |
|---|---|
| **Tích hợp** | ✅ P/Invoke Native C-ABI |
| **Thư viện** | `wintrust.dll` (Có sẵn trong C:\Windows\System32) |
| **Cài đặt** | Không cần cài đặt hay build |

> Đã tối ưu triệt để. C# P/Invoke thẳng vào API Windows, loại bỏ hoàn toàn PowerShell. Giảm trễ ~500ms mỗi lần quét.

---

#### 1.4 Entropy Calculator — Tính mức hỗn loạn

| Thuộc tính | Chi tiết |
|---|---|
| **Ngôn ngữ gốc** | Python (`math.log2`) |
| **Tích hợp** | ✅ Hàm nội bộ `_entropy()` trong `server.py` |
| **Entropy per-section** | LIEF đã tính sẵn (`section.entropy`) |

> Chấp nhận được. Nếu cần tăng tốc sau này, thay bằng `numpy` (C extension).

---

### ═══════════════════════════════════════
### NHÓM 2: CẦN REFACTOR (Đang dùng subprocess)
### ═══════════════════════════════════════

#### 2.1 Detect It Easy (DIE) → Chuyển sang P/Invoke Native C-ABI

| Thuộc tính | Hiện tại | Sau refactor |
|---|---|---|
| **Tích hợp** | ❌ subprocess `diec.exe` | ✅ P/Invoke `die_core.dll` (C-ABI) |
| **Kiến trúc** | Phụ thuộc CLI cồng kềnh | Tích hợp trực tiếp vào lõi C# |
| **Tốc độ** | ~200ms/lần (spawn process) | <5ms/lần (Native call) |

**Cách triển khai (Chi tiết trong file scripts/dev/Build-Native-Deps.ps1):**
Mã nguồn C++ (`die_library`) được tự động tải về và build thông qua CMake/Ninja, xuất ra thư viện `die_core.dll` dưới dạng C-ABI. App C# sau đó sử dụng P/Invoke để gọi thư viện này. Toàn bộ quá trình cài đặt, build và copy DLL được tự động hóa hoàn toàn trong script `scripts/dev/Build-Native-Deps.ps1`.

**Tác động:** Mang lại sức mạnh phân tích sâu nhất thế giới của DIE thẳng vào bộ nhớ của C# App mà không bị độ trễ spawn process.

---

#### 2.2 Binwalk v3 — Build DLL từ source Rust

| Thuộc tính | Hiện tại | Sau refactor |
|---|---|---|
| **Tích hợp** | ❌ subprocess `binwalk.exe` | ✅ P/Invoke `binwalk_core.dll` |
| **Source** | Build exe từ crate | Tự động hóa hoàn toàn trong `scripts/dev/Build-Native-Deps.ps1` |
| **Tốc độ** | ~100ms/lần (spawn process) | <1ms/lần (P/Invoke call) |

**Cách triển khai (Chi tiết trong file scripts/dev/Build-Native-Deps.ps1):**
Mã nguồn Rust của Binwalk được tự động clone về. Script `scripts/dev/Build-Native-Deps.ps1` sẽ tự động tiêm (inject) lớp vỏ C-ABI (FFI code) vào mã nguồn gốc và gọi `cargo build --release --crate-type cdylib`. Tệp `binwalk_core.dll` sau đó được copy sang thư mục `Native/` để C# P/Invoke. Mọi mã nguồn Rust và C# liên quan đều đã được đóng gói hoặc sinh tự động, không cần thao tác thủ công.

---

#### 2.3 Authenticode — C# P/Invoke trực tiếp Windows API

| Thuộc tính | Hiện tại | Sau refactor |
|---|---|---|
| **Tích hợp** | ❌ subprocess PowerShell (~500ms) | ✅ P/Invoke `wintrust.dll` (~1ms) |
| **Dependency** | PowerShell runtime | Không (Windows system DLL có sẵn) |

**Tại sao không cần viết thêm C wrapper?**

File `wintrust.dll` nằm trên mọi máy Windows (`C:\Windows\System32\wintrust.dll`) **chính là code C đã được Microsoft compile sẵn**. C# gọi thẳng vào đó qua P/Invoke — không có tầng trung gian nào cả.

**Tác động:** Loại bỏ hoàn toàn PowerShell khỏi pipeline. Giảm ~500ms mỗi lần quét.

---

### ═══════════════════════════════════════
### NHÓM 3: TOOL TRONG KẾ HOẠCH (Chưa triển khai)
### ═══════════════════════════════════════

#### Giai đoạn 5: Kernel / Memory

| Tool | Ngôn ngữ | Chiến lược tích hợp |
|---|---|---|
| **MemProcFS** | C | ✅ Python bindings (`memprocfs` PyPI) → gom vào Unified MCP Server |
| **KsDumper** | C# | ✅ Thêm Project Reference → gọi trực tiếp (cùng .NET runtime) |
| **PE-sieve** | C++ | ✅ Thêm `extern "C"` wrapper → build DLL → P/Invoke từ C# |

#### Giai đoạn 6: Game Engine

| Tool | Ngôn ngữ | Chiến lược tích hợp |
|---|---|---|
| **Il2CppDumper** | **C#** | ✅ Thêm Project Reference (cùng ngôn ngữ, 0ms overhead) |
| **Blutter** (Flutter RE) | Python + C++ | Python wrapper → gom vào Unified MCP Server |

#### Giai đoạn 7: Emulation / Math

| Tool | Ngôn ngữ | Chiến lược tích hợp |
|---|---|---|
| **Qiling** | Python | ✅ `import qiling` → Unified MCP Server |
| **Unicorn Engine** | C core + Python bindings | ✅ `import unicorn` → Unified MCP Server |
| **Capstone** | C core + Python bindings | ✅ `import capstone` → Unified MCP Server |
| **Triton** | C++ core + Python bindings | ✅ `import triton` → Unified MCP Server |
| **Z3 Prover** | C++ core + Python bindings | ✅ `import z3` → Unified MCP Server |

#### Giai đoạn 8: Android Dynamic & API/Network

| Tool | Ngôn ngữ | Chiến lược tích hợp |
|---|---|---|
| **Frida** | C core + Python bindings | ✅ `import frida` → Unified MCP Server |
| **JADX** | **Java** | MCP server stdio (spawn khi mở app) |
| **Ghidra** | **Java** | MCP server stdio (spawn khi mở app) |
| **Burp Suite** | **Java (GUI)** | ✅ Tích hợp qua `MCP HTTP/SSE` (Port 9876, tương tự IDA-MCP) |

#### Giai đoạn 9: Advanced Disassembly (GUI)

| Tool | Ngôn ngữ | Chiến lược tích hợp |
|---|---|---|
| **IDA Pro** | C++ / IDAPython | ✅ Cài Plugin `IDA-MCP`, tích hợp qua `MCP HTTP/SSE` (Port 11338) |

---

## PHẦN C: KIẾN TRÚC MCP SERVER

### Hiện tại (Phân tán — nhiều process)

```
C# App
  ├── spawn → md5_server.py      (Process 1)
  ├── spawn → triage_server.py   (Process 2)
  │             ├── subprocess → diec.exe        (Process 3)
  │             ├── subprocess → binwalk.exe      (Process 4)
  │             └── subprocess → powershell.exe   (Process 5)
  └── spawn → [future_tool].py   (Process N)
```
**Vấn đề:** Mỗi lần gõ `/triage` → spawn tới 5 process. Chậm, tốn RAM.

### Sau refactor (Tập trung — tối thiểu process)

```
C# App (Trưởng phòng)
  ├── [P/Invoke — không spawn process]
  │     ├── wintrust.dll (Authenticode)
  │     └── binwalk_core.dll (Deep scan)
  │
  ├── [Middleware Lọc Dữ Liệu]
  │     └── Presidio Anonymizer (Mask IP, Credentials, URL trước khi gửi lên AI)
  │
  ├── [Não bộ AI (Cloud/Local) via Function Calling]
  │     ├── Đọc mã giả C++/Java đã làm sạch
  │     └── Tự quyết định gọi các Tool dưới đây để làm rõ mã
  │
  ├── [MCP Python — 1 process duy nhất, chạy suốt phiên]
  │     └── unified_server.py
  │           ├── @tool triage_file()              ← lief (Hỗ trợ bung .xapk/.apks)
  │           ├── @tool deep_analyze()             ← capstone + qiling
  │           ├── @tool frida_hook()               ← frida
  │           ├── @tool memory_scan()              ← memprocfs
  │           └── @tool search_web_for_re_context()← Trình tra cứu Web (API) cho AI
  │
  └── [MCP Java — 1 process, chạy khi mở app]
        ├── ghidra-mcp
        └── jadx-mcp
```

**Kết quả:** Từ 5 process → 0 subprocess cho mỗi lần triage. Thời gian dự kiến giảm từ ~1.5s → <100ms. Khả năng tự động hóa Agentic tăng vọt nhờ Tool Calling và Middleware Presidio đảm bảo 100% Privacy khi dùng Cloud AI.

---

## PHẦN D: QUY TRÌNH CÀI ĐẶT & CẬP NHẬT

*(Lưu ý: Toàn bộ mã nguồn cài đặt, vá lỗi, tiêm code FFI và build C++/Rust đều đã được tự động hóa hoàn toàn. Không cần ghi chép mã nguồn thủ công tại đây).*

### Cài đặt lần đầu (`scripts/dev/Build-Native-Deps.ps1`)
Chạy file script `scripts/dev/Build-Native-Deps.ps1` để tự động hóa 100% các bước:
1. **Python Portable:** Tải tự động bản Embeddable 3.11, setup pip và cài `lief`, `mcp[cli]`.
2. **Detect It Easy (C++):** Clone mã nguồn `die_library`, cấu hình CMake với MSVC/Ninja, build ra `die_core.dll` và deploy cùng Qt dependencies.
3. **Binwalk v3 (Rust):** Clone mã nguồn, tự động tiêm (inject) lớp vỏ C-ABI (FFI code) vào `lib.rs`, chỉnh sửa `Cargo.toml` và chạy `cargo build` để xuất ra `binwalk_core.dll`.
4. **Java Tools (Ghidra, JADX) & Burp Suite MCP:** Tải binary `.jar` cho Ghidra/JADX. Riêng **Burp Suite MCP**, tích hợp trực tiếp qua giao thức `HTTP/SSE` (mặc định mở tại port `9876` khi bật tab MCP trong Burp Suite Pro/Community), không cần copy hay phụ thuộc file jar proxy trung gian.
5. **IDA-MCP Plugin:** Tự động clone mã nguồn `Captain-AI-Hub/IDA-MCP` vào `Tools/tmp_ida_mcp`. Người dùng sau đó chỉ cần chạy lệnh `python install.py` để script tự động dò tìm thư mục IDA Pro và chèn plugin vào đúng chỗ.

### Cập nhật (`update_ecosystem.ps1`)
**Quy tắc Cốt lõi: Xóa cũ - Cài mới.**
- Script sẽ tự động gọi `git pull` lấy code mới nhất.
- Thực hiện build lại DLL.
- Bắt buộc xóa (`Remove-Item`) các file DLL cũ trong thư mục `Native/` trước khi copy file DLL mới vào để tránh lỗi xung đột bộ nhớ.

### Cấu trúc thư mục sau refactor

```
Tools/
├── Python_Runtime/               ← Bản Python 3.11 Portable (Không dùng venv)
├── binwalk_src/                  ← Binwalk v3 Rust source (git repo)
├── tmp_die_library/              ← DIE C++ source (git repo, sẽ bị xóa sau khi build)
├── Native/                       ← Chứa các file DLL tự build (`binwalk_core.dll`, `die_core.dll`)
├── MCP_Unified/                  ← Unified Python MCP Server (`server.py`)
├── Java/                         ← Java MCP (`ghidra-mcp.jar`, `jadx-cli.jar`)
├── tmp_ida_mcp/                  ← IDA-MCP source (Để chạy `install.py` cài plugin vào IDA)
├── scripts/dev/Build-Native-Deps.ps1   ← Cài đặt lần đầu (Tự động tải mã nguồn, vá code, compile DLL)
└── update_ecosystem.ps1          ← Cập nhật (Git pull -> Build -> Xóa cũ -> Copy mới)
```

---

## PHẦN E: TÓM TẮT HÀNH ĐỘNG THEO THỨ TỰ ƯU TIÊN

### Ưu tiên cao (Tác động lớn nhất)

| # | Hành động | Giảm thời gian | Ghi chú |
|---|---|---|---|
| 1 | **Authenticode: Viết `AuthenticodeVerifier.cs` gọi P/Invoke `wintrust.dll`** | -500ms | Loại bỏ PowerShell hoàn toàn (✅ ĐÃ XONG) |
| 2 | **Gom md5_server + triage_server → Unified MCP Server** | -500ms (bớt 1 process spawn) | Đơn giản hóa kiến trúc (✅ ĐÃ XONG) |
| 3 | **DIE Core P/Invoke: Bỏ subprocess `diec.exe`** | -200ms | Tích hợp `die_core.dll` thẳng vào C# (✅ ĐÃ XONG) |

### Ưu tiên trung bình (Khi bắt đầu Phase 5-6)

| # | Hành động | Tác động |
|---|---|---|
| 4 | **Binwalk v3: Clone source → build DLL → P/Invoke** | Bỏ subprocess cuối cùng |
| 5 | **Il2CppDumper: Thêm Project Reference C#** | Native integration |
| 6 | **Ghidra/JADX: Setup MCP Java long-running** | JVM chỉ khởi động 1 lần |

### Giữ nguyên (Đã tối ưu)

| Tool | Lý do |
|---|---|
| LIEF (`import lief`) | Python library, lõi C++, đã tối ưu |
| Hash (MD5, SHA1, SHA256) | C# `System.Security.Cryptography`, lõi Windows CNG |
| Entropy | Hàm Python nội bộ, đủ nhanh |
