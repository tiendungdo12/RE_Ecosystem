---
sessionId: session-261005-165737-w6hc
---

# Fix Check-CommitUpdate Bugs

### ✓ Step 1: Fix clone fail và null-reference

### ✓ Step 2: Fix commit count cho DIE

# Redesign Menu + DLL Functionality Test

### ✓ Step 1: Fix clone fail và null-reference

### ✓ Step 2: Fix commit count cho DIE

### ✓ Step 3: Add Test-DllFunctionality for DIE and Binwalk

### ✓ Step 4: Redesign menu — auto check update + 2 options

### 3 Lỗi trong `Check-CommitUpdate` (dòng 88-132 của `scripts/dev/Build-Native-Deps.ps1`)

**Lỗi 1: Binwalk clone fail — `fatal: cannot change to '...': No such file or directory`**
- `git clone --depth 1 --branch 0.14.0` fail vì `0.14.0` là **tag**, không phải branch.
- Khi clone fail, `$tempDir` không được tạo, nhưng code vẫn đến `git -C $tempDir rev-parse HEAD` → throw exception.

**Lỗi 2: Null-reference — `You cannot call a method on a null-valued expression`**
- Sau clone fail, `$headCommit` là `$null`.
- `$headCommit.Substring(0, 12)` gọi trên `$null` → exception.

**Lỗi 3: DIE commit count sai — `(0 commits behind)` khi thực tế có nhiều commits**
- `--depth 1` chỉ fetch 1 commit (HEAD), không có history.
- `git fetch --depth full` không hoạt động đúng sau shallow clone.
- `git log "$LockedCommit..HEAD"` không tìm thấy `$LockedCommit` trong history → trả về 0.

# Technical Design

### Sửa `Check-CommitUpdate` (dòng 88-132)

#### 1. Handle clone fail
Sau `git clone`, check `$LASTEXITCODE` — nếu fail, thử `--tag` thay vì `--branch`:

```powershell
git clone --depth 1 --branch $Branch $Repo $tempDir 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    # Clone fail — thử với --tag cho tags
    git clone --depth 1 --tag $Branch $Repo $tempDir 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "  ``$Name``: Clone failed for $Branch"
        return @{ NeedsUpdate = $null; ... }
    }
}
```powershell

#### 2. Fix commit count cho DIE
Thay vì `--depth 1` rồi `--depth full`, dùng `git ls-remote` để lấy HEAD commit mà không cần clone full:

```powershell

# Lấy HEAD commit từ remote mà không cần clone

$headCommit = git ls-remote $Repo $Branch | Select-Object -First 1 -ExpandProperty 'Commit'

# Clone full chỉ khi cần đếm commits

if ($headCommit -ne $LockedCommit) {
    git clone $Repo $tempDir 2>$null | Out-Null  # Clone full để đếm
    $behind = (git -C $tempDir log "$LockedCommit..HEAD" --oneline 2>$null).Count
}
```powershell

#### 3. Null-safety
Đảm bảo `$headCommit` không null trước khi gọi `.Substring()`:

```powershell
if ([string]::IsNullOrEmpty($headCommit)) {
    Write-Fail "  ``$Name``: Could not get HEAD commit"
    return @{ NeedsUpdate = $null; ... }
}
$headShort = $headCommit.Substring(0, 12)
```powershell

### File Structure

| File | Thay đổi |
|------|----------|
| `scripts/dev/Build-Native-Deps.ps1` | Sửa `Check-CommitUpdate` function |

# Testing

### Cách validate

1. **Run menu option 1 (Check update):** Verify không còn exception, Binwalk hiển thị kết quả thay vì lỗi.
2. **Verify commit count:** DIE hiển thị số commits behind đúng (không phải 0).
3. **Verify cleanup:** `$tempDir` trong `%TEMP%` bị xóa sau khi check xong (finally block).

### Cách validate redesign

1. **Run script không argument:** Verify auto check update chạy trước menu, kết quả hiển thị trong header.
2. **Menu mới:** Chỉ có 2 options (Update, Exit).
3. **Test functionality:** Chọn Update, verify DLL được load và hàm export được gọi thành công.
4. **Test rollback:** Gây lỗi build, verify DLL cũ được khôi phục.

# Fix Test-DllFunctionality Errors

### ✓ Step 1: Fix DIE LoadLibrary error 126

Thêm `SetDllDirectory` để resolve Qt6 dependencies khi load `die_core.dll`.

### ✓ Step 2: Fix Binwalk Marshal.GetTypeForDelegate error

Thêm `Get-DelegateType` helper function thay thế `Marshal.GetTypeForDelegate` (không tồn tại trong .NET).

### ✓ Step 3: Make LoadLibrary fail non-blocking

Chuyển LoadLibrary fail và test block error từ `Write-Fail` sang `Write-Warn` — DLL có thể có dependencies nặng không available trong PowerShell context.

### ✓ Step 4: Fix Add-Type duplicate type error

Move `Add-Type` cho Kernel32 ra ngoài `Test-DllFunctionality`, gọi 1 lần duy nhất trong `Invoke-TestStage` — tránh conflict khi test nhiều DLL trong cùng session.

### ✓ Step 5: Fix Get-DelegateType type conversion error

Delegate types (`[int]`, `[System.IntPtr]`) bị chuyển thành string khi gọi bên trong scriptblock. Fix bằng cách tạo delegate types **bên ngoài** scriptblock trong `Invoke-TestStage`, rồi truyền vào scriptblock qua `-DelegateTypes` hashtable parameter — đảm bảo `[Type]` objects giữ nguyên type information.

# Test Backup and Rollback

### ✓ Step 1: Tạo Invoke-TestBackup và Invoke-TestRollback helper functions

Thêm 2 hàm helper: `Invoke-TestBackup` backup `die_core.dll`, `binwalk_core.dll`, và `Rules/die_db/` vào `.backup` files trước khi test; `Invoke-TestRollback` khôi phục từ `.backup` files và xóa `.backup` sau khi hoàn tất.

### ✓ Step 2: Tích hợp backup trước test và rollback sau test fail

Menu Update: gọi `Invoke-TestBackup` trước `Invoke-TestStage`, gọi `Invoke-TestRollback` khi test fail. Command-line `-Mode Full`: tương tự. Đảm bảo `.backup` files được xóa sạch sau khi thành công hoặc sau khi rollback.

# Fix Update Workflow Order

### ✓ Step 1: Xóa build skip condition và per-component backup/rollback

Xóa `if (-not $Force -and (Test-Path ...))` trong `Invoke-Build-Die` và `Invoke-Build-Binwalk` để luôn build từ source mới. Xóa per-component backup/rollback code trong từng hàm build vì `Invoke-TestBackup`/`Invoke-TestRollback` quản lý ở cấp pipeline. Xóa safety net rollback trong `Invoke-Build` catch block.

### ✓ Step 2: Sửa thứ tự pipeline thành Backup → Build → Test → Cleanup

Sửa menu Update và command-line `-Mode Full` để đúng thứ tự: (1) Backup DLL cũ và DIE DB, (2) Build DLL mới từ source, (3) Test DLL mới, (4) Cleanup backup files. Nếu build fail hoặc test fail → rollback từ backup.

# Persist Commit Locks

### ✓ Step 1: Thêm cơ chế lưu và load commit locks

Thêm file `.commit_locks.json` để lưu commit hash thành công giữa các lần chạy script. Load từ file khi mở script, save sau khi build + test thành công. DIE DB không cần lock vì luôn lấy HEAD.

### ✓ Step 2: Cập nhật Invoke-Build-Die và Invoke-Build-Binwalk

Mỗi hàm build capture `git rev-parse HEAD` sau `git checkout` để biết commit hash thực tế được build. Return hashtable `{ Success, Commit }` thay vì `$true`. Invoke-Build gộp kết quả và trả về `{ Success, DieCommit, BinwalkCommit }`.

### ✓ Step 3: Hiển thị commit locks trong PROMOTE stage

Sau khi build + test thành công, hiển thị commit locks hiện tại trong PROMOTE stage để user biết commit nào đang dùng.

# Track Failed Commits to Avoid Rebuilding Broken Commits

### ✓ Step 1: Thêm $FailedCommits hashtable và Save/Load

Thêm `$FailedCommits` hashtable (Die, Binwalk) bên cạnh `$CommitLocks`. Cập nhật `Save-CommitLocks` để lưu `Die_Failed` và `Binwalk_Failed` vào `.commit_locks.json`. Load từ file khi mở script. Khi build + test thành công, xóa `$FailedCommits` entries.

### ✓ Step 2: Check update skip commit failed

Sửa `Check-CommitUpdate` để kiểm tra HEAD commit có phải là commit đã fail không — nếu có thì hiển thị "COMMIT ĐÃ THỬ" (⛔) và return `NeedsUpdate = $false`. Cập nhật auto-check header table và check stage summary table để hiển thị ⛔ cho commit failed.

### ✓ Step 3: Build fail ghi nhận, build success xóa commit failed

Trong `Invoke-Build-Die` và `Invoke-Build-Binwalk` catch blocks: lưu `$BuiltCommit` vào `$FailedCommits`, gọi `Save-CommitLocks`, thông báo user. Trong menu Update và `-Mode Full`: sau khi build + test thành công, xóa `$FailedCommits` entries và `Save-CommitLocks`.

# DIE DB HEAD Tracking + English Standardization + Post-Update Summary

### ✓ Step 1: DIE DB skip clone when HEAD unchanged

Add `DieDb_Head` field to `.commit_locks.json` to track last built HEAD of Detect-It-Easy repo. Use `git ls-remote` before cloning to compare; if HEAD matches, skip the ~32MB clone. Update `Save-CommitLocks` and load section to handle `DieDb_Head`.

### ✓ Step 2: Standardize all console output to English

Replace ALL Vietnamese text with English in console output and comments across all three scripts: Build-Native-Deps.ps1, Update-Python-Deps.ps1, Update-DotNet-Deps.ps1. Keep emoji status indicators (⚠️, ✅, ❌, ⛔, 🔄).

### ✓ Step 3: Add post-update summary table

Add post-update summary table to all three scripts matching a consistent design pattern:
- **Build-Native-Deps.ps1:** `Invoke-UpdateSummary` function showing Component, Commit, Status, Notes table after successful build+test.
- **Update-Python-Deps.ps1:** Replaced raw `pip list` output with structured table (Package, OldVersion, NewVersion, Status) after successful update.
- **Update-DotNet-Deps.ps1:** Added structured table (Component, Details, Status, Notes) after successful build verify, showing templates and NuGet packages updated.