# 自适应网络监控验证记录

## 改动与原因

- `Core/Monitoring/MonitoringEngine.cs`：统一事件、定时和手动检测的互斥；健康定时使用轻量探测，异常最多 10 秒复核，恢复验证结果决定下一周期；事件从首次通知合并 1 秒，检查中最多追加一次，停止后旧回调失效。
- `Core/Monitoring/NetworkChangeSource.cs`：封装系统网络可用性、地址变化通知和订阅清理；订阅失败保留定时兜底。
- `Core/Connectivity/ConnectivityTester.cs`、`IConnectivityTester.cs`：轻量探测仅读取接口快照及执行 HTTPS HEAD；沿用既有成功门槛，不调用独立 DNS、网关探针和系统命令。轻量异常/超时转完整诊断，时间预算独立，仍受总期限约束。
- `Core/Models/MonitorPolicy.cs`、`ConnectivityReport.cs`：新配置正常间隔 90 秒，旧显式值保留；报告携带轻量标记，旧报告默认完整。
- `Core/Localization/Localizer.cs`、`App/Presentation/ConnectivityReportPresentation.cs`、`App/MainWindow.xaml.cs`：区分检测类型、来源和时间；只展示本轮探针；解释自适应周期；接入系统事件。
- `Guardian/Program.cs`：接入同一系统事件来源，日志记录轻量或完整诊断，心跳继续携带实际下一次检测时间。
- `tests/NetGuardian.Tests/ConnectivityTests.cs`、`AdaptiveMonitoringTests.cs`：轻量探测门槛、取消、接口缺地址、周期转换、事件合并、检查中通知、停止重启、订阅失败、独立超时预算、恢复成功后调度、报告序列化兼容。
- `tests/NetGuardian.App.Tests/ConnectivityReportSelectionTests.cs`、`PolicyEditFeedbackTests.cs`：检测类型来源展示、旧探针不混入新结果，以及旧 30 秒和新 90 秒策略保存失败时均保持原值。
- `README.md` 与设计、计划：说明周期、事件和延迟边界。

## 命令与结果

统一使用 SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe`（实际查询为 8.0.422），通过 `NETGUARDIAN_DOTNET` 和 `scripts/initialize-build.ps1` 设置仓库现有环境。

| 命令（dotnet 指上述 SDK） | 结果 | 退出码 |
|---|---|---|
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --filter FullyQualifiedName~ConnectivityTests --verbosity minimal -p:UseSharedCompilation=false` | 先因缺少新入口 CS1061 失败；实现后 27/27 | 1 → 0 |
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --filter FullyQualifiedName~AdaptiveMonitoringTests --verbosity minimal -p:UseSharedCompilation=false` | 先缺少事件来源 CS0246；实现后 6/6；追加超时回归先失败再修复，最终 8/8 | 1 → 0 |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --filter FullyQualifiedName~ConnectivityReportSelectionTests --verbosity minimal -p:UseSharedCompilation=false -m:1` | 先缺展示入口 CS0103，随后 4/4 | 1 → 0 |
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --verbosity minimal -p:UseSharedCompilation=false` | 沙箱内 11 失败/206 通过；放宽沙箱后 217/217 | 1 → 0 |
| `dotnet test NetGuardian.sln --no-restore --verbosity minimal -p:UseSharedCompilation=false -m:1` | 首轮 Core217/217，WPF147/148；修正用例后 Core219/219、WPF149/149，均无跳过 | 1 → 0 |
| 同上，独立复核修复后完整重跑 | Core222/222、WPF149/149，合计371项、无跳过 | 0 |
| `dotnet build NetGuardian.sln --no-restore --verbosity minimal -p:UseSharedCompilation=false -m:1` | 0 错误、2 个 NU1900 警告 | 0 |

首次未指定单节点的基线全量测试没有输出，主动中断，退出 1；不将此视为测试通过，也未据此断定 SDK 不可用。后续使用单节点运行完整套件。

## 失败详情与完整输出

完整命令输出保留在工作区忽略目录 `.dotnet-home/`，未提交日志或构建产物：

- `adaptive-probes-red.log`：三处 CS1061，`ConnectivityTester` 未包含 `RunLightweightCheckAsync`。
- `adaptive-engine-red.log`：两处 CS0246，缺少 `INetworkChangeSource`。
- `adaptive-ui-red.log`：CS0103，不存在 `ConnectivityReportPresentation`。
- `adaptive-timeout-red.log`：`轻量超时仍执行完整诊断而不直接累计失败`，Assert.True，Expected True / Actual False；修复后 `adaptive-timeout-green.log` 8/8。
- `adaptive-core.log`：11 个失败的完整堆栈及退出 1。多数为 `System.UnauthorizedAccessException : Access to the path is denied.`，在 `FileSystem.MoveFile`、`SettingsStore.SaveCore` 或 `GuardianStatusStore.Write` 发生；两个 RecoveryBudget 用例在持久化失败后 Assert.True 为 False。未修改这些测试或存储实现。同命令放宽沙箱后的 `adaptive-core-unsandboxed.log` 217/217，退出 0。
- `adaptive-solution.log`：`保存失败不能把未保存策略留给正在运行的监控`，Assert.Equal，Expected 30 / Actual 90，`PolicyEditFeedbackTests.cs:22`；已改为显式 30/90 参数化覆盖，保留原行为断言。
- `adaptive-solution-final.log`：完整套件通过记录；测试编译中包含 NU1900（无法加载 `https://api.nuget.org/v3/index.json` 的漏洞元数据）及 WFAC010（现有 manifest 高 DPI 配置建议），未禁用检查。
- `adaptive-build.log`：解决方案构建通过记录，2 个 NU1900、0 错误。

## 独立复核修正

复核发现一次性定时回调早于截止时间时可能直接返回、不再检测，现已按剩余时间重新安排；设置截止时间在启动定时器之前。健康状态和失败计数提交在生命周期锁内核对取消令牌及代次，恢复验证也核对，防止停止重启后旧检查污染状态。完整结果已记录后恢复阶段超时或异常不重复增加失败次数。

`adaptive-review-red.log` 保存新增回归的完整输出（退出 1）：`单次定时回调提前到达也必须继续安排检测`，Expected 1 / Actual 0；`完整失败后恢复总超时本轮不得重复累计`，Expected 1 / Actual 2。修复后 `adaptive-review-green.log` 自适应回归 11/11、退出 0。另覆盖了活跃检查停止重启后释放旧结果、新一轮仍完整诊断。

独立复核再次检查修复，未发现新增阻断问题；没有独立重跑测试。停止重启测试覆盖探针等待阶段，没有精确暂停于结果返回后进入提交锁之前的极小竞态窗口，锁内取消及代次校验由代码复核确认。

修复后的完整测试输出为 `.dotnet-home/adaptive-solution-reviewed.log`，Core222/222、WPF149/149，退出0；构建输出为 `.dotnet-home/adaptive-build-reviewed.log`。此前所有红测与失败日志保留，未跳过或删除测试。

修复后再次构建退出0，0错误、2个NU1900警告；`git diff --check` 退出0。没有另设格式化工具，沿用现有代码风格。

## 验收边界

测试使用可驱动时钟、模拟网络事件及探针，另运行现有 WPF 布局与交互套件。没有执行真实断网、网卡重启、守护启停、已安装文件替换或系统重启；不把自动化验证当作实际无人值守验收。CPU、内存与流量节省比例尚未实测。

正常周期保留现有配置；旧配置为 30 秒时仍为 30 秒，不会静默改成 90 秒。异常复核间隔还需加上本轮检测/恢复耗时。

轻量检查不读取实时 SSID，网络名称可能显示为空；其探针表只包含本轮实际检查内容。完整诊断及手动检测继续查询 SSID。

原有 `docs/2026-10-18-validation.md` 改动不属于本任务，保持未提交。
