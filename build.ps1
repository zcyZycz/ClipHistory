# 编译 ClipHistory.exe
# 依赖：Windows + .NET Framework 4.x（系统自带 csc.exe，无需安装任何 SDK）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\build.ps1

param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$out = Join-Path $distDir 'ClipHistory.exe'

# 找 csc.exe
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'csc.exe not found. Requires .NET Framework 4.x.'
}

if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

# 把使用说明复制成 ASCII 名，作为 exe 内嵌资源
$readme = Join-Path $root 'docs\使用说明.md'
$embedded = Join-Path $distDir 'readme.md'
Copy-Item $readme $embedded -Force

if (Test-Path $out) { Remove-Item $out -Force }

Write-Host "Building ClipHistory..." -ForegroundColor Cyan

$args = @(
    '/nologo'
    '/target:winexe'
    '/optimize+'
    '/codepage:65001'
    "/win32icon:$srcDir\app.ico"
    "/win32manifest:$srcDir\app.manifest"
    "/resource:$embedded,readme.md"
    "/out:$out"
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
    "$srcDir\ClipHistory.cs"
)

& $csc $args

if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
if (-not (Test-Path $out)) { throw 'Output exe missing' }

$f = Get-Item $out
$hash = (Get-FileHash $out -Algorithm SHA256).Hash

Write-Host ""
Write-Host ("OK    : {0}" -f $f.FullName) -ForegroundColor Green
Write-Host ("Size  : {0:N1} KB" -f ($f.Length / 1KB))
Write-Host ("SHA256: {0}" -f $hash)
Write-Host ""
Write-Host ("Version: {0}" -f $f.VersionInfo.FileVersion)
Write-Host ("Company: {0}" -f $f.VersionInfo.CompanyName)
