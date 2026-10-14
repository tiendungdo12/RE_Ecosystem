> [!CAUTION]
> **HISTORICAL / RESEARCH — terminology clarification, 13 October 2026.** Earlier Harness/platform ownership and implementation/metric claims below are preserved research, not current contracts. [Canonical architecture](docs/ARCHITECTURE.md) assigns state/policy/evidence/persistence to RE Platform Core and reserves AI Harness for optional H7 RE Agent Runtime. The [following architecture conversation](docs/reference/ARCHITECTURE_TERMINOLOGY_CLARIFICATION.md) clarifies the source vision. Use [CURRENT](docs/CURRENT_STATE.md) for actual source status.

# BÁO CÁO KIẾN TRÚC HỆ SINH THÁI RE 2.0 (WINDOWS & ANDROID AUTOMATION PIPELINE)

> [!CAUTION]
> **⚠️ TÀI LIỆU LỊCH SỬ — KHÔNG CÒN LÀ NGUỒN THAM CHIẾU CHÍNH**
>
> Tài liệu này là SRS gốc của dự án, được viết trước khi kiến trúc được chuẩn hóa lại. Nhiều quyết định trong đây **đã bị thay thế**, bao gồm:
> - ❌ Unified Python MCP Server → ✅ **3 Domain MCP Servers** (MCP-RE-Deep, MCP-Network, MCP-Dynamic) tự viết
> - ❌ Triage chạy qua MCP → ✅ **Triage = Direct Calls** (P/Invoke, 0 AI tokens, <2.5s)
> - ❌ Presidio Data Masking thô → ✅ **Pseudonymization L1 + Context Hints** (giữ ngữ nghĩa)
> - ❌ 28+ tools gom chung → ✅ **Phân loại 4 nhóm:** Giữ 7 (A), Đổi tích hợp 9 (B), Loại 7 (C), Hoãn 5 (D)
> - ❌ AI làm triage → ✅ **Phase 1 deterministic** (checksec + IOC + YARA + MASVS auto + CWE/ATT&CK mapping)
>
> **Tài liệu chuẩn hiện tại:**
> - [`README.md`](./README.md) — Định vị sản phẩm và entry point
> - [`STRATEGY.md`](./STRATEGY.md) — Roadmap H0-H9 và acceptance gates
> - [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) and [`docs/CURRENT_STATE.md`](./docs/CURRENT_STATE.md) — Boundaries and source-grounded implementation status
>
> Nội dung bên dưới được giữ lại làm tham chiếu lịch sử (historical reference). **Không cập nhật body này theo source mới**; mọi quyết định mới đi vào canonical docs trên.

---

Tài liệu này tổng hợp toàn diện cấu trúc, quy trình vận hành và chiến lược triển khai giao thức Model Context Protocol (MCP) cho Hệ sinh thái Tự động hóa Dịch ngược (Reverse Engineering Automation Ecosystem 2.0). Hệ thống được tối ưu hóa cho môi trường Native Windows, loại bỏ các thành phần cồng kềnh và tích hợp sâu sắc trí tuệ nhân tạo (AI).

## 1. MA TRẬN CÔNG CỤ HỆ SINH THÁI (ECOSYSTEM TOOLCHAIN)

Các thành phần công cụ được phân định rõ ràng theo từng tầng chức năng, tối ưu hóa giữa việc dùng chung các lõi tính toán tĩnh và phân nhánh đối với các công cụ can thiệp động đặc thù của hệ điều hành:

| Tầng Phân Tích (Layer) | Nền tảng Windows (x64 PC) | Nền tảng Android (ARM64 / Dalvik) |
| :--- | :--- | :--- |
| **1. UI & Giao Diện** | **Dùng chung:** Custom WinUI 3 App (AI Command Center & DevToys), Ghidra GUI, ImHex, PE-bear | |
| **2. Não Bộ AI & Middleware**| **Dùng chung:** **Cloud AI** (Gemini 1.5 Pro / GPT-4o / Claude) cho độ chính xác cao giai đoạn đầu, tiến tới **Local AI** (Qwen2.5 / Nous Hermes) giai đoạn sau. Bắt buộc tích hợp **Presidio Middleware (Data Masking)** trước khi gửi dữ liệu lên Cloud. Hỗ trợ **Function Calling / Tool Calling**. Tích hợp SearXNG / Web Search. | |
| **3. Nhận Diện & Tiền Xử Lý**| DIE CLI, LIEF | Cụm Pre-Triage (Giải nén xapk/apks), DIE CLI, LIEF, Quark Engine |
| **4. Phân giải Engine Game** | **Dùng chung:** Il2CppDumper (Unity), UE4Dumper (Unreal), Blutter/Custom Script (Flutter app.so) | |
| **5. Trích Xuất Bộ Nhớ (Dumping)**| MemProcFS, KsDumper (Custom CLI), PE-sieve | Custom Frida-DexDumper, Medusa |
| **6. Giải Ảo Hóa & Dọn Mã Rối**| Remill + Rellic, angr | Remill + Rellic, angr, JADX De-obfuscator |
| **7. Dịch Ngược (Decompile)**| Ghidra Headless, **IDA Pro** (MCP Plugin) | Ghidra Headless, JADX CLI, **IDA Pro** |
| **8. Giả Lập & Giải Toán Động**| **Dùng chung:** Qiling Framework, Capstone, Unicorn, Triton, Z3 Solver | |
| **9. Phân Tích Mạng (Network)**| **Burp Suite** (MCP stdio), MITMProxy / ZAP | **Burp Suite**, MITMProxy / ZAP, eCapture |
| **10. Gỡ Lỗi Thủ Công (Debug)**| x64dbg (kèm ScyllaHide), **IDA Pro** | GDB, Frida CLI, **IDA Pro** |

### Nhiệm vụ chi tiết của các công cụ cốt lõi:

- **Custom WinUI 3 App:** Đóng vai trò làm Client Native gọn nhẹ (thay thế Electron). Xử lý UI chat mượt mà. Đặc biệt đóng vai trò làm "Trưởng phòng" kiểm soát các luồng Unpacking tĩnh (Deterministic) và giải nén `.xapk`.
- **Cloud AI / Local AI (Não bộ):** Sử dụng Cloud AI (Gemini/Claude) qua cơ chế Function Calling/Tool Calling để điều phối tổng quan (Agent) và phân tích luồng dịch ngược sâu. Local AI (Qwen/Hermes) là backup offline.
- **Presidio Middleware (Người gác đền):** Nằm giữa C# App và Cloud AI. Làm nhiệm vụ Masking (che giấu) toàn bộ IP, URL, thông tin nhạy cảm từ mã nguồn trước khi đẩy lên Cloud.
- **eCapture (Android độc quyền):** Sử dụng công nghệ eBPF chạy trực tiếp trong Linux Kernel để bắt plaintext của các gói tin mạng TLS/HTTPS mà không cần cài đặt chứng chỉ CA hoặc bypass SSL Pinning thủ công.
- **MemProcFS (Windows độc quyền):** Ánh xạ bộ nhớ vật lý của Windows thành hệ thống tệp ảo qua driver hoặc phần cứng DMA, giúp bypass các cơ chế Anti-cheat Ring 0 một cách tàng hình.
- **LIEF (Dùng chung):** Thư viện đa nền tảng dùng để vá lỗi cấu trúc Header (PE cho Windows, ELF/.so cho Android) ngay sau khi bộ nhớ bị dump từ RAM ra đĩa cứng.
- **IDA Pro & Burp Suite (Heavy GUI):** Các cỗ máy phân tích khổng lồ có giao diện riêng biệt. C# kết nối và điều khiển chúng từ xa qua MCP (stdio/HTTP) để lấy dữ liệu chuyên sâu (Decompile, Network Intercept) làm đầu vào cho AI mà không cần can thiệp quy trình thủ công của người dùng.

## 2. QUY TRÌNH THỰC THI PIPELINE TỰ ĐỘNG (AUTOMATION WORKFLOW)

Luồng xử lý tuần tự đảm bảo dữ liệu đầu ra của tầng trước là dữ liệu đầu vào sạch của tầng sau. Báo cáo cuối cùng tuân thủ nghiêm ngặt nguyên tắc ghi tách riêng biệt các thành phần tĩnh, động, bộ nhớ và mạng.

| Loại tệp thực tế | Dấu hiệu Nhận diện (Magic Bytes / Metadata) | Luồng Xử lý (Định tuyến) |
| :--- | :--- | :--- |
| **Windows Native** (.exe, .dll, .sys) | Chữ ký MZ (PE Header). Cả Native C++, .NET và tệp bọc (PyInstaller). | Chạy thẳng vào luồng **Windows x64** (MemProcFS, Ghidra x64). |
| **Android Native** (.so chuẩn) | Chữ ký ELF + Header ARM/AArch64. | Chạy thẳng vào luồng **Android ARM64** (Frida, Ghidra ARM). |
| **Android Java/App** (.apk, .dex, .xapk, .apks) | Chữ ký PK (Zip/APK) hoặc `dex\n`. Riêng `.xapk/.apks` qua Pre-Triage trích xuất `base.apk`. | Đẩy qua **JADX CLI** để xuất mã nguồn Java. |
| **Dart AOT Snapshot** (app.so Win/Android) | Header chứa chuỗi định danh Dart SDK phiên bản (VD: 3.3.0 (stable)). | Đẩy qua cụm **Engine Parsers** (Blutter port) để móc nối Object Pool. |
| **Unity IL2CPP** (global-metadata.dat) | File `global-metadata.dat` kèm theo `GameAssembly.dll` hoặc `libil2cpp.so`. | Đẩy qua cụm **Engine Parsers** (Il2CppDumper) xuất Header. |
| **Cocos Bytecode** (.jsc, .jsb) | Chữ ký XXTEA hoặc bytecode đặc thù V8/SpiderMonkey. | Đẩy qua luồng **Dumping Động** (Frida/KsDumper) để lấy plaintext. |

### Các Giai đoạn Xử lý Chi tiết:

| Giai đoạn Xử lý | Luồng Windows (PE / .exe / .dll) | Luồng Android (APK / .dex / .so) |
| :--- | :--- | :--- |
| **Giai đoạn 1: Pre-Triage & Triage** | DIE CLI nhận diện packer. LIEF phân tích thô PE. Nhận diện Engine (Unity/Flutter). Cắt luồng nếu có `app.so`. | Xử lý nén `.xapk/.apks` để lấy `base.apk`. Quark/DIE kiểm tra packer. Nhận diện Engine. Cắt luồng nếu có `app.so`. |
| **Giai đoạn 2: Unpacking & Dumping** | Chạy qua MemProcFS ngầm. KsDumper/PE-sieve cào RAM để dump mã sạch. | Chạy qua thiết bị/giả lập. Frida-DexDumper móc file DEX xịn. LIEF/SoFixer vá Header cho `.so`. |
| **Giai đoạn 3: De-obfuscation** | Remill + Rellic lên LLVM IR dọn rác. angr bẻ nhánh giả (Opaque Predicates). | JADX tự động tái cấu trúc luồng. Remill xử lý mã ARM64 cho tệp `.so`. |
| **Giai đoạn 4: Decompilation** | Ghidra Headless dịch ngược mã máy ra C++ giả và xuất JSON. | JADX CLI dịch bytecode ra Java sạch. Ghidra dịch `.so` ra C++. Xuất JSON. |
| **Giai đoạn 5: Data Masking** | Presidio lọc thông tin nhạy cảm khỏi C++ JSON trước khi gửi cho AI. | Presidio che giấu tên package/API key/IP nhạy cảm khỏi Java/C++ JSON. |
| **Giai đoạn 6: AI Agentic & Math** | Cloud/Local AI đọc mã, dùng Function Calling gọi Triton/Z3 giải mã, hoặc gọi `search_web` tìm thông tin lỗ hổng. | AI phân tích JNI logic. Dùng Tool Calling để bẻ gãy mã hóa native hoặc tra cứu anti-debug. |
| **Giai đoạn 7: Reporting** | AI tổng hợp dữ liệu (đã unmask), xuất báo cáo Markdown bảo mật tách biệt các tab: Tĩnh, Động, Bộ nhớ, Mạng. | |

### Cỗ Máy Điều Phối: Bước Triage (Người gác cổng)

Mọi tệp đầu vào đều **BẮT BUỘC** phải qua cụm `MCP_Static_Analysis` (DIE CLI + LIEF). JSON trả về từ cụm này chính là "bản đồ chỉ đường" cho AI.

#### Kịch bản 1: File Windows DLL sạch (Luồng Ngắn)
- **Tệp đưa vào:** `logic_core.dll`
- **Kết quả Triage:** `packer_detected: "None"`, `file_type: "PE32"`.
- **Quyết định (Luồng chạy):**
  1. LIEF phân tích ->
  2. C# App đẩy qua Ghidra MCP (bỏ qua dump RAM/giải ảo hóa) ->
  3. Qua Presidio Masking ->
  4. AI phân tích bằng Tool Calling -> Xuất Báo cáo.

#### Kịch bản 2: Game Cross-platform (Đặc thù Engine)
*(Lưu ý: Đây là một luồng lớn rẽ nhánh ngay từ Bước 1, không đi theo luồng phân tích PE/ELF thông thường).*

**Trường hợp 2A: Unity IL2CPP**
- **Tệp đưa vào:** `GameAssembly.dll` và `global-metadata.dat`.
- **Kết quả Triage:** Chữ ký `IL2CPP`.
- **Quyết định:** Cắt luồng, gọi thẳng cụm `MCP_Engine_Parsers` (Il2CppDumper) -> Bơm script kết quả vào Ghidra MCP -> Qua Masking -> AI đọc và xuất báo cáo.

**Trường hợp 2B: Flutter/Dart AOT**
- **Tệp đưa vào:** `app.so`
- **Kết quả Triage:** Chữ ký Dart SDK.
- **Quyết định:** Chuyển qua Blutter Parser để khôi phục Object Pool thay vì phân tích ELF tĩnh thông thường.

#### Kịch bản 3: Tệp mã độc bị Pack nặng (Luồng Dài - "Trùm cuối")
- **Tệp đưa vào:** `cheat_hack.exe`
- **Kết quả Triage:** `packer_detected: "VMProtect 3.x"`, section `.vmp0` hiện diện, Entropy cực cao.
- **Quyết định của AI (Luồng chạy):**
  1. Triage ->
  2. AI nhận thấy Ghidra sẽ bị "mù" nếu đọc ngay, nó gọi **Cụm `MCP_Windows_Memory`** ->
  3. MemProcFS + KsDumper tóm lấy file sau khi nó tự bung trong RAM ->
  4. Đẩy file vừa dump qua cụm Giải ảo hóa (Remill / angr) để chặt các nhánh Opaque Predicate ->
  5. Đưa tệp sạch cuối cùng vào Ghidra MCP ->
  6. Z3 Solver giải mã các khóa thuật toán -> Xuất Báo cáo.
- **Đánh giá:** Đây mới là lúc hệ thống "bật full nội tại" để đi hết toàn bộ pipeline.

#### Kịch bản 4: App Android bị giấu Dex (Luồng Mobile)
- **Tệp đưa vào:** `game.apk` hoặc `game.xapk`
- **Kết quả Triage (Quark Engine):** Giải nén xapk lấy `base.apk`. Nhận diện `packer: "SecNeo"`, file `classes.dex` gốc bị siêu nhỏ (stub).
- **Quyết định của AI (Luồng chạy):**
  1. Nhận diện APK bị pack ->
  2. C# (Trưởng phòng) tự động hủy luồng JADX tĩnh (tránh hỏng file) ->
  3. Yêu cầu bật Emulator / cắm điện thoại, gọi **Cụm `MCP_Android_Dynamic`** ->
  4. Bơm script Frida-DexDumper vào RAM để moi file DEX xịn ra ->
  5. Lấy file DEX xịn ném lại vào JADX MCP -> Chuyển kết quả cho AI phân tích sâu.

#### Nguyên lý "Vòng lặp Phản hồi" Kết Hợp (Hybrid Agentic Loop)
Kiến trúc sử dụng mô hình **Lai (Hybrid): Quy trình Cơ bắp do con người set cứng (Deterministic), Phân tích Não bộ do AI quyết định (Agentic)** thông qua Function Calling/Tool Calling:
- **Cơ bắp (Hardcoded):** Các bước nhận diện packer, bung RAM bằng KsDumper, LIEF vá header luôn do C# tự chạy theo kịch bản tĩnh. Đảm bảo độ chính xác tuyệt đối, tránh AI bị ảo giác làm hỏng file/OS.
- **Não bộ (Agentic):** Khi bản mã giả đã sẵn sàng trên bàn làm việc, Cloud AI / Local AI được cấp quyền tự do qua Tool Calling:
  - Nếu gặp một mã hóa XOR nội bộ, AI tự gọi Tool `z3_solver(key)` để giải.
  - Nếu AI gặp một API lạ (vd: một kỹ thuật anti-debug mới), nó tự gọi Tool `search_web("bypass IsDebuggerPresent 2026")`, đọc kết quả, rồi tiếp tục phân tích.
  - Sau khi khám phá xong, AI tổng hợp, định dạng qua Presidio (Data Masking) và trả kết quả sạch đẹp lên UI.

## 3. CHIẾN LƯỢC TÍCH HỢP CÔNG CỤ VÀ TRIỂN KHAI ĐA NGÔN NGỮ

Để tối ưu hóa hiệu suất (giảm độ trễ) và dễ dàng quản lý mã nguồn, hệ thống từ bỏ mô hình "mỗi tool một MCP subprocess" để chuyển sang kiến trúc **Tích hợp lai (Hybrid Integration)** với 4 triết lý cốt lõi:
1. **Library-first:** Ưu tiên dùng thư viện (P/Invoke, `import`) thay vì gọi file `.exe` (subprocess).
2. **Source-first:** Quản lý source code, tự build thay vì tải binary có sẵn (ngoại trừ Java).
3. **Một Process Python duy nhất:** Gom tất cả tool Python vào 1 Unified MCP Server.
4. **Xóa cũ - Cài mới:** Khi cập nhật công cụ, luôn tự động xóa sạch phiên bản cũ trước khi build/cài bản mới để tránh rác.

### Ma trận giao tiếp đa ngôn ngữ (Zero-overhead tới Native)

- **[C# Direct] - Gọi trực tiếp (0ms):** 
  - **C# Native:** Tính mã băm (Hash MD5/SHA) dùng thư viện hệ thống `System.Security.Cryptography`.
  - **C# Projects:** Các tool viết bằng C# (Il2CppDumper, KsDumper) được tích hợp thẳng làm thư viện (Project Reference).
- **[C# P/Invoke] - Gọi Native C/Rust (~1ms):** 
  - **Authenticode:** Gọi thẳng Windows API `wintrust.dll` tích hợp sẵn trong HĐH.
  - **DIE & Binwalk v3:** Tự động clone mã nguồn C++ (DIE) và Rust (Binwalk), build thành `die_core.dll` và `binwalk_core.dll` rồi gọi qua P/Invoke.
- **[Unified Python MCP] - Chạy suốt phiên (<10ms/lệnh):** Gom toàn bộ thư viện (LIEF, capstone, frida, qiling...) vào **1 process Python duy nhất** (stdio). Process này khởi động 1 lần và phục vụ mọi lệnh phân tích.
- **[Java MCP] - Long-running subprocess:** Các tool nặng như Ghidra, JADX và **Burp Suite** được khởi động kèm theo App (1 lần duy nhất) qua stdio và giữ sống suốt phiên.
- **[HTTP/SSE MCP] - Giao tiếp mạng nội bộ:** Các phần mềm GUI khổng lồ như **IDA Pro** (Python Plugin port 11338) tự mở server nội bộ, App C# kết nối tới như một client.

### Chi tiết phân bổ kiến trúc thư mục quản lý:

```
Tools/
├── binwalk_src/                  # Mã nguồn Rust Binwalk v3 để tự build
├── Native/                       # Chứa các file DLL tự build (binwalk_core.dll)
├── MCP_Unified/                  # 1 file server.py duy nhất chứa toàn bộ logic Python
├── Python_Runtime/               # Bản Python 3.11 Embeddable (Portable) tự động tải về, thay thế Venv
├── Java/                         # Chứa ghidra-mcp.jar, jadx-cli.jar
├── tmp_ida_mcp/                  # Chứa source IDA-MCP để user chạy install.py nạp vào IDA Pro
├── scripts/dev/Build-Native-Deps.ps1   # Script cài đặt (Tải mã nguồn, vá code FFI, build DLL, tải JAR)
└── update_ecosystem.ps1          # Script cập nhật (Xóa bản cũ -> Pull source -> Rebuild)
```

## 4. THIẾT KẾ CƠ SỞ DỮ LIỆU RAG & QUY TRÌNH KIỂM THỬ (QA/QC)

### Chiến lược phân tách dữ liệu RAG (Tránh hiện tượng ảo giác chéo):
Sử dụng **Metadata Filtering** trong ChromaDB để dán nhãn cố định:
- Tài liệu Windows: `{"platform": "windows"}`
- Tài liệu Android: `{"platform": "android"}`
- Toán học/Mật mã: `{"platform": "general"}`

### Quy trình đánh giá chất lượng (Testing Framework):
1. **Unit Test (Tầng Công cụ Lõi):** Thiết lập kho "Crackme" chuẩn để chạy thử độc lập lệnh CLI thô (DIE, LIEF, JADX) đảm bảo định dạng JSON.
2. **LLM Evaluation (Tút tát Prompt):** Dùng Promptfoo chạy ~100 kịch bản. Nếu AI vi phạm (gộp tĩnh/động), đánh dấu FAILED.
3. **End-to-End Test (Tích hợp Hệ thống):** Kéo thả tệp mẫu vào UI, theo dõi độ trễ, tự động bật tắt driver và text streaming.

## 5. KIẾN THỨC NỀN TẢNG CẦN BỔ SUNG (LEARNING ROADMAP)

- **Giao thức MCP:** Cấu trúc JSON-RPC, luồng stdio.
- **Windows Native UI:** WinUI 3, MVVM, `async/await`, `IAsyncEnumerable`.
- **Linux/Android Internals:** Hệ thống file ảo, JNI, eBPF (WSL2).

---

# LỘ TRÌNH TRIỂN KHAI CHI TIẾT (PHASE ROADMAP)

## GIAI ĐOẠN 1: MẶT TIỀN VÀ BỘ NÃO (WinUI 3 & LLM API)
*App WinUI 3 trở thành Agent Framework riêng biệt, không lệ thuộc vào code cứng.*
- **Block 1.1:** Dựng UI Chat (WinUI 3 XAML) ✅ **ĐÃ XONG**
- **Block 1.2:** Viết API gọi Cloud AI (Gemini/Claude) & Local Ollama (IAsyncEnumerable Streaming, Function Calling) ✅ **ĐÃ XONG**
- **Block 1.3:** Dựng "Khung xương Thư ký" (The Agent Harness) & Config-First ✅ **ĐÃ XONG**
  - Dùng `AgentConfigLoader.cs` quét `Configs/` đọc `IDENTITY.md` và `ROUTING_RULES.yaml` lúc khởi động. Tích hợp **Presidio Middleware** để lọc dữ liệu nhạy cảm trước khi đàm thoại với Cloud AI.
- **Block 1.4:** Khung Cài Đặt Ứng Dụng (Global App Settings) ✅ **ĐÃ XONG**
  - Xây dựng màn hình Settings (Sáng/Tối/Tự động, Accent Color, Custom Fonts, Switch giữa Cloud AI/Local AI).
  - Tích hợp logic tự động cập nhật UI tức thời (`ColorPaletteResources`). Đã loại bỏ hoàn toàn cơ chế chọn Python Environment (chỉ dùng duy nhất Portable Python để đảm bảo Zero-Configuration).

## GIAI ĐOẠN 2: LÀM CHỦ GIAO THỨC MCP & TOOL CALLING
*Xây dựng định tuyến lai (Hybrid Routing) qua Branch Table.*
- **Block 2.1:** Hiểu chuẩn JSON-RPC của MCP ✅ **ĐÃ XONG**
- **Block 2.2:** Code tool MCP mẫu bằng Python ✅ **ĐÃ XONG**
- **Block 2.3:** Cho C# giao tiếp với MCP bằng Process (stdio) ✅ **ĐÃ XONG**
- **Block 2.4:** Xây dựng Branch Table Delegation (Điều phối rẽ nhánh) ✅ **ĐÃ XONG**
  - Vòng lặp Agent Loop cài cắm lệnh switch/case dựa trên `ROUTING_RULES.yaml`.
  - Khớp luật -> C# ép hệ thống gọi Tool (tước quyền AI).
  - Không khớp luật -> C# ném dữ liệu cho Ollama tự tư duy gọi Tool.

## GIAI ĐOẠN 3: NGƯỜI GÁC CỔNG — STATIC TRIAGE PIPELINE
*Mục tiêu: Biến App thành "người gác cổng" biết phân tích file đầu vào thật sự, dùng các công cụ chuyên nghiệp có sẵn thay vì tự viết logic phân tích.*

> **TRIẾT LÝ MỚI (Cập nhật Bước 2 & 3):**
> - **Zero-Configuration Deployment:** Xóa bỏ hoàn toàn `venv` và System Python, chuyển sang dùng **Portable Python (Python Embeddable 3.11)** để App chạy ngay trên mọi máy mà không cần cài đặt rườm rà.
> - **Unified MCP Server:** Gom tất cả LIEF, Entropy vào duy nhất 1 file `server.py` để tiết kiệm RAM.
> - **Native P/Invoke:** Các tool nặng như Binwalk sẽ được bọc (wrapper) bằng Rust và gọi qua C# P/Invoke.

- **Block 3.1: Hợp nhất Kiến trúc (Unified MCP) & Portable Python ✅ ĐÃ XONG**
  - Tích hợp Portable Python 3.11 vào `Tools/Python_Runtime/`.
  - Hợp nhất `md5_server` và `triage_server` thành một `MCP_Unified/server.py` duy nhất.
- **Block 3.2: Triage-aware Routing ✅ ĐÃ XONG**
  - Cấu hình nhánh (Branch Table) gọi `/triage` để lấy JSON kết quả trước khi định tuyến.
- **Block 3.3: LIEF & DIE Core Integration – Xóa sổ Subprocess ✅ ĐÃ XONG**
  - Xóa bỏ công cụ cồng kềnh `die_cli`. Thay vào đó, gọi trực tiếp thư viện `die_core.dll` qua C# P/Invoke. Trả về kết quả JSON siêu chi tiết về Compiler/Protector/Packer thẳng vào giao diện mà không tốn chi phí spawn process.
- **Block 3.4: Kiểm thử luồng Triage Tĩnh (Integration Test) ✅ ĐÃ XONG**
  - Dùng một file mồi (vd: `Audition.exe`) để chạy kiểm thử luồng từ C# -> Portable Python (LIEF) + C# P/Invoke (DIE/Binwalk).
  - Tích hợp thành công JSON-RPC qua stdio để hiển thị kết quả Real-time lên ứng dụng WinUI. Lỗi Python `report_generator` đã được vá hoàn toàn.
- [x] **Block 3.5: Tích hợp Binwalk v3 (Rust C ABI)** **(✅ ĐÃ XONG)**
  - Tải mã nguồn gốc Binwalk (Rust).
  - Inject hàm wrapper C ABI `binwalk_scan` trả về JSON, và `binwalk_free` giải phóng vùng nhớ con trỏ `*mut c_char`.
  - Build `binwalk_core.dll` qua `cargo build --crate-type cdylib`.
  - Triển khai C# P/Invoke qua `BinwalkWrapper.cs` đọc trực tiếp kết quả JSON full mà không qua Python.
  - Hủy gọi subprocess Python, Python server chỉ trả về cờ `deep_scan: "Pending C# processing"` để C# tự kích hoạt Binwalk nếu cần.
- **Block 3.6: Authenticode Signature Verification (Windows API) ✅ ĐÃ XONG**
  - P/Invoke thẳng vào `wintrust.dll` để xác thực chữ ký số siêu nhanh.
- **Block 3.7: Chuẩn hóa Triage Report & Cải tiến Giao diện ✅ ĐÃ XONG**
  - Trình bày kết quả Triage JSON đẹp mắt lên giao diện UI của WinUI 3 (Dùng thẻ Card, Badge màu sắc).
  - Tích hợp hiệu ứng nền Kính xuyên thấu (Mica/Acrylic) và tự động bo góc các khối chức năng. Tự động xử lý các rác dữ liệu LIEF (như Section bị trống tên `[UNNAMED]`). Cải thiện trải nghiệm Fluent Design tổng thể.

## GIAI ĐOẠN 4: TỰ ĐỘNG HÓA BÁO CÁO VÀ ĐÓNG GÓI
- **Block 4.1:** Gắn kết đầu ra. Tool trả kết quả về C#, Branch Table ném cho AI tổng hợp báo cáo.
- **Block 4.2:** Tối ưu hóa file `.csproj` và dùng lệnh `winapp package` để đóng gói MSIX.

## GIAI ĐOẠN 5: ĐÁNH CHIẾM RING 0 (Windows Kernel & Memory)
*Mục tiêu: Xử lý Anti-Cheat và Packer mức hệ điều hành.*
- **Block 5.1: Giao tiếp Kernel-User (C++/Rust)**
  - Đọc KsDumper.sys, viết CLI gọi thẳng Driver bằng Rust/C++.
- **Block 5.2: Tự động hóa MemProcFS (C#)**
  - Viết MCP C# gọi `vmm.dll` lấy danh sách process ẩn.

## GIAI ĐOẠN 6: KỸ NGHỆ ENGINE GAME
*Mục tiêu: Giải mã cấu trúc ảo hóa của Unity và Dart.*
- **Block 6.1: Cấu trúc ảo hóa Game (Engine VM)**
  - Bọc Il2CppDumper thành MCP. Gắn logic nhận diện Unity vào routing.

## GIAI ĐOẠN 7: ĐỈNH CAO TOÁN HỌC & GIẢ LẬP ĐỘNG
*Mục tiêu: AI tự viết thuật toán giải mã.*
- **Block 7.1: Giả lập CPU (Emulation) với Qiling**
  - Dùng AI tạo shellcode C++ an toàn ném cho Qiling chạy ngầm lấy Registers.
- **Block 7.2: Symbolic Execution & SMT Solver**
  - Dùng Triton-MCP kết hợp Z3 Solver bẻ gãy mã hóa thuật toán (XOR+SHIFT).

## GIAI ĐOẠN 8: BIÊN GIỚI MOBILE (Android RE & Network)
*Mục tiêu: Đưa Android vào hệ sinh thái quản lý tập trung.*
- **Block 8.1: Frida & ADB Bridge**
  - Python Frida API qua ADB, nhúng JS quét RAM tìm dex.
- **Block 8.2: Kernel Network Sniffing (eBPF)**
  - Bọc eCapture thành MCP Server chạy qua WSL2.
