# 日志页紧凑窗口与跟随阅读验证

日志页在 960×500 展开长详情时，原布局的记录表高度为 0 DIP。此次将搜索、级别／来源筛选和导出固定在顶部，把记录表、详情和跟随状态放入有限高度的正文滚动区；正文最小高度 400 DIP，详情文本继续使用自己的滚动框。

滚轮边界交接曾暴露跟随回归：正文接管边界 `PreviewMouseWheel` 后，旧的普通 XAML 处理器收不到已处理事件，向上阅读不会关闭“跟随最新”。修复将观察逻辑集中到 `LogReadingBehavior.Attach`，使用 `AddHandler(Mouse.PreviewMouseWheelEvent, ..., handledEventsToo: true)`；移除 `MainWindow.xaml` 的重复 `PreviewMouseWheel` 和 `MainWindow.Log.cs` 的重复方法。程序调用 `ScrollIntoView` 不触发该用户输入观察，滚动条手动操作仍关闭跟随。

相关文件：`src/NetGuardian.App/MainWindow.xaml`、`src/NetGuardian.App/Presentation/LogReadingBehavior.cs`、`src/NetGuardian.App/MainWindow.Log.cs`、`tests/NetGuardian.App.Tests/DesktopLayoutTests.cs`、`tests/NetGuardian.App.Tests/LogReadingBehaviorTests.cs`。

## 验证

- 原布局红灯：`log-small-workarea-diagnostic.log`，中英文 960×500 各失败，记录表 0 DIP，退出 1。
- 日志正文修复后针对性 4/4 通过，原全量 Core 206/206、WPF 142/142 通过。
- 跟随回归红灯：`log-follow-handled-red.log`，向上滚轮期望关闭跟随但实际仍为 True，1 失败、2 通过，退出 1。
- 跟随修复后全量 `log-follow-handled-final-tests.log`：Core 206/206、WPF 144/144，共 350，无跳过，退出 0。
- Release 构建 `log-follow-handled-build.log`：退出 0，2 条既有 WFAC010、0 错误。

已查看 `logs-expanded-960-500-en-US.png` 等离屏图；图像使用模拟日志，且部分保存于详情滚动后的状态，不替代正式业务窗口、硬件滚轮或触控板验收。按钮位置统一到内容框外、标题下方尚未实施，本单元未安装。
