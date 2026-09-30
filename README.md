# VALORANT / Riot Client NVIDIA 颜色自动切换

程序会在后台每秒检查一次进程：

- VALORANT 正在运行，或 Riot 客户端窗口可见时：应用图 1（亮度 65、对比度 65、Gamma 1.10、数字鲜艳度 60、色调 0）。
- 游戏退出且 Riot 客户端没有可见窗口时：恢复图 2（50、50、1.00、50、0）。
- 程序退出、Windows 注销或执行卸载脚本时，也会尝试恢复图 2。

实现不会向 VALORANT 注入 DLL，也不会读取/修改游戏内存；它只调用 NVIDIA 显示接口和 Windows 进程列表。

## 安装

右键 `install.ps1`，选择“使用 PowerShell 运行”。它会：

1. 使用 Windows 自带的 C# 编译器生成 `ValorantColorSwitcher.exe`；
2. 验证本机 NVIDIA 接口；
3. 将快捷方式放入当前用户的“启动”目录；
4. 恢复桌面默认颜色并启动后台监听。

如果 PowerShell 阻止脚本，可在本目录打开 PowerShell 后运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

安装后，任务栏右下角会出现托盘图标。右键可以临时应用游戏颜色、恢复桌面颜色、重新加载配置或退出。

## 修改配置

编辑 `settings.ini` 即可。保存后程序会自动重载，不需要重新编译。

默认只在 `RiotClientUx` 有可见、非最小化窗口时把它当作触发条件，这避免游戏退出后 Riot 后台托盘进程一直阻止颜色恢复。如果希望 Riot 客户端只要在后台运行就触发，把：

```ini
RiotClientRequiresVisibleWindow=false
```

## 卸载

右键 `uninstall.ps1`，选择“使用 PowerShell 运行”。脚本会停止程序、恢复图 2 并移除开机启动，但不会删除本目录中的源码和配置。

## 排查

- NVIDIA 接口检查结果：`diagnostic.txt`
- 运行日志：`%LOCALAPPDATA%\ValorantColorSwitcher\switcher.log`
- 如果驱动更新后颜色没有切换，先退出托盘程序，然后重新运行 `install.ps1`。

本程序默认调整所有 NVIDIA 显示器。只想调整 Windows 主显示器时，在 `settings.ini` 中设置：

```ini
ApplyToAllNvidiaDisplays=false
```
