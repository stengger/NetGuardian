# Wi-Fi 连接失败与当前连接展示：调查及验证记录

本轮需求：定位用户报告的手动切换失败，并在 Wi-Fi 管理页独立显示当前实际连接。用户已确认实现方案。

## 已确认的原因

本机 WLAN 自动配置日志在 2026-11-09 17:21:32（北京时间）记录事件 8002，目标网络连接失败，原因码 `163851`，原文为“特定的网络不可用。”；17:21:35 的事件 8001 记录系统自动连回原网络。请求已经送达 Windows，不能把这次结果归因为后台拒绝交权，也没有证据将其归因为密码错误。

原实现只确认 `WlanConnect` 接受请求，再轮询目标 SSID，无法显示上述系统最终失败原因。这解释了界面上的笼统超时提示。代码可以改善结果处理和展示；无法据此承诺不可用的无线网络已经可以连接。

## 修改范围

| 文件或模块 | 目的 |
| --- | --- |
| `Core/Wifi/NativeWifiScanner.Connection.cs`、`WifiConnectionAttempt.cs` | 关联接口及配置的连接完成/失败通知，翻译原因码；超时或取消后中止未完成请求，并确认接口收尾，未知状态继续持锁等待。 |
| `INativeWifiConnector.cs`、`WifiClient.cs` | 区分不能发起原生请求与已发生的明确连接失败；收到明确失败或跟踪异常后，不重复发送 netsh 连接。 |
| `WifiConnectionInfo.cs`、`IWifiClient.cs`、`NetshWifiClient.cs` | 读取实际连接、信号、安全与对应无线网卡的 IPv4，区分查询失败和未连接，支持中英文及多接口。 |
| `App/MainWindow.CurrentWifi.cs`、`Presentation/CurrentWifiPresentation.cs` | 页面进入、周期和网络操作结束后刷新；查询在途时跳过周期请求，显式刷新可丢弃旧快照，错误不能保留旧 SSID。 |
| `MainWindow.xaml`、`Presentation/ResponsiveLayout.cs` | 添加独立当前连接卡片；短窗口操作栏横向滚动，普通窗口自动换行，为中英文列表保留完整数据行。 |
| `MainWindow.NetworkActions.cs`、`MainWindow.xaml.cs` | 明确失败提示、操作结束刷新、退出时先取消并等待手动操作收尾，退出过程中拒绝新操作。 |
| `Core/Localization/Localizer.cs` | 中英文当前连接、读取失败、地址准备及具体失败提示。 |
| 新增及更新的 Core/App 测试 | 覆盖通知过滤、明确失败、取消/超时等待收尾、快照解析、错误展示、独立卡片及小窗口数据行。 |

## 已完成验证

使用仓库 SDK `.dotnet-home/sdk/dotnet.exe` 8.0.426，`DOTNET_CLI_HOME` 指向仓库 `.dotnet-home`。完整输出保存在本机忽略目录 `.dotnet-home/validation/wifi-connection-followup/`。

| 命令或检查 | 结果 |
| --- | --- |
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --logger 'console;verbosity=minimal'` | 当前 Core 完整回归 289 项通过、0 失败、0 跳过，退出码 0；`core-final.log`。 |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~TrayContextMenuTests' --logger 'console;verbosity=minimal'` | 正常桌面权限下 5 项通过，退出码 0；`tray-desktop-context.log`。受限桌面环境下首次完整回归及随后一次检查的真实弹出菜单测试失败，保留原断言后在正常桌面权限下验证通过。 |
| 修复后的 Core 进行本机只读查询 | 退出码 0，实际读取当前 SSID、信号约 90%、安全类型与 IPv4；原因码翻译为“特定的网络不可用。”；`read-current.log`。未切网或修改网络配置。 |
| 只读代码复核 | 修正连接超时提前交锁、收尾期间误断原连接、迟到断开通知及周期刷新丢弃错误结果的边界。复核未运行测试，测试结果单独记录。 |

初次测试先验证了回归用例能够失败：Core 明确失败被误回退为成功，App 缺少当前连接区域。该预期失败退出码 1，完整断言及堆栈保存在 `regression-red.log`。

## 英文短窗口布局：失败记录与修复

下列相关检查曾连续三次退出码 1。已依用户 `AGENTS.md` 第 6 条停止调整，用户随后明确授权“继续诊断并修复布局”。没有跳过、删除或降低原有测试断言。

| 检查 | 当次结果及完整输出 |
| --- | --- |
| `dotnet test NetGuardian.sln --no-restore --logger 'console;verbosity=minimal'` | Core 287 项通过；App 165 项通过、3 项失败。中英文短窗口数据行与受限桌面托盘焦点检查失败；[完整错误及堆栈](../.dotnet-home/validation/wifi-connection-followup/solution-debug.log)。 |
| `dotnet test NetGuardian.sln --no-restore --filter 'FullyQualifiedName~NativeWifiConnectionTests\|FullyQualifiedName~WifiConnectionAttemptTests\|FullyQualifiedName~CurrentWifi\|FullyQualifiedName~SmallWorkAreaTests\|FullyQualifiedName~TrayContextMenuTests' --logger 'console;verbosity=minimal'` | Core 17 项通过；App 20 项通过、3 项失败。卡片改为标题与 SSID 同行后，中英文短窗口数据行仍失败，托盘焦点仍受受限环境影响；[完整错误及堆栈](../.dotnet-home/validation/wifi-connection-followup/review-fixes.log)。 |
| `dotnet test NetGuardian.sln --no-restore --filter 'FullyQualifiedName~WifiConnectionAttemptTests\|FullyQualifiedName~CurrentWifi\|FullyQualifiedName~SmallWorkAreaTests\|FullyQualifiedName~TrayContextMenuTests\|FullyQualifiedName~WindowClosingTests' --logger 'console;verbosity=minimal'` | 正常桌面权限下 Core 10 项通过；App 25 项通过、1 项失败。收起短窗口辅助说明、压缩间距后，中文及托盘已通过，英文 960×500 仍失败；[完整错误及堆栈](../.dotnet-home/validation/wifi-connection-followup/final-focused.log)。 |

失败原文为 `NearbyGrid 只剩表头`，断言要求列表实际高度至少 90，并继续验证第一行完整可见。获准继续后先记录几何尺寸：英文 960×500 下，操作栏两行高 78、连接卡片高 61，列表实际高仅 79。复现命令为 `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --filter 'FullyQualifiedName~SmallWorkAreaTests.短窗口无线列表至少可以显示一个完整结果行' --logger 'console;verbosity=minimal'`，退出码 1，[完整尺寸及错误](../.dotnet-home/validation/wifi-connection-followup/layout-measurements.log)。

修复为仅在短窗口使用单行横向滚动操作栏，保留按钮尺寸，普通窗口恢复自动换行。原有列表高度、完整行及最后列可滚动读取断言均保留；测试加入完整中英文当前连接内容，以及末端连接按钮可完整滚动显示、恢复大窗口后清除横向偏移的验证。新增操作栏测试先以原控件不具备滚动能力而失败，退出码 1，见 [预期失败日志](../.dotnet-home/validation/wifi-connection-followup/action-scroll-red.log)。首次相关检查 32 项通过，新增中文用例错误地要求原本可放下的按钮必须溢出，1 项失败、退出码 1，见 [完整错误](../.dotnet-home/validation/wifi-connection-followup/layout-green.log)；修正该新用例的前提后，中英文仍都验证末端按钮完整可达。

最终完整 Debug 回归：`dotnet test NetGuardian.sln --no-restore --logger 'console;verbosity=minimal'`，正常桌面权限下 Core **289/289**、App **170/170**，0 失败、0 跳过，退出码 **0**，见 [完整输出](../.dotnet-home/validation/wifi-connection-followup/solution-debug-final.log)。`git diff --check` 通过。原有 WFAC010 高 DPI 警告未作无关调整。

## 发布与本机验收

代码提交：`6e4180f336dec49783c609b3db3a3b8b430189a9`，提交信息“完善无线连接失败反馈与当前连接展示并修复短窗口布局”。

以 `git archive` 导出该提交到忽略目录，设置仓库 SDK 路径及 `SourceRevisionId`，执行隔离源码的 `scripts/publish.ps1 -OutputDirectory <隔离发布目录>/package`。脚本完成还原、完整 Release 测试和界面/后台发布：Core **289/289**、App **170/170**，0 失败、0 跳过，退出码 **0**；[完整发布输出](../.dotnet-home/validation/wifi-connection-followup/isolated-release-publish.log)。构建标识为 `1.0.0+6e4180f336dec49783c609b3db3a3b8b430189a9`。

部署及验收脚本通过实际 Windows PowerShell 5 解析，按系统正常管理员确认运行。更新前界面已退出，无自动化关闭界面操作；核实后台及计划任务归属，备份差异文件、任务及配置后，发送正常停止请求，更新以下两个目录并恢复原任务状态：

- 本机：`C:\Program Files\NetGuardian`
- 项目发布目录：`C:\Users\Qiu\Documents\Net\dist\NetGuardian`

2026-11-09 19:01（北京时间）部署成功、退出码 **0**：共更新 20 个差异文件，每个目录核验 25 个文件哈希，设置字节一致。备份位于本机忽略目录 `.dotnet-home/validation/wifi-connection-followup/deployment-backup-20261109-190104`，详见 [部署结果](../.dotnet-home/validation/wifi-connection-followup/deployment-result.json)。

19:02 启动验收成功、退出码 **0**：GUI 来自安装目录并可响应，界面/Core/后台四个主要程序集的版本及哈希一致；后台来自安装目录，状态 `Healthy`、失败计数 0、心跳正常，原有 57 个网络及恢复策略均保留，配置文件字节一致，无新增崩溃日志。详见 [启动验收结果](../.dotnet-home/validation/wifi-connection-followup/installed-verification.json)。

使用实际安装的 `NetGuardian.Core.dll` 再次执行只读查询，退出码 **0**：当前连接、SSID、信号 89%、WPA2、CCMP 与 IPv4 均读取成功，原因码 163851 翻译正确，见 [安装版只读查询输出](../.dotnet-home/validation/wifi-connection-followup/installed-read/read-current.log)。

桌面工具已确认实际安装路径的主窗口显示正常；管理员界面没有响应自动化导航，已请用户手动打开 Wi-Fi 管理页以继续核验当前连接卡片。当前记录不将该页面的实机视觉验收列为已完成。未实际执行切网，目标网络的物理可用性仍需以 Windows 的最新结果为准。
