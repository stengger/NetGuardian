# 添加 Wi-Fi 弹窗边角修复

## 原因与范围

用户截图中的添加 Wi-Fi 弹窗使用普通 WPF Window 和 WindowStyle.None，没有接入主窗口的 Fluent 外壳。`AddWifiDialog.cs` 改为 FluentWindow，启用内容延伸至标题栏、Round 圆角偏好，保持无背景特效及非透明窗口。现有表单、保存、取消和自定义标题栏接线保留，不新增 UI 框架。

对照 [WPF-UI 4.3.0 FluentWindow 实现](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Controls/FluentWindow/FluentWindow.cs)：框架在 HWND 初始化时应用 DWM 偏好并建立 WindowChrome，CaptionHeight=0、UseAeroCaptionButtons=false，适用于现有自定义标题栏。

## 验证

SDK 与工作区 TEMP 沿用项目设置。公共参数为 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln --configuration Release` 加公共参数 | Core 206/206、WPF 80/80，无跳过 | 0 |
| `dotnet build NetGuardian.sln --configuration Release` 加公共参数 | 0 警告、0 错误 | 0 |

`AddWifiDialogTests.cs` 覆盖中英文及明暗主题模板的四组测试：使用 FluentWindow、标准窗口样式与 WindowChrome，不使用 AllowsTransparency；在当前 Windows 10.0.26200.0 上读取真实离屏 HWND 的 DWM 属性 33，确认偏好值为 Round=2；检查预填值、默认保存选项、两个操作按钮存在及纵向范围。

红灯记录：最初普通 Window 不能赋值为 FluentWindow，测试退出 1；加入按钮数量断言后，Release 与 Debug 各四组测试因 `Expected: 2, Actual: 0` 在按钮数量处失败，退出 1。原因是夹具没有 Application 资源，而 Owner 不向第二个窗口继承样式，导致弹窗模板缺失、视觉树为空；在测试中显式合并主题资源后通过。没有删除或降低断言。

完整输出位于忽略目录 `.dotnet-home/validation/product-ui/`：`red-add-wifi-corners.log`、`add-wifi-release-tests.log`、`add-wifi-debug-diagnostic.log`、`add-wifi-corners-complete.log`、`add-wifi-release-final-tests.log`、`add-wifi-build.log`。

独立只读审查未发现阻止提交的明确回归。DWM 属性验证证明偏好已经下发，不等同屏幕圆角像素验收；普通 WPF 内容位图不包含系统合成边角。主题资源在测试构造后合并，因此这些组合不证明弹窗所有颜色已验证。没有实际执行保存、取消、关闭、拖动交互，也没有真实保存 Wi-Fi 配置；安装版本与真实显示效果仍待验证。完整产品目标未完成。
