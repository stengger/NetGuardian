# NetGuardian

面向远程 Windows 实验室电脑的 Wi-Fi 监控与恢复工具。包含 WPF 管理界面和无界面后台守护，要求 .NET 8 桌面运行时；开发、构建才需要 .NET 8 SDK。

## 恢复行为

- 按策略检测无线设备、SSID、IPv4、DNS 和多个公网 HTTPS 目标。
- 当前 Wi-Fi 关联正常但校园网上游失效时，尝试重连并验证真实联网；仍失败则按优先级切到另一 Wi-Fi。
- 只有公网验证成功才报告恢复。失败网络有短期退避，整轮恢复有截止时间。
- 网关不响应 Ping 不直接认定断网；HTTPS 不把认证页重定向算作成功。
- 代码 10 等 PnP 无线设备故障独立诊断。高风险恢复明确开启后，才尝试 WLAN 服务、网络接口或 PnP 设备重启。
- 后台恢复动作冷却记录持久化，进程重启不能绕过。整机重启默认关闭，仅可用于持续设备故障，并且最多每 24 小时尝试一次。

两个 Wi-Fi 必须已经保存 Windows 无线配置，且备用网络确实能独立上网。网页或专用客户端的校园认证尚不支持自动登录；同一网卡物理损坏或两个上游同时故障时，软件不能保证恢复。

## 构建、测试和发布

如果 SDK 不在 PATH，先设置环境变量（不修改系统 PATH）：

```powershell
$env:NETGUARDIAN_DOTNET = '你的 dotnet.exe 完整路径'
.\scripts\build.ps1
& $env:NETGUARDIAN_DOTNET test tests\NetGuardian.Tests\NetGuardian.Tests.csproj
.\tests\Test-GuardianScripts.ps1
.\scripts\publish.ps1
```

显式 SDK 路径用户也可用 `& $env:NETGUARDIAN_DOTNET test ...`。发布脚本先还原、测试，再输出：

```text
dist\NetGuardian\NetGuardian.exe                    管理界面
dist\NetGuardian\guardian\NetGuardian.Guardian.exe  后台守护
```

直接运行发布界面，或使用 `Start-NetGuardian.vbs`。启动正式版不再要求 SDK；启动脚本不会重定向 APPDATA。退出界面时等待正在执行的网络恢复收尾。

## 安装无人值守后台

1. 在界面保存两个 Wi-Fi，配置优先级、探针和恢复策略。高风险恢复按需显式开启。
2. 管理员 PowerShell 先检查安装内容：

```powershell
.\scripts\register-netguardian-task.ps1 -WhatIf
```

3. 在具备本地回退方式的维护窗口安装：

```powershell
.\scripts\register-netguardian-task.ps1 -RunNow
```

安装程序将发布文件复制到 `%ProgramFiles%\NetGuardian`，首次将用户配置复制到 `%ProgramData%\NetGuardian\settings.json` 并限制写权限；后续默认保留已有机器配置。注册 SYSTEM 开机任务和每分钟监督任务。无需自动登录，也不依赖交互桌面。主任务跟踪真实 Guardian 进程，异常退出会重试；心跳过期会核对任务执行目标和本次启动时间后重新启动。

`-RunNow` 会立即开始监控；不加此参数时，监督任务也会在约一分钟内启动后台。安装后可能按既有策略切换网络，远程会话可能中断。注册安装完成不等于真实无人值守验收通过。

安装后重新打开界面，自动使用机器配置。后台每五秒热加载有效配置，每分钟与当前身份能读取的 Windows 无线配置同步凭据可用状态，避免旧界面标记排除已保存的备用网络；Windows 配置查询失败时保留最近有效状态。界面显示后台状态，并避免与后台同时自动或手动切网；执行手动网络修改前需在维护窗口停止后台。

安装前后可使用只读诊断，检查当前身份、可用无线配置和实际公网探针；此命令不会自动启用网卡或切网：

```powershell
.\dist\NetGuardian\guardian\NetGuardian.Guardian.exe --diagnose --settings '<完整配置路径>' --output '<诊断输出路径>'
```

健康、Windows 配置可读取且至少两个启用的自动连接候选实际可用时返回 0，否则返回 2；输出不包含无线密码。`--validate` 仅检查磁盘配置，不依据可能过时的凭据标记判断 Windows 中的实际可用性。

需要定位后台扫描差异时，在 `--diagnose` 后加 `--scan`，额外输出原生扫描错误码、原生及 netsh 可见网络；不会执行连接或恢复动作。

无线接口被禁用后，即使 WLAN 接口暂时消失，后台也会检查仍存在的 PnP 无线设备。设备代码 22 使用精确实例执行启用；代码 10 等启动故障继续走分级设备恢复，不能当作普通接口禁用处理。

更新前请先停止旧后台；卸载先停止监督，再请求后台退出并等待恢复收尾，保留程序、配置和日志：

```powershell
.\scripts\unregister-netguardian-task.ps1
```

机器配置存在时，界面仍使用它；取消后台后可手动启动界面监控。

## 可选整机重启

只针对长时间持续的无线设备故障，不针对普通上游断网。需在共享 settings.json 显式设置：

```json
{
  "enableComputerRestart": true,
  "deviceFailureMinutesBeforeRestart": 30
}
```

以上是已有配置的字段示例，不能拿它替换完整配置。默认关闭，最短设备故障等待十分钟。还需启用高风险恢复及服务、网卡恢复策略：同一次持续设备故障中，两种恢复必须实际尝试过，冷却跳过或取消不算；恢复正常或转为上游故障时清除计时和动作记录。重启前重新检查设备错误码，并限制每二十四小时最多一次，恢复记录损坏或不可写时禁止此动作。

预留预算后会发出六十秒倒计时重启，Windows 可能强制关闭应用；取消倒计时使用管理员命令 `shutdown /a`。实验任务、磁盘加密和远程软件的开机行为必须在启用前确认。

## 配置与日志

- 未安装后台：`%APPDATA%\NetGuardian`。
- 安装后台后：`%ProgramData%\NetGuardian`，界面与后台共用。
- `settings.json` / `.bak`：配置和备份，不保存明文无线密码。
- `logs\app-YYYYMMDD.log`：按天记录；`events-YYYYMM.jsonl`：恢复事件。
- `guardian-status.json`：进程身份、心跳及最近检测结果。
- `recovery-budget.json`：跨进程恢复冷却；`alerts\last-high-risk.json`：最近高风险动作。

## 项目目录

```text
src/NetGuardian.Core      检测、恢复状态机、无线 API、配置和日志
src/NetGuardian.App       WPF 管理界面
src/NetGuardian.Guardian  不依赖登录的后台进程
tests                    xUnit 场景测试与无破坏脚本测试
scripts                  构建、发布、安装、卸载和心跳监督
docs                     本轮计划、验证记录；archive 保存历史设计与审查
```

历史报告不是当前功能清单。没有真实故障验收前，不应把模拟测试通过等同于无人值守可靠性已得到现场确认。

本机 2026 年 10 月现场验收已覆盖：仍关联但上游失效时切换另一独立 Wi-Fi、禁用设备自动启用、精确 PnP 重启、守护崩溃后监督重拉，以及重启后未登录时 SYSTEM 后台自行恢复公网。原始 Code 10 故障未再次复现，不能将上述结果称为该原始故障已实测修复；设备故障自动重启的判定和预算已有分模块测试，完整 Code 10 组合模拟仍可补充。见[实施计划与完成审计](docs/2026-10-18-unattended-recovery-plan.md)。
