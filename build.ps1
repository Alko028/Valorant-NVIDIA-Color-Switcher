[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $projectDir 'ValorantColorSwitcher.cs'
$output = Join-Path $projectDir 'ValorantColorSwitcher.exe'
$icon = Join-Path $projectDir 'assets\app-icon.ico'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 Windows C# 编译器：$compiler"
}
if (-not (Test-Path -LiteralPath $icon)) {
    throw "找不到程序图标：$icon"
}

Write-Host '正在编译 ValorantColorSwitcher.exe ...' -ForegroundColor Cyan
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$icon" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    "/out:$output" `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出代码：$LASTEXITCODE"
}

Write-Host "编译完成：$output" -ForegroundColor Green
