# ============================================================================
#  Builds the CargoSync installer end-to-end:
#    1. Publishes a self-contained win-x64 build (no .NET runtime needed on the
#       target PC) into ..\bin\publish
#    2. Compiles Installer\CargoSync.iss with Inno Setup into
#       Installer\Output\CargoSync-Setup.exe
#
#  Usage (from the project root):  powershell -ExecutionPolicy Bypass -File Installer\build-installer.ps1
# ============================================================================
$ErrorActionPreference = "Stop"

$proj      = Join-Path $PSScriptRoot "..\OrganizationImportTool.csproj" | Resolve-Path
$publishDir= Join-Path $PSScriptRoot "..\bin\publish"
$iss       = Join-Path $PSScriptRoot "CargoSync.iss"
$iscc      = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# Make sure no running instance locks the output
Get-Process -Name CargoSync,OrganizationImportTool -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "==> Publishing self-contained win-x64 build..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "==> Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Join-Path $PSScriptRoot "Output\CargoSync-Setup.exe"
if (Test-Path $setup) {
    $sizeMb = [Math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host "==> DONE: $setup ($sizeMb MB)" -ForegroundColor Green
} else {
    throw "Setup file was not produced"
}
