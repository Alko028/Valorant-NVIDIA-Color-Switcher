[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $projectDir 'ValorantColorSwitcher.exe'
$startupDir = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupDir 'VALORANT NVIDIA Color Switcher.lnk'

# 只结束本目录中的旧实例，避免占用待编译的 exe。
Get-CimInstance Win32_Process -Filter "Name='ValorantColorSwitcher.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath) -eq [IO.Path]::GetFullPath($exe)) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

& (Join-Path $projectDir 'build.ps1')

Write-Host '正在检查 NVIDIA 接口 ...' -ForegroundColor Cyan
$check = Start-Process -FilePath $exe -ArgumentList '--diagnose' -Wait -PassThru
$diagnostic = Join-Path $projectDir 'diagnostic.txt'
if ($check.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $diagnostic) { Get-Content -LiteralPath $diagnostic }
    throw 'NVIDIA 接口检查失败，请查看 diagnostic.txt。'
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $projectDir
$shortcut.Description = '自动切换 VALORANT / Riot Client 的 NVIDIA 颜色设置'
$shortcut.Save()

# 先确保桌面为图 2，再启动后台监听。
$restore = Start-Process -FilePath $exe -ArgumentList '--restore' -Wait -PassThru
if ($restore.ExitCode -ne 0) {
    throw '恢复桌面颜色失败，请查看日志。'
}
Start-Process -FilePath $exe -WorkingDirectory $projectDir

Write-Host ''
Write-Host '安装完成。程序已在后台运行，并已加入当前用户的开机启动。' -ForegroundColor Green
Write-Host '任务栏右下角托盘图标可以手动切换、重载配置或退出。'
