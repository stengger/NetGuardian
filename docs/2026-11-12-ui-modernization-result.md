# NetGuardian 界面整改与复查结果

日期：2026-11-12。审查依据：[根因报告](2026-11-12-ui-audit.md)。用户回复“继续”后实施。当前产品代码提交：`d6a8a9a`；本机安装尚待用户手动托盘退出后更新，不能把源码完成等同于安装完成。

## 改动与原因

| 根因 | 处理结果 | 文件与验证 |
|---|---|---|
| A01 高对比可读性 | 共用标签头动态解析文字与表面资源，主要／危险按钮使用独立描边，避免填充与背景相同时丢边界 | `Themes/Buttons.xaml`、`Palette.*.xaml`；HighContrastThemeTests 覆盖黑白系统色和预设组合 |
| A02 强调色分裂 | 深浅色从单一 AppAccentColor 引用蓝色，主题变化时同步库运行时强调资源；高对比跳过固定色同步并保留系统色 | `App.xaml.cs`、`Presentation/AppThemePolicy.cs`、调色板、主题测试；对比度脚本支持颜色令牌引用 |
| A03 提示挤压正文 | 提示区以正文空间预算为先，受最大比例约束，三条长提示在内部滚动，保留关闭和动作 | `MainWindow.xaml`、FeedbackViewportConverter、Tokens；UiModernizationTests 覆盖三尺寸及两个列表入口 |
| A04 重复样式与局部尺寸 | 共用表格／空态等资源移入 Common，卡片／标题／分组移入 Surfaces；测速不再定义第二套 Card，展示代码与主窗口、菜单间距走令牌 | `Themes/Common.xaml`、`Surfaces.xaml`、`Tokens.Common.xaml`、SpeedTestView、TrayContextMenu、ResponsiveLayout；动态描边令牌测试 |
| A05 按钮状态 | 文字与表面反馈分离，悬停／按下使用独立叠加层；常驻删除入口为中性底＋危险文字及描边，最终危险确认仍为实心 | Buttons、调色板；ButtonStateTests、ButtonFocusPresentationTests；原命令和文案不变 |
| A06 展开入口不统一 | 更新说明显式引用现有 ContentExpanderStyle；保留已有箭头中心、减少动画与内容访问行为 | MainWindow、UiConsistencyTests、原 UpdateLayoutTests／ExpansionInteractionTests |
| A07 无线列表争高 | 紧凑阈值基于实际内容高度；连接摘要及选中说明单行且全文 Tooltip；双击说明保留在连接详情；列表等比分配，空扫描状态不占位，保留原虚拟化 | MainWindow、ResponsiveLayout、令牌；SharedSurfaceTests 验证最小及默认窗口有 36 条网络时双列表至少两行 |
| A08 容器层级不一 | 策略与更新分组使用轻分隔，设置／策略统一标签列和内容宽度；测速复用卡片表面 | FormGroupStyle、页面布局；原设置、更新、测速和最小窗口测试 |
| A09 辅助技术标注 | 两个单位选择引用原标签；Error／Danger 使用一致错误播报语义 | MainWindow、FeedbackPresenter、UiConsistencyTests；未新增或修改中文文案 |
| A10 装饰图标混用 | 空态、警告和状态装饰字符复用现有 Fluent SymbolIcon，原说明全文保留 | MainWindow、Common、相关空态／状态布局测试；品牌图标未重绘 |
| T01 托盘 Esc | 记录到焦点位于 PopupRoot、Esc 已路由并被默认处理，但菜单保持开启；自定义菜单显式处理 Esc 关闭 | TrayContextMenu；原失败断言保留，新增菜单根与子项路径及重新打开测试 |

已对照修改前 XAML 核验 339 个命名控件全部保留，命名控件的 Click／Checked／Unchecked／SelectionChanged／Drop／MouseDoubleClick／Closing 绑定无变化。未修改后台、核心业务、IPC、网络恢复策略、配置、数据结构或 NuGet 依赖。

## 验证与证据

所有本地输出位于 `artifacts/ui-audit/`，按忽略规则不提交。完整错误和堆栈保留在对应日志；下表不给失败记录改名为通过。

| 检查 | 结果 | 输出 |
|---|---|---|
| 第一批完整构建／回归 | 构建成功；核心 305/305，桌面 310/311，退出 1；唯一原有托盘 Esc 失败 | `theme-build-verified.log`、`theme-test-verified.log` |
| 共用组件完整构建／回归 | 核心 305/305、桌面 319/319，退出 0，无跳过 | `components-build.log`、`components-full.log` |
| 最后按钮表面调整专项 | 9/9，退出 0 | `components-final-targeted-verified.log` |
| 页面／小工作区专项 | 19/19，退出 0 | `surfaces-targeted-verified.log` |
| 整套截图复查构建／回归 | 构建成功，保留原有两条 WFAC010 DPI 提示；核心 305/305、桌面 323/323，退出 0，输出完整矩阵 | `final-build.log`、`final-test.log`、`final/results/*.trx` |
| 最后描边令牌检查 | 新测试先复现 1 DIP 固定描边未跟随 2 DIP 令牌；修正后主题／按钮／共用表面 21/21，退出 0 | `token-border-red.log`、`final-token-build.log`、`final-token-test.log` |
| 颜色对比度 | 深浅色 14 对通过，退出 0；不代表所有状态已满足 AA | `contrast-final.log` |
| 正式构建与发布验证 | Release 核心 305/305、桌面 324/324，退出 0，无跳过；dotnet publish 成功，退出 0；构建标识 1.1.1+d6a8a9af384c1f1210105ba6dfa08208b6adb61a | `release-test.log`、`publish.log` |
| Git 差异检查 | 提交前检查并修正 Common 文件末尾额外空行；最终检查退出 0 | `git diff --check`／`git diff --cached --check` |

复现时先设置项目 SDK：`$env:NETGUARDIAN_DOTNET=Join-Path $PWD '.dotnet-home/sdk/dotnet.exe'`，执行 `. ./scripts/initialize-build.ps1`。本轮主要命令：

- `dotnet build NetGuardian.sln --no-restore`。
- `dotnet test NetGuardian.sln --no-build --no-restore`（Debug 完整回归）。
- `dotnet test NetGuardian.sln --configuration Release --no-restore --logger 'trx;LogFilePrefix=release' --results-directory artifacts/ui-audit/final/results`（正式配置完整回归）。
- `dotnet publish src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --no-build --no-restore --output artifacts/ui-audit/publish`。
- `python scripts/check-contrast.py`。

测试前 TEMP/TMP 指向项目 `artifacts/ui-audit/temp`；`NETGUARDIAN_UI_AUDIT_DIR` 指向 `final/matrix`，`NETGUARDIAN_DESKTOP_PREVIEW_DIR` 指向 `final/baseline`。这里的 dotnet 均为初始化脚本解析的项目 SDK 可执行文件。

中间失败与修正均保留：

- `theme-red.log`：新增主题测试 4 项失败（预期复现）；`theme-test.log`：高对比新增资源键重复导致资源解析失败，修正后专项 14/14。
- `components-red.log`：4 项新布局／一致性断言失败（预期复现）。`feedback-targeted.log`：提示限高后仍有两项列表越界，结合实际内容紧凑展示修正。
- `viewport-test.log`：核心通过，桌面 314/316，两个 960×500 布局因常驻说明新增高度失败；将原说明放入现有连接详情，之后小工作区测试通过。
- `components-targeted.log`：旧问题修正后，尚未实现的新按钮／一致性断言 3 项失败。`components-final-targeted.log`：调整叠加层时多留 XAML 闭合标签，MC3000，退出 1；修正后专项 9/9。
- `surfaces-red.log`：共用样式／两行视口 3 项新断言失败。`surfaces-targeted.log`：迁移后的相对路径多了一层 Themes，22 项资源加载失败；修正路径。
- `surfaces-targeted-2.log`：两个操作栏恢复断言及最小窗口两行断言失败；将操作栏判据恢复为短窗口，空扫描状态不占位，随后专项 19/19。
- `surfaces-full.log`：核心通过，桌面 322/323；从未挂接的动态画刷取色得到透明色，新增运行时强调色测试失败；改为直接取 Color 令牌，随后整套 323/323。
- `token-border-red.log`：预期复现固定描边，修正后 21/21。
- 各日志中错误消息和完整堆栈均可直接查看。没有删除、禁用或跳过原用例；没有把 Git 权限／资源路径错误解释成 .NET 工具不可用。

本地预览入口：`artifacts/ui-audit/final/index.html`，图片尺寸与 SHA256 清单：`final/manifest.json`。

## 前后对比截图

修复前根目录：`artifacts/ui-audit/matrix/`；修复后：`artifacts/ui-audit/final/matrix/`。两边保持相同文件名，便于复核。

| 场景 | 两边相同文件名 | 拼图 |
|---|---|---|
| 最小窗口、36 条网络 | `WifiPanel-Dark-1040x660-n36-100.png` | `final/contact/compare-WifiPanel-Dark-1040x660-n36-100.png` |
| 高对比标签可读性 | `ToolsPanel-HighContrast-1180x758-n3-100.png` | `final/contact/compare-ToolsPanel-HighContrast-1180x758-n3-100.png` |
| 错误详情展开 | `Feedback-Dark-Danger-True.png` | `final/contact/compare-Feedback-Dark-Danger-True.png` |
| 策略分组 | `PolicyPanel-Light-1180x758-n3-100.png` | `final/contact/compare-PolicyPanel-Light-1180x758-n3-100.png` |
| 设置分组 | `SettingsPanel-Light-1180x758-n3-100.png` | `final/contact/compare-SettingsPanel-Light-1180x758-n3-100.png` |
| 测速 | `ToolsTab1-Light-1180x758.png` | `final/contact/compare-ToolsTab1-Light-1180x758.png` |

正式配置完整回归再次重新生成 825 张矩阵图，并按三主题联系表检查 243 个主页面 DIP 布局，抽查重点原图、高密度、弹窗和提示。原桌面测试另有加载、禁用、长错误、更新说明、日志展开及焦点等预览。没有声称每张图都做过全尺寸人工验收。

## 覆盖边界与遗留

- 在本次隔离布局测试及已检查截图范围，审查列出的高、中严重度根因均有对应修正和回归证据。真实桌面完整验收仍未完成，不能宣布“所有环境零问题”。
- 96／120／144 DPI 是 RenderTargetBitmap 输出密度，不等于操作系统 100%／125%／150% 缩放及跨屏实测；本轮未修改用户系统 DPI 或主题。
- 高对比使用库预设＋系统色组合和黑白键覆盖测试，没有实际切换运行中的系统高对比。
- 真实读屏器、全部悬停／按下组合和系统目录选择器、启动失败系统弹窗未逐项人工验收。自动化桌面只提供根窗口，不能据此假定完整键鼠交互通过。
- 矩阵主窗口是隔离夹具，部分动态状态与下拉值为空；更新卡等需结合原 UpdateLayoutTests 的有数据截图，不把夹具空白当产品缺陷。
- 列表保留原 Recycling 虚拟化；36 条网络／360 条日志布局已覆盖，尚无新的长期驻留或 1000+ 网络性能基准。
- WFAC010 为既有 WinForms/WPF manifest DPI 提示，本轮不删除 WPF DPI 配置以消除提示。
- 文案建议保留在原报告，未实施任何中文改写。未添加 NuGet 依赖。

## Git 提交

- `58b8397`：统一主题强调资源并修复高对比标签与按钮边界。
- `3dabb4d`：修复托盘菜单无项焦点时无法通过 Esc 关闭。
- `663ced6`：统一共用组件状态并保障提示展开时的正文视口。
- `cf5d9e6`：以单一颜色令牌同步库运行时强调色。
- `d6a8a9a`：统一页面分组与设计令牌并改善无线列表可见空间。

## 本机更新

发布目标为正式配置的界面文件，版本沿用项目 `1.1.1`，以提交与 SHA256 区分本轮界面构建。待更新目录：`C:\Program Files\NetGuardian` 和项目 `dist\NetGuardian`。核心与后台源码自当前安装记录 `bc34a0d` 起无变化，本轮更新只计划替换必要界面文件，保留现有核心、后台、配置及运行状态。

管理员只读预检已通过（`artifacts/ui-audit/ui-deployment-preflight.json`）：界面 PID 102432 来自 `C:\Program Files\NetGuardian\NetGuardian.exe`，后台 PID 116040 来自该目录 guardian 子目录。每目录只需替换 NetGuardian.exe、NetGuardian.dll、NetGuardian.pdb 和 UIInstalledBuild.json；依赖与运行时配置哈希一致，不替换核心／后台文件。普通进程查询无法读取其路径、CIM 查询拒绝访问的限制已通过正常 UAC 核验解决。按项目规则，必须请用户从托盘手动退出后才能替换，不能自动关闭或强制结束。备份、哈希校验、启动路径与后台未重启核验须在安装步骤完成后补记。
