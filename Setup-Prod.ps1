# Setup-Prod.ps1 — RE_Ecosystem Production Installation
#
# MUC DICH: Huong dan cai dat va setup moi truong cho RE_Ecosystem.
# Antigravity CLI (agy.exe) da bi loai bo - khong con la target cua du an.
#
# DEV SETUP: Chay scripts/dev/Build-Native-Deps.ps1 de build DIE, Binwalk, Python runtime tu source.
# PRODUCTION: Chi can cac file trong lib/ va Rules/ da duoc bundle.

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  RE ECOSYSTEM - INSTALLATION GUIDE" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Antigravity CLI (agy.exe) da bi loai bo khoi du an."
Write-Host ""
Write-Host "DEVELOPMENT SETUP:" -ForegroundColor Yellow
Write-Host "  Chay scripts/dev/Build-Native-Deps.ps1 de build native dependencies:" -ForegroundColor Yellow
Write-Host "    .\scripts\dev\Build-Native-Deps.ps1 -Mode BuildOnly" -ForegroundColor Yellow
Write-Host ""
Write-Host "PRODUCTION DEPLOYMENT:" -ForegroundColor Green
Write-Host "  Cac file trong lib/ va Rules/ da duoc bundle." -ForegroundColor Green
Write-Host "  Chay .\scripts\Setup-Runtime.ps1 de provision Python va DIE rules da pin." -ForegroundColor Green
Write-Host ""
Write-Host "Xem $CommitLocks trong scripts/dev/Build-Native-Deps.ps1 de biet commit locks." -ForegroundColor Cyan
Write-Host ""
