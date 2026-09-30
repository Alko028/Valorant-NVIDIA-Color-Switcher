[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $projectDir 'ValorantColorSwitcher.exe'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'VALORANT NVIDIA Color Switcher.lnk'

Get-CimInstance Win32_Process -Filter "Name='ValorantColorSwitcher.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath) -eq [IO.Path]::GetFullPath($exe)) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

if (Test-Path -LiteralPath $exe) {
    $restore = Start-Process -FilePath $exe -ArgumentList '--restore' -Wait -PassThru
    if ($restore.ExitCode -ne 0) {
        Write-Warning '桌面颜色自动恢复失败，请在 NVIDIA 控制面板中恢复默认值。'
    }
}

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

Write-Host '已停止程序、恢复桌面颜色，并移除开机启动。程序文件仍保留在当前目录。' -ForegroundColor Green
