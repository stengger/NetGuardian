# 主窗口工作区边界验证

## 范围与实现

接续 `527ff68` 的短窗口内容修复，处理固定 1040×660 DIP 最小尺寸在较小工作区中无法收回的问题。正常工作区仍使用原最小尺寸，不更改字号、默认尺寸及 DPI 清单。

- `Presentation/WindowWorkArea.cs`：自有主窗口加载、重新显示、状态恢复、系统工作区或 DPI 消息、移动结束后合并检查。当前监视器工作区和窗口边界均使用物理像素，避免对混合缩放多屏的绝对坐标直接做 DPI 除法。最小尺寸按当前工作区及实际 DPI 换算，并可恢复原最小尺寸；正常窗口超界时夹紧尺寸及位置，最大化和最小化不主动 SetWindowPos。
- `MainWindow.xaml.cs`：初始化策略并在资源清理时释放。释放取消排队操作、移除 HWND hook 并解绑所有窗口事件；不增加静态系统事件订阅。
- `WindowWorkAreaTests.cs`：8 项用例覆盖负坐标、工作区顶部偏移、保持有效原位置、自有 HWND 的实际边界、恢复最小尺寸、无效工作区、显示前接线、隐藏重显、工作区消息及排队时释放。测试注入工作区，只操作自己的离屏窗口，不操作用户实际 NetGuardian 窗口。
- `PanelNavigationTests.cs`：增加加载归属前置断言，帮助诊断一次未定位的模拟空格激活失败；没有删除或跳过选择、焦点断言。

## 红绿回归与审查

日志均位于 ignored 目录 `.dotnet-home/validation/product-ui/`，命令完整输出保存在对应文件。

1. 先以无行为接口运行新增测试：`window-workarea-red.log`，4 失败、2 通过，退出 1；错误为未收回的矩形及 MinWidth 仍是 1040，而非预期 960。实现后 `window-workarea-green.log`，6/6，退出 0。
2. 加入自有 HWND 的加载和系统消息序列后，首次全量 Core 206/206、WPF 132/132，退出 0：`window-workarea-all-tests.log`。
3. 独立审查发现跨 DPI 拖动期间可能提前 SetWindowPos。新增进入移动、工作区变化、退出移动的真实自有 HWND 消息回归，`window-workarea-drag-red.log`，1 失败、7 通过，退出 1。随后跟踪 WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE，交互期间跳过位置调整，退出后排队；独立复查未发现新增明确阻塞。
4. 修复后全量 `window-workarea-final-tests.log` 出现一次导航 Space 失败：Core 206 通过，WPF 132 通过、1 失败，退出 1。完整错误与堆栈在日志，摘要：`PanelNavigationTests.键盘激活现有面板更新导航选择(key: Space)` 第 52 行 `Assert.Same`，期望 SettingsNavButton、实际 null。不得据此断言生产导航已故障或已修复。
5. 新窗口用例补齐现有 WPF 桌面集合标记，导航测试补前置归属断言。联合诊断 `window-workarea-navigation-diagnostic.log`，16/16，退出 0；再次全量 `window-workarea-verified-tests.log`，Core 206/206、WPF 133/133，共 339，无跳过，退出 0。

导航偶发失败仍无现场根因。独立审查核对 [.NET 8 ButtonBase 源码](https://raw.githubusercontent.com/dotnet/wpf/v8.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/ButtonBase.cs)，Space 激活依赖实时修饰键、鼠标按键、捕获与按压状态，Enter 不完全相同；这只能解释可能性，不能作为本次失败结论。WPF 集合本来已禁用与其他集合并发，不能把漏标记直接归因为并发。后续若再现，应补按下前后输入状态证据，不更改断言来掩盖失败。

## 命令与结果

公共参数：`--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。SDK 8.0.422，先运行 `scripts/initialize-build.ps1`。

- `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --configuration Release --filter FullyQualifiedName~WindowWorkAreaTests` 加公共参数：见上述红绿日志。
- `dotnet test NetGuardian.sln --configuration Release` 加公共参数：最终 339/339，退出 0。
- `dotnet build NetGuardian.sln --configuration Release --no-incremental` 加公共参数：退出 0，2 条既有 WFAC010、0 错误，见 `window-workarea-build.log`。保留 WPF 入口依赖的 PerMonitorV2 清单，没有屏蔽分析器。

## 未完成与验收边界

生产窗口原始 MinWidth / MinHeight 仍为 1040 / 660；仅在工作区不足时动态降低。不能把本次注入工作区、物理边界测试描述成真实多显示器或 200% 缩放验收。前轮部分页面的 960×500 内容测试不等于所有页面都已在任意小工作区可达。

实际跨屏 DPI、贴靠、最大化及断接屏幕、讲述人、系统主题消息链和各页高缩放内容仍待验收；同 DPI 的非交互跨屏移动也应在后续实机验收中覆盖。整体产品目标未完成。本轮源代码提交和已安装版本应分别记录，不把构建结果当成已部署。
