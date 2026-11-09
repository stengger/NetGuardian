# 工作审视报告：网络操作与后台协作

日期：2026-11-09。本轮基于用户截图、相关事件处理代码、后台状态机、自动化测试及独立进程验证检查用户操作流程。

## 原定目标

后台运行时能够从应用手动切换 Wi-Fi，同时检查相邻网络操作是否存在按钮可点但被拦截、重复操作、等待不透明和结果反馈不准确等同类问题。验收要求是共享控制权可安全交接、结果能区分关联与联网、等待和取消状态明确、现有测试不回退，并按仓库规则更新实际本机安装。

## 完成情况

- [x] 后台临时交权、恢复收尾和自动恢复已实现并提交。
- [x] 连接、凭据设置、DNS、DHCP 和网络维护接入统一入口。
- [x] 等待实际连接及联网状态；恢复列表和周围已保存网络都能发起连接。
- [x] 冲突按钮、重复添加、缺少凭据的引导、页头托盘状态及扫描准备阶段取消已修正。
- [x] 单元测试、完整 Debug 回归及跨进程独占验证通过。
- [x] 从提交 `4db35be` 隔离发布，完成本机安装、启动和真实后台交权核验，见[部署记录](2026-11-09-workflow-deployment.md)。

## 发现的问题

| 严重程度 | 具体问题与证据 | 根本原因 | 已实施的改进 |
| --- | --- | --- | --- |
| 必须改正 | `RunExclusiveNetworkActionAsync` 遇到后台锁直接返回，影响连接、凭据设置、DNS、DHCP 和网络维护。 | 只验证防并发约束，未验证用户请求如何完成。 | 使用临时请求、后台收尾与同一独占锁交接，不停用或重建后台任务。 |
| 必须改正 | `Connect_Click` 只根据连接请求是否接受报告“命令已发送”。 | 用底层请求结果代替用户关心的最终状态。 | 等待真实目标 SSID、地址准备和公网检查；区分已连接可上网、已连接但公网失败、无法确认连接。 |
| 应当改正 | `UpdateWifiSelectionUi` 仅允许恢复列表连接，周围列表中的已保存网络不能直接连接，且仍可重复添加。 | 两个入口分别判断，未共享同一套选择状态。 | 统一 `WifiActionAvailability`，共享连接目标、添加和编辑可用性；精确区分 SSID 大小写。 |
| 应当改正 | 未保存 Windows 凭据的网络仍能点击连接。 | 可操作状态仅依据是否选中行。 | 连接按钮按凭据状态启用；禁用时显示设置、导入或扫描同步的说明，并提供辅助功能帮助文本。 |
| 应当改正 | 多个修改入口没有统一忙碌状态，另一页面仍可能发起冲突操作。 | 只处理单按钮防重复，未检查跨页面状态。 | 统一操作状态及独占请求，禁用冲突的编辑、连接、监控和检测入口；读取网卡信息不能重新启用忙碌按钮。 |
| 应当改正 | `ScanWifiAsync` 在进入扫描会话前等待配置同步，期间无进度且不能取消。 | 新增准备步骤时没有将其纳入原操作的生命周期。 | 准备与扫描使用同一会话和取消流程；取消准备后不继续扫描。共享的启动导入可继续完成，不发布已取消扫描的周围结果。 |
| 应当改正 | 后台交权期间仍可能显示“后台监控中”，旧心跳报告可能覆盖手动结果。 | 状态展示未覆盖新增控制权状态。 | 页头及托盘显示手动操作或后台暂停，暂停期间不套用旧报告。 |
| 应当改正 | 手动操作后立即重新检测，可能在网络稳定或用户认证前再次自动切换。 | 只验证后台恢复运行，未验证恢复时机。 | 自动检查和地址变化事件都尊重 30 秒稳定期；普通启动及手动检测行为保持原语义。 |

同时检查了 DNS 和 DHCP 的目标核验、确认与读取失败反馈，策略保存事务，检测、网站检查、IP 查询、质量检查和测速的运行及取消状态。这些已有流程保留；本轮没有对所有页面做完整人工验收。

## 修改文件及原因

后台协议文件、状态模型、监控稳定期及测试见[后台交接说明](2026-11-09-manual-network-handoff.md)。界面部分如下：

| 文件 | 原因 |
| --- | --- |
| `MainWindow.NetworkActions.cs` | 统一等待、交权、独占、结果异常提示和恢复流程，关闭界面时取消等待。 |
| `MainWindow.xaml.cs` | 接入真实连接验证、附近已保存网络连接、统一按钮状态、凭据引导及辅助功能文本，使用稳定期恢复前台监控。 |
| `MainWindow.NetworkInfo.cs` | 网卡信息刷新不能重新启用被手动操作禁用的续租按钮。 |
| `MainWindow.WifiImport.cs`、`MainWindow.WifiScan.cs` | 将配置同步放进可显示进度和取消的扫描生命周期。 |
| `Presentation/WifiActionAvailability.cs` | 两个无线列表使用同一套连接、添加及编辑可用性规则。 |
| `Presentation/MonitorStatusPresentation.cs` | 页头和托盘统一展示后台暂停及手动操作状态。 |
| `Core/Wifi/ManualWifiConnector.cs` | 等待真实连接与联网检查，处理系统拒绝、地址过渡、目标变化、超时及取消。 |
| `Core/Localization/Localizer.cs` | 中英文等待、升级、失败、连接及凭据提示，移除与实际布局不符的“右侧”说明。 |
| `WifiActionAvailabilityTests.cs`、`MonitorStatusPresentationTests.cs`、`WifiScanSessionTests.cs` | 验证附近网络选择、忙碌状态、缺少凭据、后台暂停及准备阶段取消。 |
| `ManualWifiConnectorTests.cs` | 验证真实连接语义、Windows 配置名、地址准备、无公网、目标变化和取消收尾。 |
| `README.md`、本报告 | 记录实际使用流程、修改原因、验证与边界。 |

## 验证

使用仓库 SDK `.dotnet-home/sdk/dotnet.exe` 8.0.426，`DOTNET_CLI_HOME` 指向仓库 `.dotnet-home`。完整输出在忽略目录 `.dotnet-home/validation/manual-network/`。

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `dotnet build NetGuardian.sln --no-restore -v minimal` | 退出码 0，0 错误；保留原有 2 条 WFAC010 高 DPI 清单警告。 | `final-build-reviewed.log` |
| `dotnet test NetGuardian.sln --no-build --no-restore --logger 'console;verbosity=minimal'` | 正常桌面权限下 Core 278、App 164，合计 442 项通过；0 失败、0 跳过，退出码 0。 | `solution-final.log` |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~WifiActionAvailabilityTests\|FullyQualifiedName~MonitorStatusPresentationTests\|FullyQualifiedName~WifiScanSessionTests\|FullyQualifiedName~NetworkOperationMessageTests' --logger 'console;verbosity=minimal'` | 末轮按钮与帮助文本检查后 12 项相关测试通过，退出码 0。 | `focused-app-reviewed.log` |
| `tests/Test-GuardianScripts.ps1` | 监督、启动、安全卸载的替身测试通过，退出码 0。 | `guardian-scripts.log` |
| `dotnet exec .dotnet-home/validation/manual-network/ProcessHandoff.dll` | 两个真实独立进程验证请求、交权、独占、撤销及恢复通过，退出码 0；没有执行网络命令。 | `process-handoff.log` |
| `git diff --check` | 无空白错误，退出码 0。 | 工具输出 |
| 隔离源码的 `scripts/publish.ps1 -OutputDirectory …/package` | 完整 Release 回归 442 项通过、0 跳过；界面与后台发布成功，退出码 0。 | `isolated-release-publish.log` |
| `dotnet exec …/live/LiveHandoff.dll` | 已安装 Core 与真实后台交权、独占、拒绝并发请求、持续心跳、撤销及稳定期恢复全部通过，退出码 0；没有执行网络命令。 | `live-handoff.log`、`live-handoff-exit.json` |
| 本机部署及启动核验 | 两处目录各 27 个文件哈希一致；界面响应、后台健康、协议版本 1；原策略及 57 条网络配置保留，配置字节一致。 | `deployment-result.json`、`installed-verification.json` |

本轮没有测试失败。最初只读查看正在运行的管理员进程时，受限权限无法读取其路径、命令退出码 1；没有据此停止任何进程。通过管理员只读脚本核实后确认界面与后台都位于 `C:\Program Files\NetGuardian`，心跳健康。该权限检查不属于构建或运行时故障。

## 做得好的地方

- 原独占控制锁和停止后等待恢复收尾的约束得以保留，交权期间心跳不中断，监督脚本无需放宽故障判断。
- 优先复用已有确认、目标复核、原子保存和报告选择逻辑，未直接绕过后台锁执行网络命令。
- 测试同时检查成功、失败、取消、过期、重试和旧后台兼容，另用独立进程验证文件锁语义。

## 下次重点关注

发布时必须同时匹配界面、Core 与后台组件，并验证实际启动路径；以后新增网络修改入口应复用统一操作流程，准备、等待和取消步骤都纳入用户可见的状态。更广泛的人工验收可覆盖真实校园网认证、信号消失及远程连接中断后的体验，本轮不以模拟测试代替这些实机结论。
