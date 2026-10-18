# 无人值守恢复优化验证记录

日期：2026-10-18，北京时间。目标是自动处理“关联校园 Wi-Fi 但上游失效”、备用网络切换及无线设备故障，并准备开机未登录时运行的守护。

## 最终结果

- 完整 Debug 解决方案构建：成功，退出码 0，0 警告、0 错误，包含 Core、App、Guardian、Tests。
- Debug xUnit：135/135 通过，退出码 0。
- Release 发布脚本：还原成功，135/135 测试通过，App/Guardian 均发布成功，退出码 0。
- Windows PowerShell 5 脚本测试：语法检查、五个监督场景、无 SDK 正式版启动、安全卸载全部通过，退出码 0；任务和进程均为替身。
- 深浅主题对比度：10/10 对比通过，退出码 0。保留原有主题重构与检查脚本。
- Guardian 发布 exe `--validate`：现有配置共五项，其中两项允许自动切换且已有保存凭据，退出码 0。此校验不执行网络连接，不证明两个上游当下都能上网。
- 空候选及缺失配置的负向校验：均正确返回 78，外层验收脚本退出码 0。
- 安装脚本 `-WhatIf`：成功预览，退出码 0，没有复制系统文件或注册真实任务。
- 两轮独立代码审查发现的取消收尾、过期状态升级、旧心跳、安装版脚本、共享配置覆盖、电池监督、陈旧重启状态问题均已修复并复核。

## 主要命令

SDK：`E:\Environment\dotnet-sdk-8.0\dotnet.exe`（8.0.422）。测试/构建时 DOTNET_CLI_HOME 使用项目 `.dotnet-home`，NUGET_PACKAGES 使用用户 `.nuget\packages`。

```powershell
& 'E:\Environment\dotnet-sdk-8.0\dotnet.exe' restore NetGuardian.sln --configfile NuGet.Config --verbosity normal
& 'E:\Environment\dotnet-sdk-8.0\dotnet.exe' build NetGuardian.sln --no-restore --verbosity minimal
& 'E:\Environment\dotnet-sdk-8.0\dotnet.exe' test tests\NetGuardian.Tests\NetGuardian.Tests.csproj --no-restore --verbosity minimal
$env:NETGUARDIAN_DOTNET = 'E:\Environment\dotnet-sdk-8.0\dotnet.exe'
.\scripts\publish.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-GuardianScripts.ps1
python scripts/check-contrast.py
.\dist\NetGuardian\guardian\NetGuardian.Guardian.exe --validate --settings '<用户配置路径>'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\register-netguardian-task.ps1 -WhatIf
git diff --check
```

## 失败与纠正记录

预期红测退出码均为 1：原 SSID 关联但无公网、候选失效、无轻恢复可执行、停止取消、动态周期、超时、SSID 退避、上游故障不升级；网关屏蔽 Ping、单 HTTPS 目标、重定向、取消；配置 null 字段；服务取消后未启动；代码10设备实例重启；恢复收尾竞争、验证未知/陈旧报告错误升级。生产修复后全量通过，未禁用或跳过用例。

部分新增接口在红测阶段出现预期编译错误，完整错误正文如下；实现接口后通过：

```text
SystemCommandTests.cs(11,26): error CS1729: “ProcessCommandRunner”不包含采用 1 个参数的构造函数
GuardianRuntimeTests.cs(1,24): error CS0234: 命名空间“NetGuardian.Core”中不存在类型或命名空间名“Runtime”(是否缺少程序集引用?)
RecoveryBudgetTests.cs(34,27): error CS0246: 未能找到类型或命名空间名“DeviceFailureTracker”(是否缺少 using 指令或程序集引用?)
```

配置红测完整异常类型/消息：

```text
System.NullReferenceException : Object reference not set to an instance of an object.
System.ArgumentNullException : Value cannot be null. (Parameter 'source')
System.InvalidOperationException : Sequence contains no elements
```

服务收尾及设备重启红测断言：

```text
Assert.Equal() Failure: Collections differ
Expected: string[] ["stop", "start"]
Actual: List<string> ["stop"]
Assert.Contains() Failure: Filter not matched in collection
Collection: [("netsh.exe", ["interface", "set", "interface", "name=Wi-Fi", "admin=disabled"]), ("netsh.exe", ["interface", "set", "interface", "name=Wi-Fi", "admin=enabled"])]
```

一次并发构建出现 CS2012：Core 的 obj DLL 被另一构建占用，退出码 1。随后协调为顺序构建并通过；没有将文件锁误判为运行时或测试框架故障。引擎红绿测试完整输出保留于忽略目录 `.dotnet-home/validation/2026-10-18/.engine-*.log`。

监督脚本测试曾因测试场景未复原“无关任务路径”连续失败三次（退出码均为 1）：

```text
监督动作错误：启动=0，停止=0
At tests/Test-GuardianScripts.ps1:20
位于 Assert-Counts，tests/Test-GuardianScripts.ps1: 行20
位于 <ScriptBlock>，tests/Test-GuardianScripts.ps1: 行38
```

停止盲目重试后根据堆栈定位测试隔离错误，恢复夹具后，Windows PowerShell 5 的完整脚本测试通过。产品没有操作真实任务。

Git 首次暂存失败：遗留 2026-07-07 的零字节锁，已确认无 Git 进程；经授权移除后恢复。随后沙箱内写索引被拒，经授权在沙箱外完成。错误原文：

```text
fatal: Unable to create 'C:/Users/Qiu/Documents/Net/.git/index.lock': File exists.
Another git process seems to be running in this repository, e.g.
an editor opened by 'git commit'. Please make sure all processes
are terminated then try again. If it still fails, a git process
may have crashed in this repository earlier:
remove the file manually to continue.
fatal: Unable to create 'C:/Users/Qiu/Documents/Net/.git/index.lock': Permission denied
```

暂存检查曾报告 `ConnectivityTests.cs:155: new blank line at EOF.`（退出码 1），已清理。自定义禁用 core.autocrlf 的检查还把 CRLF 报为 trailing whitespace；恢复仓库既有 Git 行尾设置后，标准 `git diff --check` 通过。检查没有更改 Git 全局配置。

## 目录清理

- 删除原自研控制台测试入口 Program.cs，保留并完善已迁移的 xUnit 测试。
- 将两处重复命令执行器合并到带截止时间的 ProcessCommandRunner；简化构建/发布公共环境初始化，不再覆盖 APPDATA，也不再在还原失败时使用可能过期的资产文件继续构建。
- OVERVIEW.md、UI_REVIEW.md 及七月旧设计/计划移到 docs/archive/2026-07，历史信息保留，README 为当前使用说明。
- 将临时引擎验证日志移入 .dotnet-home/validation，根目录不再堆放日志。
- 构建产物、用户配置与凭据未加入 Git；用户已有界面/主题/核心重构在验证后衔接保留。

## 改动文件与目的

| 文件或目录 | 修改目的 |
|---|---|
| Core/Connectivity/ConnectivityTester.cs、IConnectivityEnvironment.cs、IConnectivityTester.cs | 分离外部探针，修复联网判断并支持完整恢复验证 |
| Core/Monitoring/MonitoringEngine.cs、RecoveryPlanner.cs、EngineEvents.cs | 分级恢复、有限时检查、停止取消、动态周期、候选退避及故障类型门控 |
| Core/Models/MonitorPolicy.cs、NetworkState.cs | 规范探针配置，增加设备故障分类 |
| Core/SystemCommands/ProcessCommandRunner.cs | 系统命令统一超时、取消与子进程结束 |
| Core/Wifi/NetshWifiClient.cs、WifiClient.cs、WifiDeviceInspector.cs | 复用命令执行，读取当前存在的实体无线设备故障 |
| Core/Network/NetworkConfigurationService.cs | 移除重复执行器和无效条件分支 |
| Core/Recovery/HighRiskRecoveryService.cs、RecoveryBudget.cs | 取消后恢复收尾、设备实例重启、持久化预算和整机重启资格保护 |
| Core/Runtime/GuardianRuntime.cs | 共用路径、跨会话独占租约、原子心跳状态 |
| Core/Settings/AppSettings.cs、SettingsStore.cs | 配置热加载/备份容错、共享路径、可选整机重启配置 |
| Core/Logging/FileLogSink.cs、EventJournal.cs、Localization/Localizer.cs | 持久日志与事件、共享目录、准确描述后台与设备状态 |
| App/App.xaml.cs、MainWindow.xaml.cs | 独立后台展示与互斥、手动操作独占、动态策略、等待恢复收尾再退出 |
| App/App.xaml、MainWindow.xaml、AddWifiDialog.cs、Themes、csproj | 保留并衔接原有界面重构所需资源与依赖，已编译及对比度验证 |
| NetGuardian.Guardian/csproj、Program.cs、NetGuardian.sln | 无交互后台、配置轮询、心跳、停止信号、最后兜底与启动前校验 |
| tests/NetGuardian.Tests | 整合原有 xUnit 迁移，增加场景与安全回归，删除过时控制台入口 |
| tests/Test-GuardianScripts.ps1 | PowerShell 5 下模拟监督、无 SDK 启动与安全卸载 |
| scripts/build.ps1、publish.ps1、initialize-build.ps1、resolve-dotnet.ps1 | 简化工具链初始化、正确传播失败、测试后发布两个程序 |
| scripts/start-netguardian.ps1 | 发布版优先、安装版启动、不依赖 SDK、不更改配置目录 |
| scripts/register-netguardian-task.ps1、watch-netguardian.ps1、unregister-netguardian-task.ps1 | 安装受保护目录、开机任务、心跳监督、保留共享配置及安全停止 |
| scripts/check-contrast.py | 保留原有主题回归工具，并验证十组颜色对比 |
| README.md、docs、.gitignore | 当前使用说明、验证与历史归档，移除重复忽略规则 |

代码提交：`3a2f9f5`（监控核心）、`33097a3`（重启保护）、`b7c06e5`（后台和部署）。文档归档与验证记录另作独立提交。

## 尚未执行的现场验收

本轮没有安装/启用真实 SYSTEM 守护，也未切换实际 Wi-Fi、重启网卡或电脑。因此，尚不能宣称真实无人登录、断网和设备故障均已现场验收通过。

启用后应在有本地回退方式的维护窗口逐项验收：A 保持 SSID 但上游失效、B 可用；A 消失；B 也无公网；两个网络恢复其一；进程崩溃/假死；系统重启且无人登录；远程软件换网后自动重连。整机重启默认关闭；现有用户配置也未开启高风险恢复，须明确选择启用后才能自动重启网卡。
