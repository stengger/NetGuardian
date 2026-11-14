# 弹出提示挤压页面修复记录

用户截图显示提示出现时，Wi-Fi 页面与侧栏一起向上收缩。根因是 MainWindow 根网格第三个 Auto 行承载提示，提示的实际高度参与了正文高度分配。

## 复现与修复

| 窗口 | 提示出现前 Wi-Fi 页面高度 | 旧实现出现提示后 | 覆盖层实现 |
| --- | --- | --- | --- |
| 1180×758 | 562 | 446（减少 116） | 562 |
| 1040×660 | 464 | 384（减少 80） | 464 |

- MainWindow.xaml：提示移到正文同一网格行，去掉专门占空间的底部 Auto 行。
- Themes/Common.xaml：共用 FeedbackOverlayHostStyle 统一右下角定位、内容可见性、滚动与原高度限制。
- Themes/Tokens.Common.xaml：FeedbackOverlayZIndex 统一提示覆盖层层级。
- FeedbackOverlayLayoutTests.cs：逐项比较提示出现、三条长提示展开、全部关闭前后的页面、列表、列宽、侧栏和设置入口边界；默认、最小及窄屏尺寸覆盖深色、浅色和高对比资源。

提示临时覆盖右下角局部内容，关闭和长详情滚动入口仍可用；不覆盖页面顶部操作。队列、计时、文案、守护、IPC、扫描和恢复逻辑未修改。

## 验证记录

新测试先运行旧实现：9 个场景均因页面高度变化而失败，退出码 1。修复后提示相关专项 33 项通过，无失败或跳过，退出码 0。完整错误、TRX、修复前后截图保存在 artifacts/ui-audit/feedback-overlay-20261114/，不提交生成物。

使用项目 SDK 执行 dotnet build NetGuardian.sln -c Release --no-restore --nologo，以及 dotnet test NetGuardian.sln -c Release --no-build --no-restore --nologo：构建退出码 0；核心 305、桌面 360 项全部通过，无失败或跳过，退出码 0。完整测试与截图渲染串行运行。git diff --check 通过。

RenderTargetBitmap 图验证 WPF 布局和绘制，不替代真实系统 DPI 切换、跨屏或辅助技术验收。

## 本机更新

用户手动托盘退出后，通过标准 UAC 更新 C:\Program Files\NetGuardian 和项目 dist\NetGuardian 的五个 UI 文件；两个目录共 10 个文件哈希一致。软件提交 9884c876e5bbd6eb4c59aa74c1e8e5d6d7977c51，构建标识 1.1.1+9884c87。发布退出码 0，保留原有两条 WFAC010。

重新启动的 UI 进程 PID 88252，桌面窗口清单确认来自 Program Files 安装目录，实际启动截图显示“后台监控中”。守护 PID 99664 保持运行；替换前后、启动后 settings.json 哈希未变化。备份、安装清单和核验结果保存在本次审查目录。实际提示前后布局由 WPF 渲染和边界回归验证，本机截图确认安装启动，不冒充本机自动点击提示验证。
