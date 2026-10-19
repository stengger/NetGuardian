# 桌面一致性与设置可达性验证

- `MainWindow.xaml`：正文、页标题、卡片间距与圆角接入已有令牌；上传/下载使用与测速一致的方向色，避免上传流量使用警告色。隐藏策略、设置重复标题和工具页容易误导当前标签的固定标题。
- 设置内容整体可滚动；语言、关闭行为控件关联可访问标签。提示、单位及空状态使用可读辅助色，保留禁用控件独立弱化状态。
- `App.xaml.cs`：原先在主窗口创建前应用自动主题，尚无窗口可监听；现在在窗口显示并拥有 HWND 后再次应用配置，绑定系统主题消息。明暗显式模式仍取消监听。
- `DesktopLayoutTests.cs`：夹具支持明暗主题；四组主题/语言组合在 1040×660 验证长状态可滚到底且语言/主题入口初始可达。读取实际前景笔刷，合成透明度后验证代表性状态、空提示、风险说明、单位与卡片背景至少 4.5:1。

沿用项目 SDK、工作区 TEMP 与公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln` 加公共参数 | Core 206/206、WPF 76/76，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |
| `E:\Environment\Python\Python312\python.exe scripts/check-contrast.py` | 明暗语义色 14/14 组合通过 | 0 |

完整测试/构建日志位于忽略目录 `.dotnet-home/validation/product-ui/consistency-tests.log`、`consistency-build.log`；设置局部复核 4/4 位于 `consistency-settings.log`。已检查浅色英文长状态滚动到底的截图，不是正常内容首屏截图。

独立只读复核未发现新增明确 P1/P2。对比度测试限于列明控件和背景，不等同全部交互状态符合可访问性标准。自动主题的实际系统消息、跨屏 DPI、高对比模式、完整键盘和讲述人仍待正式进程验收；未修改用户系统主题或设置。完整产品目标尚未完成。
