# 🔬 Phân Tích Toàn Diện: Chiến Lược Tích Hợp Tool Hệ Sinh Thái RE

> Cập nhật: 2026-07-08
> Trạng thái: **ĐÃ DUYỆT** — Đây là tài liệu tham chiếu chính thức cho kiến trúc tích hợp tool.

---

## PHẦN A: NGUYÊN TẮC CỐT LÕI

### Triết lý thiết kế

1. **Library-first:** Ưu tiên tích hợp dạng thư viện (import/P/Invoke). Chỉ dùng subprocess cho tool Java (bắt buộc).
2. **Source-first:** Quản lý source code thay vì tải binary. Build trong pipeline cài đặt.
3. **Một process Python duy nhất:** Gom tất cả tool Python vào 1 MCP server, chạy suốt phiên.
4. **Giao tiếp đa ngôn ngữ qua C ABI:** C++, Rust đều expose `extern "C"` → C# gọi qua P/Invoke (~nano giây).

### Ma trận giao tiếp đa ngôn ngữ

```
┌──────────────────────────────────────────────────────────────┐
│                C# / WinUI 3 (.NET 10 Runtime)                │
│                      App chính                                │
├────────────┬────────────┬─────────────┬──────────────────────┤
│  P/Invoke  │  P/Invoke  │  C# Direct  │  MCP stdio           │
│  (C ABI)   │  (C ABI)   │  (cùng CLR) │  (JSON-RPC)          │
├────────────┼────────────┼─────────────┼──────────────────────┤
│ Windows    │ C/Rust DLL │ C# Library  │ Python    │ Java     │
│ System DLL │ (Native)   │ (Project    │ (1 proc)  │ (1 proc) │
│            │            │  Reference) │           │          │
├────────────┼────────────┼─────────────┼───────────┼──────────┤
│ wintrust   │ binwalk    │ Il2Cpp      │ lief      │ Ghidra   │
│ .dll       │ _core.dll  │ Dumper      │ hashlib   │ JADX     │
│            │ die_core   │ KsDumper    │ capstone  │          │
│            │ .dll       │             │ frida     │          │
│            │            │             │ qiling    │          │
│            │            │             │ memprocfs │          │
└────────────┴────────────┴─────────────┴───────────┴──────────┘
```

**Giải thích cơ chế:**
- **P/Invoke (Platform Invocation):** C# gọi thẳng hàm C trong file `.dll`. Chi phí ~nano giây. C++/Rust cần thêm `extern "C"` để "giả dạng" thành C.
- **C# Direct:** Tool viết bằng C# → thêm Project Reference → gọi trực tiếp class/method. 0ms overhead.
- **MCP stdio:** C# spawn process → giao tiếp qua JSON-RPC trên stdin/stdout. Chi phí spawn 1 lần (~500ms), sau đó mỗi lệnh <10ms.

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

> Đã tối ưu. LIEF cũng có sẵn API đọc chữ ký số PE (`binary.signatures`) — dùng làm backup cho Authenticode.

---

#### 1.2 hashlib — Băm MD5 / SHA256

| Thuộc tính | Chi tiết |
|---|---|
| **Ngôn ngữ gốc** | C (OpenSSL core) + Python stdlib |
| **Tích hợp** | ✅ Python stdlib |
| **Cài đặt** | Có sẵn trong Python |

> Đã tối ưu. Lõi C native, tốc độ ngang tool hệ thống.

---

#### 1.3 Entropy Calculator — Tính mức hỗn loạn

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

**Lịch sử Quyết định:**
Ban đầu, kế hoạch là thay thế DIE bằng `yara-python` để nhận diện packer. Tuy nhiên, YARA thiếu vắng các rule cập nhật về Compiler (MSVC, Borland) và khả năng nhận diện siêu sâu của DIE. Do đó, quyết định cuối cùng là **loại bỏ hoàn toàn YARA** và giữ lại DIE.
Nhưng thay vì gọi `diec.exe` qua subprocess, chúng ta đã tận dụng thư viện `die_core.dll` được xuất ra dưới dạng C-ABI.

**Cách triển khai (DieWrapper.cs):**
```csharp
[DllImport("die_core.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
public static extern int DIE_LoadDatabaseA(string databasePath);

[DllImport("die_core.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
public static extern IntPtr DIE_ScanFileA(string fileName, uint flags, string databasePath);

[DllImport("die_core.dll", CallingConvention = CallingConvention.Cdecl)]
public static extern void DIE_FreeMemoryA(IntPtr ptr);
```

**Tác động:** Mang lại sức mạnh phân tích sâu nhất thế giới của DIE thẳng vào bộ nhớ của C# App mà không bị độ trễ spawn process, đồng thời loại bỏ sự phức tạp của việc duy trì tập luật YARA.

---

#### 2.2 Binwalk v3 — Build DLL từ source Rust

| Thuộc tính | Hiện tại | Sau refactor |
|---|---|---|
| **Tích hợp** | ❌ subprocess `binwalk.exe` | ✅ P/Invoke `binwalk_core.dll` |
| **Source** | Build exe từ crate | Clone source → thêm FFI → build DLL |
| **Tốc độ** | ~100ms/lần (spawn process) | <1ms/lần (P/Invoke call) |

**Cấu trúc thư mục:**
```
Tools/
├── binwalk_src/                  ← Git clone từ ReFirmLabs/binwalk
│   ├── src/
│   │   ├── lib.rs                ← Logic gốc Binwalk (không sửa)
│   │   └── ffi.rs                ← Lớp extern "C" wrapper (ta thêm vào)
│   └── Cargo.toml                ← Thêm: crate-type = ["cdylib"]
└── Native/
    └── binwalk_core.dll          ← Output sau cargo build --release
```

**Lớp wrapper FFI mẫu (`ffi.rs`):**
```rust
use std::ffi::{CStr, CString};
use std::os::raw::c_char;

#[no_mangle]
pub extern "C" fn binwalk_scan(path_ptr: *const c_char) -> *mut c_char {
    let path = unsafe { CStr::from_ptr(path_ptr) }.to_str().unwrap();
    let result = binwalk::scan(path);  // Gọi logic gốc
    let json = serde_json::to_string(&result).unwrap();
    CString::new(json).unwrap().into_raw()
}

#[no_mangle]
pub extern "C" fn binwalk_free(ptr: *mut c_char) {
    unsafe { drop(CString::from_raw(ptr)); }
}
```

**C# gọi:**
```csharp
[DllImport("binwalk_core.dll", CallingConvention = CallingConvention.Cdecl)]
static extern IntPtr binwalk_scan([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

[DllImport("binwalk_core.dll", CallingConvention = CallingConvention.Cdecl)]
static extern void binwalk_free(IntPtr ptr);
```

**Quy trình build:**
- **Lần cài đặt đầu tiên:** Clone source → `cargo build --release` → copy `.dll` vào `Tools/Native/`
- **Khi cập nhật:** `git pull` source mới → `cargo build --release` → thay thế `.dll` cũ

---

#### 2.3 Authenticode — C# P/Invoke trực tiếp Windows API

| Thuộc tính | Hiện tại | Sau refactor |
|---|---|---|
| **Tích hợp** | ❌ subprocess PowerShell (~500ms) | ✅ P/Invoke `wintrust.dll` (~1ms) |
| **Dependency** | PowerShell runtime | Không (Windows system DLL có sẵn) |

**Tại sao không cần viết thêm C wrapper?**

File `wintrust.dll` nằm trên mọi máy Windows (`C:\Windows\System32\wintrust.dll`) **chính là code C đã được Microsoft compile sẵn**. C# gọi thẳng vào đó qua P/Invoke — không có tầng trung gian nào cả.

**Code C# gọi Windows API trực tiếp:**
```csharp
// Core/Security/AuthenticodeVerifier.cs
[DllImport("wintrust.dll", SetLastError = true)]
static extern int WinVerifyTrust(
    IntPtr hwnd,
    ref Guid pgActionID,
    ref WINTRUST_DATA pWVTData);

public static AuthenticodeResult Verify(string filePath)
{
    // Setup WINTRUST_DATA structure
    // Gọi WinVerifyTrust
    // Parse kết quả → trả về Status + Subject + Issuer
}
```

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

#### Giai đoạn 8: Android Dynamic

| Tool | Ngôn ngữ | Chiến lược tích hợp |
|---|---|---|
| **Frida** | C core + Python bindings | ✅ `import frida` → Unified MCP Server |
| **JADX** | **Java** | MCP server long-running (spawn khi mở app) |
| **Ghidra** | **Java** | MCP server long-running (spawn khi mở app, dùng ghidra-mcp) |

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
C# App
  ├── [P/Invoke — không spawn process]
  │     ├── wintrust.dll (Authenticode)
  │     └── binwalk_core.dll (Deep scan)
  │
  ├── [MCP Python — 1 process duy nhất, chạy suốt phiên]
  │     └── unified_server.py
  │           ├── @tool triage_file()    ← lief + yara + hashlib
  │           ├── @tool deep_analyze()   ← capstone + qiling
  │           ├── @tool frida_hook()     ← frida
  │           └── @tool memory_scan()    ← memprocfs
  │
  └── [MCP Java — 1 process, chạy khi mở app]
        ├── ghidra-mcp
        └── jadx-mcp
```

**Kết quả:** Từ 5 process → 0 subprocess cho mỗi lần triage. Thời gian dự kiến giảm từ ~1.5s → <100ms.

---

## PHẦN D: QUY TRÌNH CÀI ĐẶT & CẬP NHẬT

### Cài đặt lần đầu (`setup_tools.ps1`)

```powershell
# 1. Python Virtual Environment
python -m venv Tools/venv
Tools/venv/Scripts/pip install lief yara-python mcp[cli]

# 2. YARA Rules
git clone https://github.com/Neo23x0/signature-base Tools/Rules/signature-base
git clone https://github.com/Yara-Rules/rules Tools/Rules/yara-rules

# 3. Binwalk v3 (build DLL từ source)
git clone https://github.com/ReFirmLabs/binwalk Tools/binwalk_src
# Thêm ffi.rs + sửa Cargo.toml (tự động bằng script)
cargo build --release --manifest-path Tools/binwalk_src/Cargo.toml
Copy-Item Tools/binwalk_src/target/release/binwalk_core.dll Tools/Native/

# 4. Java tools (tải binary — quá lớn để build from source)
# Download ghidra-mcp.jar, jadx-cli.jar từ GitHub Releases
```

### Cập nhật (`update_tools.ps1`)

**Quy tắc Cốt lõi: Xóa cũ - Cài mới.** Trước khi cập nhật bất kỳ tool nào, script phải dọn dẹp sạch sẽ thư mục/file cũ để tránh lỗi xung đột hoặc tốn dung lượng rác.

```powershell
# 1. Python packages
Tools/venv/Scripts/pip install --upgrade lief yara-python

# 2. YARA Rules
git -C Tools/Rules/signature-base pull
git -C Tools/Rules/yara-rules pull

# 3. Binwalk v3 (rebuild DLL, thay thế cái cũ)
git -C Tools/binwalk_src pull
cargo build --release --manifest-path Tools/binwalk_src/Cargo.toml
# XÓA FILE CŨ TRƯỚC KHI COPY MỚI
Remove-Item Tools/Native/binwalk_core.dll -ErrorAction SilentlyContinue
Copy-Item Tools/binwalk_src/target/release/binwalk_core.dll Tools/Native/

# 4. Java tools (check GitHub API → tải nếu có version mới)
# So sánh version hiện tại vs latest release
# Nếu có bản mới: Xóa file .jar cũ -> Tải file .jar mới
```

### Cấu trúc thư mục sau refactor

```
Tools/
├── venv/                         ← Python virtual environment
├── Rules/                        ← YARA rules (git repos)
│   ├── signature-base/           ← Neo23x0
│   └── yara-rules/               ← Community
├── binwalk_src/                  ← Binwalk v3 Rust source (git repo)
│   ├── src/
│   │   ├── lib.rs
│   │   └── ffi.rs                ← Wrapper extern "C"
│   └── Cargo.toml
├── Native/                       ← Compiled DLLs
│   └── binwalk_core.dll
├── MCP_Unified/                  ← Unified Python MCP Server
│   └── server.py                 ← Tất cả tool Python gom vào đây
├── Java/                         ← Java MCP servers
│   ├── ghidra-mcp.jar
│   └── jadx-cli.jar
├── setup_tools.ps1               ← Cài đặt lần đầu
├── update_tools.ps1              ← Cập nhật
└── requirements.txt              ← Pin version Python packages
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
| hashlib | Python stdlib, lõi OpenSSL C |
| Entropy | Hàm Python nội bộ, đủ nhanh |
