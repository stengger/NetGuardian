# 手动网络操作的后台交接

## 协议与行为

界面手动连接 Wi-Fi、设置凭据、修改 DNS、续租 DHCP 或执行网络维护时，通过机器数据目录中的临时请求向后台申请控制权。后台停止自动调度，取消当前恢复并等待收尾，随后释放原有 `monitor.lock`；界面取得同一把锁后才允许修改网络。后台进程和心跳保留，不停止、删除或重建计划任务。

请求写在 `manual-control.json`，包含随机请求标识和 20 秒有效期，每 5 秒续期。`manual-control.lock` 序列化来自多个界面的操作。心跳的 `ControlProtocolVersion=1` 表示后台支持此协议，`State=ManualControl` 表示正在为手动操作交权。旧版后台或另一个不支持交接的监控窗口仍保持原控制权，界面应显示明确的升级或停止另一窗口的提示。

只有当前恢复确已结束，后台才能释放监控锁；操作期间即使请求续期失败，界面仍持有监控锁，后台不能抢占。操作结束先释放监控锁、撤销本次请求，再释放请求锁。界面异常退出后操作锁由系统释放，请求过期后后台自动恢复。后台恢复后给网络 30 秒稳定时间，期间地址变化事件也不能提前触发自动恢复，避免立即干扰手动选择和网络认证。

正常后台循环仍每 5 秒执行一次，不增加常态扫描或公网检测；手动操作期间缩短为 250 毫秒检查一次交接状态。此时跳过整机重启判断。原有监督脚本继续根据新鲜心跳判断后台存活。

## 修改范围

- `Runtime/ManualNetworkAccess.cs`：请求文件、界面手动操作租约、后台交权状态机，使用现有监控锁保持跨进程独占。
- `Runtime/GuardianRuntime.cs`：心跳增加兼容旧数据的协议版本字段。
- `NetGuardian.Guardian/Program.cs`：处理请求、等待恢复收尾、保留心跳、自动恢复、交接期间禁止重启判断。
- `Monitoring/MonitoringEngine.cs`：可选启动稳定期，自动定时及网络事件均尊重该期限；普通启动行为不变。
- `ManualNetworkAccessTests.cs`、`AdaptiveMonitoringTests.cs`：覆盖控制权独占、收尾、取消、超时、旧后台、过期、重试和稳定期。

## 首轮验证

使用仓库内 `.dotnet-home/sdk/dotnet.exe` 8.0.426，设置 `DOTNET_CLI_HOME` 为仓库 `.dotnet-home`。

- `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --logger 'console;verbosity=minimal'`：278 项通过，0 失败、0 跳过，退出码 0。包含后续界面连接验证服务的 8 项测试。
- `dotnet build src/NetGuardian.Guardian/NetGuardian.Guardian.csproj --no-restore -v minimal`：0 警告、0 错误，退出码 0。
- `tests/Test-GuardianScripts.ps1`：语法、5 个监督场景、无 SDK 启动、安全卸载全部通过，退出码 0；系统任务和进程调用均为替身。
- 完整输出位于忽略目录 `.dotnet-home/validation/manual-network/` 下的 `control-tests.log`、`core-final.log`、`guardian-final-build.log`、`guardian-scripts.log`。

这些测试不改变本机网络或真实后台。界面接入、其他流程检查及最终集成回归另见体验检查报告。
