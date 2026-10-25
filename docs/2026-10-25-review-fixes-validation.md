# 项目审查问题修复验证

## 修复范围

- `RecoveryBudget.cs`、Guardian `Program.cs`：整机重启同时核对当前高风险主开关、细分动作授权、持续故障及动作完成证据；设备复核后重新读取磁盘策略。沿用已有两种细分动作均关闭时的旧配置兼容规则。
- `RecoveryPlanner.cs`、`MonitoringEngine.cs`、`MainWindow.xaml.cs`：SSID 按原始值与 Ordinal 比较，候选排除、关联校验、失败冷却、界面选择和结果记录遵循相同身份规则。
- `NetshWifiClient.cs`：网络及配置名称解析保留首尾空格和大小写；导入密码时对直接命中和列表回退都核对配置内真实 SSID，不凭配置名称认定网络。
- `PublicIpInfoService.cs`、`MainWindow.Diagnostics.cs`：默认查询保留缓存；手动刷新明确绕过缓存，成功后更新缓存。
- `RecoveryBudgetTests.cs`、`MonitoringTests.cs`、`UnattendedEngineTests.cs`、`NetshConnectionTests.cs`、`NetshParserTests.cs`、`PublicIpInfoTests.cs`：增加或校正以上行为的回归覆盖。

## 最终验证命令

以下 `dotnet` 指工作区 `.dotnet-home/sdk/dotnet.exe`，版本 8.0.426；设置 `DOTNET_CLI_HOME=.dotnet-home`。

```powershell
dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore -p:MSBuildEnableWorkloadResolver=false -v:normal
dotnet build NetGuardian.sln --no-restore -p:MSBuildEnableWorkloadResolver=false -p:BuildInParallel=false -m:1 -v:normal
dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-build --no-restore -v:normal
./tests/Test-GuardianScripts.ps1
git diff --check -- src tests
```

- 核心完整套件：241/241 通过，无跳过，退出 0；使用正常权限运行，以允许测试在临时目录原子替换文件。
- 解决方案构建：退出 0；保留原有两条 WFAC010 高 DPI manifest 建议，不在本轮修改范围内。
- 桌面完整套件：155/155 通过，无跳过，退出 0。
- 无破坏脚本测试及差异空白检查：退出 0。计划任务和进程调用全部由测试替身执行。
- 完整最终日志位于工作区忽略目录 `.dotnet-home/fix-final-core.log`、`fix-final-build.log`、`fix-final-app.log`。

## 失败证据及处理

没有删除、禁用或跳过测试。以下故意先失败的回归均保留完整输出和堆栈，修复后由完整套件覆盖：

| 日志（均在 `.dotnet-home/`） | 失败原因 | 退出码 |
| --- | --- | --- |
| `fix-restart-red.log` | 新完整配置判定入口尚未实现，CS1503 参数类型不匹配 | 1 |
| `fix-ssid-red.log` | 精确比较预期 false，实际 true；备用候选被排除 | 1 |
| `fix-ssid-cooldown-red.log` | 预期尝试两个不同名称的网络，实际仅尝试 Office | 1 |
| `fix-ssid-password-red.log` | 预期不匹配网络返回 null，实际返回另一网络的测试密码 | 1 |
| `fix-ip-red.log` | 强制刷新参数尚未实现，CS1739 | 1 |
| `fix-ip-behavior-red.log` | 预期新地址 203.0.113.20，实际返回缓存 203.0.113.10 | 1 |
| `fix-ssid-parser-red.log` | 名称空格被裁剪，大小写不同的配置被合并 | 1 |
| `fix-ssid-password-direct-red.log` | 同名配置内真实 SSID 不匹配时，仍返回了测试密码 | 1 |

SSID 针对性测试首轮 `fix-ssid-green.log` 有一项配置持久化测试失败：`System.UnauthorizedAccessException : Access to the path is denied.`，发生于 `System.IO.FileSystem.MoveFile`、`SettingsStore.cs:82`，退出 1。相同代码在正常权限下完整测试通过；没有修改持久化代码或断言来回避沙箱限制。首次 Git 写入也因 `.git/index.lock` 权限拒绝而退出 1，获得正常 Git 写入权限后完成提交。

独立代码审查指出导入密码的直接命中路径还需验证真实 SSID；已补充对应失败回归并修复。审查者复核后确认该问题闭环，未发现其他阻止交付的明确缺陷。

## 验收边界

本轮验证未执行真实断网、无线切换、设备重启、整机重启或安装更新。源码修复与模拟回归通过不等于上述故障的现场验收完成。用户原有 `docs/2026-10-18-validation.md` 修改保持原样，不纳入本轮提交。
