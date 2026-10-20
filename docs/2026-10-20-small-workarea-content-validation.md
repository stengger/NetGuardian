# 小工作区内容布局回归

## 范围及明确未完成项

本轮继续完整产品目标的小工作区调查。生产最小窗口仍为 1040×660 DIP，没有降低它，也未接入按实际屏幕工作区调整窗口的代码。新增测试明确手动把夹具最小尺寸改为 960×500，以隔离内容布局，不冒充实际 200% DPI、跨屏消息链或正式业务窗口已适配。

发现并修复的实际内容问题：侧栏流量用叠加定位，短窗口时覆盖导航；Wi-Fi 纵向两表只能显示表头；辅助列在窄卡片中压至默认最小宽度，不能据存在数据行宣称信息可读。

## 实现

- `MainWindow.xaml`：流量区移入 NavigationView.FooterMenuItems，位于设置之前，由库布局预留空间，主菜单可以滚动。两种流量样式通过祖先 PanelNavigationView 的 IsPaneOpen 互斥显示，保留图标、数值、范围和单位。
- 同文件：8 个 Wi-Fi 辅助列增加与既有设计宽度一致的 MinWidth，不隐藏安全类型、凭据、失败次数或开关，窄表通过横向滚动读取。
- `ResponsiveLayout.cs`：表区域宽至少 600 DIP 且高度不足 280 DIP 时横排；较高窗口继续纵排，足够宽窗口仍横排。保留 SSID 180 DIP 下限和完整文本提示。
- `PanelNavigationView.cs`：根自动化树按实际菜单/页脚顺序保留九个选择项和流量 Group，没有重复代理树。
- `SmallWorkAreaTests.cs`：内容操作边界、导航与流量不相交、Group 唯一及顺序、实际首行完整落在 ScrollContentPresenter 视口、横向滚动后最后列可读、两种流量样式互斥、默认延迟后的异步滚动状态完成。
- `DesktopLayoutTests.cs`：STA 夹具显式安装 DispatcherSynchronizationContext 并在退出时恢复。库 DynamicScrollViewer 在 await Task.Delay 后直接修改依赖属性；原夹具没有 UI 同步上下文，续体进入线程池导致崩溃。没有禁用滚动、延迟或动画。依据：[WPF-UI 4.3.0 源码](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Controls/DynamicScrollViewer/DynamicScrollViewer.cs)。
- `PanelNavigationTests.cs`：集合现在含非导航 GroupBox，枚举改 OfType，并增加九项精确数量断言，保留原全部选择/可达性断言。

## 失败、诊断及修正记录

完整命令输出、错误与堆栈保留于 `.dotnet-home/validation/product-ui/`，没有删减断言或跳过失败用例。

| 日志 | 结果及退出码 |
|---|---|
| `small-workarea-diagnostic-tests.log` | 初始四个操作按钮边界用例通过，退出 0；截图仍暴露表头与叠加问题，未据此结束 |
| `red-small-workarea-layout.log` | 4 通过、2 失败；导航矩形 4,388,216,40 与流量 20,286,184,150 重叠；NearbyGrid 只剩表头；退出 1 |
| `small-workarea-layout-tests.log` | 初始移入 PaneFooter，6 项通过，退出 0；全量后继续纠错 |
| `small-workarea-release-tests.log` | Core 206 通过；WPF 106 通过、2 失败后主机崩溃，退出 1。流量单位 bottom=633 超出原 610 断言，原因 PaneFooter 位于设置之后；随后 DynamicScrollViewer.UpdateVerticalScrollingState 续体跨线程修改依赖属性，System.InvalidOperationException，经 Dispatcher.VerifyAccess/DependencyObject.SetCurrentValue/ThreadPool 堆栈抛出。没有把此错误归为测试框架不可用 |
| `small-workarea-footer-release-tests.log` | 改放设置之前并修复夹具上下文；Core 206 通过，WPF 120 通过、2 失败，退出 1。InvalidCastException：GroupBox 不能转换 PanelNavigationItem，保留完整堆栈；同步测试集合类型与数量要求后继续 |
| `small-workarea-final-tests.log` | Core 206 通过；WPF 121 通过、1 失败，退出 1；新增最后列滚动断言失败 |
| `small-workarea-scroll-diagnostic.log` | 退出 1；NearbyGrid 最后列内容宽 20，表宽 269，HorizontalOffset=0、ScrollableWidth=0；补足辅助列可读下限，而非放宽断言 |
| `small-workarea-column-minimum-tests.log` | 6/6 通过，退出 0 |
| `small-workarea-verified-tests.log` | 加实际视口及默认异步状态验证后，Core 206/206、WPF 123/123，无跳过，退出 0 |
| `red-sidebar-footer-visibility.log` | 截图发现重复箭头，新增展开/收起两项均失败；期望 Collapsed，实际 Visible，退出 1。移入 ItemsControl 后 ElementName 跨名称作用域失效，改为祖先相对绑定 |
| `small-workarea-final-visibility-tests.log` | 最终 Core 206/206、WPF 125/125，无跳过，退出 0；包含九个小工作区用例，原有位置及选择断言全部保留 |
| `small-workarea-final-build.log` | 完整 Release 重建成功，退出 0，2 条既有 WFAC010、0 错误 |

## 运行方式与验收边界

SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe`，运行 `scripts/initialize-build.ps1`，沿用工作区 TEMP/TMP。公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。全量命令 `dotnet test NetGuardian.sln --configuration Release`，专项命令将项目换为 `tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj` 并加 `--filter FullyQualifiedName~SmallWorkAreaTests`。构建命令 `dotnet build NetGuardian.sln --configuration Release --no-incremental`，均加公共参数。既有 WFAC010 未屏蔽。

截图由真实窗口 XAML 夹具和内存网络样本渲染。已检查导航/流量/设置分区和双表，不能当作正式业务进程截图。生产最小尺寸和初始窗口尺寸仍可能超过小工作区，后续必须完成窗口尺寸与位置策略及全页布局验证；本轮只先消除内容布局阻碍。当前安装仍为已校验的字体版本 5089dee，未替换。

最终独立只读复核无明确阻塞，确认列最小宽度、祖先绑定、UIA 顺序、实际视口及默认异步回归均保持要求；已检查最终侧栏截图，不再重复显示展开/收起的箭头。完整日志与图片保留上述忽略目录。

差异检查发现并清理一行仅含空格的新增空白，随后 `git diff --check` 针对改动源文件及测试退出 0；没有为通过测试禁用任何检查。
