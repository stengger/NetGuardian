# 产品级界面整改验证记录

## 第一单元：操作可达、固定摘要和真实状态

本单元是完整产品化目标的一部分，不代表全应用已验收。

### 文件与原因

- `MainWindow.xaml`：策略内容滚动和固定保存区；无线列表响应式排列及 SSID 最小宽度；凭据改为只读标签；表格完整文本提示；按钮键盘焦点；移除初始红点。
- `MainWindow.xaml.cs`：接入布局与展示状态；后台正常接管与锁不可用分别反馈；策略未保存提示、独立候选和持久化快照；开始监控不隐式读取未保存编辑。
- `Presentation/ResponsiveLayout.cs`：在可用宽度不足 1120 DIP 时上下排列无线列表，宽窗口并排，供实际窗口和测试共同使用。
- `Presentation/MonitorStatusPresentation.cs`：集中前台、后台、检测中、停止和过期心跳的按钮、文字及颜色语义。
- `Presentation/CredentialStatusConverter.cs`：只读凭据状态和解释，中英文随界面更新。
- `Presentation/PolicyEditFeedback.cs`：比较编辑基线，规范化加载期间抑制修改通知，拒绝确认或恢复原值不误报。
- `Presentation/PolicySaveTransaction.cs`：保存独立设置快照，磁盘成功后才更新活动策略引用，避免并发读取未保存候选。
- `Views/SpeedTestView.xaml(.cs)`：固定三项摘要，滚动过程和说明；紧凑布局保留关键数字。
- `Localizer.cs`：补齐 14 个缺失静态文案键；新增状态/凭据/未保存提示；关闭开机守护的确认明确实际停止和注销任务的影响。
- `DesktopLayoutTests.cs`、`MonitorStatusPresentationTests.cs`、`PolicyEditFeedbackTests.cs`、`LocalizationAndUrlTests.cs`：分别覆盖窗口级布局、展示状态、编辑保存边界和实际文案存在性。

### 最终命令与结果

使用 SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe` 8.0.422，先运行项目 `scripts/initialize-build.ps1`；TEMP/TMP 为工作区 `.dotnet-home/product-temp`。命令公共参数为 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj` 加公共参数 | 184/184，无跳过 | 0 |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj` 加公共参数 | 27/27，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |
| `python scripts/check-contrast.py` | 两套主题 14 组颜色对通过 | 0 |
| `git diff --check` | 无差异空白错误 | 0 |

完整最终输出保存在忽略目录 `.dotnet-home/validation/product-ui/final-{core,app,build,contrast}.log`。

### 失败与纠正

- 文案测试先红：`Assert.NotEqual`，`ActionFailedTitle` 直接回显内部键，两个语言退出 1。补齐资源后另发现英文 `Undo` 本就与键同名，改为校验目标语言确实定义该键且非空，保留缺失检测，不要求翻译必须不同于键。
- 窗口宿主加载期间先后遇到图标相对资源路径、Pack URI 未初始化、序列化元素仍保留旧 CLR 命名空间，以及未创建原生窗口导致尚无视觉树，退出 1。检查实际展开类型名后明确重命名元素，设置正确资源基址并建立屏幕外 HWND。未修改产品代码规避测试。
- 首次夹具使用匿名只读对象绑定双向凭据属性导致测试宿主异常终止；改用真实 `WifiProfile` 可写模型。该问题属于夹具，与产品实际数据模型不同。
- 正确加载后实际复现：1040×660 下保存按钮位于 `279,699,54,32`；名称列仅 `20 DIP`；滚动后下载数字位于 `21,-65,75,43`。回归退出 1；修复后相同窗口结构的断言通过。
- 新增展示/保存组件前，测试以 CS0234 / CS0246 / CS0103 / CS1729 / CS1593 缺类型或接口退出 1，之后实现并通过。
- 独立审查发现保存前发布候选的并发边界，以及任何事件都标脏的问题。改为先持久化独立快照，并比较已保存基线；包含失败、写入期间策略引用、恢复原值和数值提交的回归。

各回归的错误、完整堆栈及退出结果保留在同一忽略目录的 `red-*.log`、`green-policy.log` 和线程工具输出中。没有禁用、删除或跳过测试。

### 视觉验证范围

测试加载产品完整 `MainWindow.xaml`，只移除业务事件接线，保留实际布局和控件。显示屏幕外窗口以使 WPF 建立真实视觉树，使用模拟网络数据；不运行 MainWindow 构造中的监控、任务注册、网络命令和配置加载。静态文字由夹具提供，不能覆盖真实业务初始化问题。

原生 Mica 无法直接由 RenderTargetBitmap 重现，离屏输出以主题实色衬底合成透明控件。截图 `policy-1040.png`、`policy-1180.png`、`wifi-1040.png`、`wifi-1180.png`、`speed-details.png` 位于上述忽略目录，已人工检查。

未执行真实公网测速、网络切换、后台任务变更或已安装程序替换；未完成真实进程截图、跨屏 DPI、系统高对比度和讲述人验收。首页、DNS、质量、适配器、日志和设置的完整产品化仍需继续，详见总计划台账。
