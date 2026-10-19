# Wi-Fi 扫描生命周期验证

## 修改与理由

- `MainWindow.WifiScan.cs`、`WifiScanSession.cs`：主页面及快捷工具共用同步防重会话，明确扫描中、取消中、成功、取消和失败状态；开始清除旧发现结果，取消后丢弃迟到结果，退出取消且不更新关闭窗口。
- `MainWindow.xaml`、`MainWindow.xaml.cs`、`Localizer.cs`：两个入口同步禁用与取消按钮，显示活动进度、限高滚动错误、结果数、空结果说明及中英文重试建议；扫描控制从主文件拆至 partial，移除旧入口独立忙状态。
- `NetshWifiClient.cs`：非零退出码抛出含退出码和原始输出的失败，避免错误解析成成功的空列表。原生扫描正常回退机制保留。
- `WifiScanSessionTests.cs`、`WifiScanFailureTests.cs`：覆盖双入口、取消、迟到成功、失败清旧结果及重试、调用开始通知、命令失败与合法空扫描。`DesktopLayoutTests.cs` 补中英文长错误布局。

## 验证结果

沿用项目 SDK、工作区 TEMP 与初始化脚本；公共参数为 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln` 加公共参数 | Core 203/203，WPF 51/51，无跳过 | 0 |
| 增加长错误布局并修正后 `dotnet test tests/NetGuardian.App.Tests` 加公共参数 | WPF 53/53，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |

测试红灯：

- 扫描会话测试实现前报 CS0246（WifiScanSession）、CS0103（WifiScanPhase），退出 1；完整编译输出保留 `.dotnet-home/validation/product-ui/red-wifi-scan.log`。
- 命令失败回归实现前：`Assert.Throws() Failure: No exception was thrown; Expected: typeof(System.InvalidOperationException)`，`WifiScanFailureTests.cs:13`；1 失败、1 通过，退出 1。完整输出保留 `red-wifi-command.log`。
- 新长错误布局最初两语言失败：`扫描错误后表格仅 44 DIP`，`DesktopLayoutTests.cs:32`，调用链经 `DesktopFixture.Run`；2 失败、51 通过，退出 1。状态最初位于列表内部，占用紧凑窗口卡片空间，移到列表上方限高 48 DIP 后通过，未降低断言或跳过测试。

最终完整日志位于忽略目录 `wifi-scan-tests.log`、`wifi-scan-app.log`、`wifi-scan-build.log`。已检查 `wifi-scan-failed-en-US.png`：长错误可滚动且保留两个列表区域；其余标签为夹具预设，不能作为完整英文界面验收。独立只读审查未发现新增明确 P1/P2。

没有执行真实 Wi-Fi 扫描、切网或后台变更；操作系统位置权限、WLAN 服务和真实取消耗时仍需正式进程验收。其他诊断工具生命周期、流量统计范围和部署继续属于完整产品目标。
