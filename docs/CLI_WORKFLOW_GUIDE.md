# Hướng Dẫn Quản Trị WinUI 3 & Windows App SDK Bằng Command Line

> **Lưu ý (13/10/2026):** Các ví dụ scaffold/build bên dưới là hướng dẫn WinUI lịch sử; chúng không mô tả headless Platform host. RE Platform Core hoạt động không cần Command Center hoặc RE Agent Runtime/AI Harness; H3 CLI/MCP phục vụ Core services. Xác minh script/template/package còn tồn tại trước khi dùng lệnh mẫu. Trạng thái implementation: [`CURRENT_STATE.md`](CURRENT_STATE.md); kiến trúc host: [`INTEROPERABILITY.md`](INTEROPERABILITY.md).

Tài liệu này tổng hợp toàn bộ các lệnh dòng lệnh (CLI) để tạo mới, nâng cấp, dọn dẹp và quản lý vòng đời ứng dụng WinUI 3 / Windows App SDK trong dự án **RE_Ecosystem**.

---

## 1. Tạo Ứng Dụng Mới Bằng .NET CLI (`dotnet new`)

Trước đây bạn đã từng dùng `dotnet new` để scaffold dự án WinUI 3. Dưới đây là các cú pháp chuẩn:

### 1.1 Cài đặt & cập nhật Template Pack
Để .NET CLI nhận diện các mẫu WinUI 3, cài đặt gói template chính thức:
```powershell
# Cài đặt template pack
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates

# Kiểm tra xem template pack có bản cập nhật mới không
dotnet new update --check-only

# Cập nhật tất cả template pack lên bản mới nhất
dotnet new update
```

### 1.2 Các lệnh tạo dự án WinUI 3
```powershell
# 1. Tạo WinUI 3 theo kiến trúc MVVM (Khuyên dùng)
dotnet new winui-mvvm -n TenDuAn

# 2. Tạo WinUI 3 Blank App (Dự án tối giản)
dotnet new winui -n TenDuAn

# 3. Tạo WinUI 3 có sẵn thanh điều hướng NavigationView
dotnet new winui-navview -n TenDuAn

# 4. Tạo WinUI 3 có giao diện TabView
dotnet new winui-tabview -n TenDuAn

# 5. Tạo WinUI 3 Class Library (Dùng làm module/thư viện chia sẻ)
dotnet new winui-lib -n TenThuVien
```

---

## 2. Tạo Ứng Dụng Mới Bằng WinApp CLI (`winapp new`)

Nếu bạn sử dụng công cụ chuyên dụng `winapp` (Microsoft WinApp CLI 0.6+):
```powershell
# Xem danh sách template có sẵn
winapp new --list

# Tạo app mới với cấu hình mặc định và phiên bản template mới nhất
winapp new --name TenDuAn --template winui-mvvm --template-version latest --use-defaults
```

---

## 3. Tự Động Hóa Quét, Nâng Cấp & Xóa Bản Cũ Bằng Script

Dự án cung cấp script PowerShell tương tác tại [`scripts/dev/Update-DotNet-Deps.ps1`](../scripts/dev/Update-DotNet-Deps.ps1). **Bạn chỉ cần mở lên chạy mà không cần nhớ bất kỳ tham số nào**:

```powershell
# Chạy script trực tiếp:
.\scripts\dev\Update-DotNet-Deps.ps1
```

### Quy trình hoạt động của script:
1. **Tự động quét:** Script tự động kiểm tra bản cập nhật của Template Pack và toàn bộ các gói NuGet trong solution.
2. **Hiển thị Menu lựa chọn:**
   ```text
   =========================================================
                        LỰA CHỌN HÀNH ĐỘNG                  
   =========================================================
     [1] Update         : Cập nhật package & template lên bản mới nhất
     [2] Update & Clean : Cập nhật + Dọn sạch bin/obj/cache/bản cũ + Build verify
     [3] Exit           : Thoát chương trình
   =========================================================
   ```
3. **Thực thi:** Bạn chỉ cần nhập phím `1`, `2` hoặc `3` tương ứng với nhu cầu.

---

## 4. Quản Lý Thư Viện Thủ Công Bằng `dotnet CLI`

Nếu muốn thêm, sửa hoặc xóa từng thư viện bằng tay:

```powershell
# Thêm hoặc nâng cấp 1 package lên bản stable mới nhất (không cần truyền --version)
dotnet add .\RE.WinUI\RE.WinUI.csproj package Microsoft.WindowsAppSDK
dotnet add .\RE.WinUI\RE.WinUI.csproj package CommunityToolkit.Mvvm

# Gỡ bỏ package
dotnet remove .\RE.WinUI\RE.WinUI.csproj package TenPackage

# Khôi phục dependencies cho toàn bộ solution
dotnet restore RE_Ecosystem.slnx

# Dọn dẹp cache NuGet
dotnet nuget locals http-cache --clear
dotnet nuget locals temp --clear
```

---

## 5. Build và Chạy Ứng Dụng Bằng Command Line

### 5.1 Sử dụng `winapp run` (Bắt lỗi XAML và stowed-exceptions tốt nhất)
```powershell
# Chạy kèm chuẩn đoán lỗi XAML
winapp run .\RE.WinUI\RE.WinUI.csproj --debug-output

# Chạy bản Release x64
winapp run .\RE.WinUI\RE.WinUI.csproj -c Release --arch x64
```

### 5.2 Sử dụng `dotnet run` (Nhờ hook `Microsoft.Windows.SDK.BuildTools.WinApp`)
```powershell
dotnet run --project .\RE.WinUI\RE.WinUI.csproj
```
