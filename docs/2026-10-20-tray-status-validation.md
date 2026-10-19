# 托盘、即时检查与报告来源一致性验证

## 修改

- `MonitorStatusPresentation.cs`：增加统一 CanCheck；前台检查、手动检查与有效后台心跳共同决定页头和托盘可用性。
- `TrayMonitorPresenter.cs`：将同一状态实际应用到 NotifyIcon 与命名菜单项；后台接管时禁用界面开始/停止并解释归属，菜单随语言更新，原生托盘文字没有变化时不重复写入。
- `MainWindow.xaml.cs`：后台分支提前返回前更新托盘；立即检测同步置忙，防页头/托盘重复启动；手动只读路径传递取消令牌，退出取消，清理后不再发布结果。
- `ConnectivityReportSelection.cs`：按 CheckedAt 保留最新报告，同时间戳保留非后台结果，避免状态刷新用旧后台心跳覆盖刚完成的手动结果；普通重复心跳不重建探针列表，语言切换强制重绘当前选中报告。
- `MainWindow.xaml`、`Localizer.cs`：首页显示检查来源与时间，长 SSID 换行并提供完整提示；不把监控归属与结果来源混为一谈。
- `TrayMonitorPresenterTests.cs`、`ConnectivityReportSelectionTests.cs`、`DesktopLayoutTests.cs`：不可见实际 NotifyIcon 与菜单状态、中英切换、旧/同时间/新报告选择，以及 1040×660 首页来源文本与探针区域。

## 验证

沿用项目 SDK 与工作区 TEMP；公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln` 加公共参数 | Core 201/201，WPF 44/44，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |
| SSID 换行后 `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --filter FullyQualifiedName~首页显示` 加公共参数 | 首页布局 1/1 | 0 |

完整日志：忽略目录 `.dotnet-home/validation/product-ui/tray-all-tests.log`、`tray-build.log`、`tray-home-layout.log`。新增测试先因尚无 TrayMonitorPresenter/CanCheck 或报告选择类型报 CS0103/CS1061/CS0246，退出 1；之后实现并通过。提交前检查 `git status`、差异及 `git diff --check`。

独立审查发现：新增即时检查收尾刷新会在同一 UI 续体中用旧后台报告覆盖新结果。增加报告选择与来源后，补旧/同时间戳/更新报告回归；独立复核确认原 P2 修复，无新增明确 P1/P2。

已检查 `home-report-context-1040.png` 的模拟数据主窗口布局，来源/时间可读且保留探针区域。托盘单测的 NotifyIcon 未设置图标且不可见，不是实际系统托盘人工验收；未执行真实联网检查、后台变更或网络修改。仍需真实进程的托盘菜单、后台心跳与手动检查组合、退出时取消、跨 DPI 和读屏验收。日志与其他页面总目标继续有效。
