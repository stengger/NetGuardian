# 诊断工具完整运行状态验证

- `DiagnosticSession.cs`、`MainWindow.Diagnostics.cs`：站点检测、IP 查询、诊断报告各自防重并支持取消；令牌贯穿底层，返回后再次检查取消，退出取消且停止更新窗口。IP 和报告开始即清旧结果；失败不残留旧结果。
- `MainWindow.xaml.cs`：移除入口独立忙状态，两个站点检测入口共用会话；诊断报告复用注入的 tester，移除不属于本次报告的实时引擎连续失败次数。语言切换重绘当前会话结果。
- 站点表保留本窗口最近 20 条带时间历史，取消不新增记录，提供明确历史范围和 HTTP 耗时说明；DNS 和 HTTP 分列准确命名，列最小宽度及水平滚动保留字段可读性。切语言重绘历史结果状态，原始错误保留。
- IP 按实际获取时间显示并说明最多缓存 10 分钟，入口从刷新状态改为查询信息，避免误以为重复点击必然获取新数据。
- 三个标签改为使用剩余空间的结果表格，移除固定最小高度；长错误限高滚动；显示取消与活动进度。
- `Localizer.cs` 补齐双语状态、范围和列名；`DiagnosticSessionTests.cs` 覆盖重复、取消后迟到结果、失败清旧结果、取消异常和重试。`DesktopLayoutTests.cs` 覆盖三页中英文 1040×660、长错误和取消按钮，要求表格保留至少 150 DIP。

沿用项目 SDK 和工作区 TEMP；公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln` 加公共参数 | Core 203/203、WPF 66/66，无跳过 | 0 |
| 明确列名和最小宽度后 `dotnet test tests/NetGuardian.App.Tests` 加公共参数 | WPF 66/66，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |

完整日志位于忽略目录 `.dotnet-home/validation/product-ui/diagnostics-tests.log`、`diagnostics-app.log`、`diagnostics-build.log`。新增会话测试首次因尚无 DiagnosticSession/DiagnosticPhase 报 CS0246/CS0103，退出 1，完整输出保留 `red-diagnostic-session.log`。没有跳过或降低测试断言。

独立只读审查未发现新增明确 P1/P2。已检查英文站点布局截图，长错误可滚动，取消及预设可见，表格使用剩余空间。截图使用真实 XAML 和模拟状态，未执行真实网络查询、取消或修改后台；实际网络时序、跨 DPI/主题/完整键盘与读屏、安装发布仍未验收。完整产品目标继续有效。
