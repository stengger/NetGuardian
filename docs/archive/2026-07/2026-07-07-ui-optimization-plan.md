# NetGuardian UI/UX 优化实施方案（U 系列）

- 日期：2026-07-07
- 依据：`UI_REVIEW.md`（同日完成的四维度设计评审，40 项问题：视觉 12 / 交互 12 / 现代化 7 / 无障碍 9）
- 性质：**纯实施方案，本文档不包含任何代码改动**。文中代码均为示意，供执行阶段参考。
- 编号说明：UI 优化采用 **U1–U5** 独立编号，与架构优化的 M 系列（M1–M4 已完成，下一步 M5）互不冲突，可交叉排期（见 §8）。

---

## 0. 总体原则

- **不动 Core 层**：全部改动限定在 `NetGuardian.App`（XAML + code-behind）与资源字典；`MonitoringEngine` 等 Core 组件零改动，现有 xUnit 用例（当前 66 个）全绿是每阶段回归底线。
- **行为兼容**：不改变任何功能语义与 `settings.json` 兼容性（U5 的窗口记忆等新字段有安全默认值）。
- **官方 token 优先**：能映射到 WPF-UI 4.3 原生主题资源的（文本色、控件底色）优先复用官方资源（随深浅主题自动翻转、零维护），自定义 token 只补语义缺口（状态色、图表色）。
- **每阶段独立可交付**：阶段结束时可构建、可运行、可截图验收，允许单独合入。
- **先地基后表皮**：token（U2）先于一切视觉调整；Snackbar 基建（U3）先于各处反馈改造，避免二次返工。

## 1. 阶段划分

| 阶段 | 主题 | 依赖 | 预估规模 | 覆盖问题（严重项加粗） |
|------|------|------|----------|------------------|
| U1 | 速赢包：bug + 硬违规 + 一行级修复 | 无 | 小（≈1 天） | **I-02、A-01、A-03、V-03(a)**、V-08、I-08、A-02(关键点位) |
| U2 | 设计 token 与主题系统 | U1 | 大（2–3 天，本方案核心） | **V-02、M-01**、V-04、V-06、V-07、V-09、V-10、V-11 |
| U3 | 反馈体系：Snackbar / 加载态 / 确认 | U2 | 中（≈2 天） | **I-01、I-03**、I-04、I-07、I-09、I-10、A-06 |
| U4 | 对话框归一与操作层级 | U2、U3 | 中（2–3 天） | **V-01、A-02(全量)**、V-05、I-05、I-06、A-04、A-05、A-07、A-08 |
| U5 | 现代化打磨（可选池，可拆散执行） | U2 | 中（≈3 天） | M-02、M-04、M-05、M-06、M-07、I-11、I-12、V-12、A-09 |
| — | 暂缓（单独立项） | — | 大 | M-03（NavigationView 内容模型重构） |

严重项清零点：完成 U1–U4。U5 全部为体验增强，可按需挑选。

---

## 2. U1 速赢包（≈1 天）

目标：用最小改动清掉"一行级"的严重问题，当天可验收。

| # | 问题 | 位置 | 改法 |
|---|------|------|------|
| 1 | **I-02** 设密码后弹假错误 | `MainWindow.xaml.cs` `SetPassword_Click` 第一分支 | `RunUiActionAsync(...)` 后补 `return;`（一行） |
| 2 | **A-01** 表格键盘焦点不可见 | `MainWindow.xaml` GridRowStyle 与 DataGridCell 隐式样式中两处 `<Setter Property="FocusVisualStyle" Value="{x:Null}" />` | 删除两行；若默认虚线框观感不佳，改挂 §5.4 的统一焦点环资源 |
| 3 | **A-03/V-04(a)** 7.2px/10.5px 字号 | 收起侧栏速度区（SidebarSpeedCompact） | 信息减量：单位并入数值行（"3.2M"），数值 ≥11px；或收起态只显示双箭头+Tooltip |
| 4 | **V-03(a)** 页头三按钮零间距 | CheckNow/StartMonitor/Stop 三个 Button | 各加 `Margin="8,0,0,0"`（左邻 8px） |
| 5 | V-08 导航图标 | RootNavigation MenuItems | Policy 换 `Shield24`（或 `TaskListLtr24`）；`WifiSettings20→Wifi124`、`GlobeSearch20→Globe24` 统一 24 号 |
| 6 | I-08 日志裸英文串 | code-behind 5 处（"Monitor started." / "Monitor stopped." / "Connecting to {0}." / "Connection command sent/failed" / "Wi-Fi profile already exists"） | 补 Localizer key（zh/en 同步），`Log(_localizer.T(...))` |
| 7 | A-02(关键点位) | 策略页 3 个 TextBox、状态圆点、AddWifiDialog 关闭按钮 | 输入框加 `AutomationProperties.LabeledBy`；StatusDot 所在 Border 加 `AutomationProperties.Name`（绑活动状态文案）；关闭按钮加 Name="关闭" |

验收：编译过、xUnit 用例全绿；键盘 Tab/方向键在任一表格内焦点可见；设置密码全流程无假错误；中文界面运行 10 分钟无英文事件串；页头按钮间距截图对比。

---

## 3. U2 设计 token 与主题系统（2–3 天，本方案核心）

### 3.1 目标形态

主窗口/对话框中 **54+12 个硬编码 hex 收敛为 ≤16 个语义 token**；主题跟随系统深浅色自动切换（`SystemThemeWatcher`），设置页提供"跟随系统/浅色/深色"三选。

### 3.2 资源结构（新增文件，均在 `src/NetGuardian.App/`）

```
Themes/
  Tokens.Common.xaml   ← 字号/间距/圆角等与主题无关的 token
  Palette.Dark.xaml    ← 深色色板（自定义部分）
  Palette.Light.xaml   ← 浅色色板（自定义部分）
```

App.xaml 合并 `Tokens.Common` + 按当前主题合并对应 Palette；订阅 `ApplicationThemeManager.Changed` 在主题切换时替换 Palette 字典（WPF-UI 的 `ThemesDictionary` 只翻转官方资源，自定义色板需要这一步联动——执行时核对 4.3.0 的事件签名）。

### 3.3 色彩 token 清单（建议值已程序化预验证 AA，19/19 通过）

**第一优先：直接复用 WPF-UI 官方资源**（自动随主题翻转）：

| 用途 | 官方资源（DynamicResource） |
|------|------------------------------|
| 主文本 | `TextFillColorPrimaryBrush` |
| 次级文本 | `TextFillColorSecondaryBrush` |
| 三级/提示文本 | `TextFillColorTertiaryBrush` |
| 禁用文本 | `TextFillColorDisabledBrush` |
| 控件底/描边 | `ControlFillColorDefaultBrush` / `ControlStrokeColorDefaultBrush` |
| 卡片底/描边 | `CardBackgroundFillColorDefaultBrush` / `CardStrokeColorDefaultBrush` |
| 强调色 | `SystemAccentColorPrimaryBrush`（QualityScoreText 已在用，推广） |

**第二优先：自定义语义 token**（官方资源没有的语义缺口）：

| Token | Dark 值 | Light 值 | 用途 |
|-------|---------|----------|------|
| `StatusHealthyBrush` | #6EA8FF | #2563EB | 健康状态、下载曲线 |
| `StatusWarningBrush` | #F59E0B | #A16207 | 警告状态、上传曲线、检测中 |
| `StatusDangerBrush` | #F47289 | #C0355C | 故障状态、错误文本 |
| `SurfaceCardBrush`* | #272C33 | #FFFFFF | 若不用官方 Card 资源时的卡片底 |
| `StrokeInputBrush` | #7A8694 | #8A94A2 | 输入框描边（满足 1.4.11 的 3:1） |
| `ChartAccentBrush` | #55E6D5 | #0D9488 | 测速仪表盘弧线 |

\* 深色卡片 #272C33 比 Mica 背景亮一档（1.16:1 层次差），同时修复 V-06 的"海拔反了"问题。预验证明细：TextTertiary 档在新卡片上 5.35:1、StrokeInput 3.55:1（dark）/3.07:1（light）、全部状态色 ≥5:1，AA 全过。

### 3.4 旧值→token 收敛映射表（执行时按表机械替换）

| 旧值（主窗口 + 对话框） | 归并到 |
|--------------------------|--------|
| #F2F5F8 #F4F8FF #F7FAFF #EEF4FF #E7EDF5 #F4F7FB #F5F0E6 #F4F1E8 | TextFillColorPrimary |
| #CDD6E2 #D5DCE6 #DDE5EF #DDE6F2 #D9E2EF #D8E0EA #DADDD3 | TextFillColorSecondary |
| #AEB8C5 #9AA7B8 #8FA0B5 #9FB0C4 #91A2B8 #B9C3D0 #AEB8AA | TextFillColorTertiary |
| #11161A #12171D #101418 #0E141B #111820 #151B24 #181D17 | CardBackground（或 SurfaceCardBrush） |
| #171D25 #1B222B #20251F（含 hover/pressed #1D2430 #223047） | ControlFill 系（hover/pressed 用官方 Secondary/Tertiary 档） |
| #2A3038 #2B3440 #2A323D #303946 #344054 #2C3B50 #253349 #26364A | CardStroke |
| #364252 #485A70 #4A5146 | StrokeInputBrush |
| #6EA8FF #60A5FA → StatusHealthy；#F59E0B → StatusWarning；#F47289 #EF6B5B → StatusDanger；#69D18B → 删除（对话框主按钮改官方 Accent）；#55E6D5 → ChartAccent | 状态/图表组 |
| 其余低频值（渐变 stop、量表底纹等） | 就近归并；行悬停/选中三段渐变直接改纯色（见 3.6） |

### 3.5 字号 / 圆角 / 间距 token

- 字阶 6 档：`FontCaption 12`、`FontBody 14`、`FontBodyStrong 14+SemiBold`、`FontSubtitle 20`、`FontTitle 28`、`FontDisplay 34`。现有 13 种就近归档：10/10.5/11→12；13/16→14；18/20→20；24/28/30→28；34/36→34。7.2px 已在 U1 消灭。
- 圆角 2 档：`CornerControl 4`（按钮/输入/徽章）、`CornerCard 8`（卡片/浮层）。现 9→8、10→4 或 8、2（强调条）保留为装饰特例。
- 间距档位：4/8/12/16/24，以 `Thickness` 资源提供常用组合；替换时顺带把 Wi-Fi 页 6px 按钮距对齐为 8px。

### 3.6 连带清理（替换过程中顺手完成）

- **V-10**：行悬停/选中的三段 LinearGradientBrush → 纯色（hover=官方 SubtleFillColorSecondary；selected=Accent 15% 叠加 + 左缘 3px Accent 条）。
- **V-11**：删除 SectionBorderStyle 的 DropShadowEffect（新卡片靠亮度层次+描边区分）。
- **V-09**：文字类 Opacity（0.72/0.74/0.78）全部删除，改用 Tertiary 文本 token。

### 3.7 主题跟随

- `App.OnStartup` 调 `SystemThemeWatcher.Watch(mainWindow)`；
- 设置页新增"应用主题"三选 ComboBox（跟随系统/浅色/深色），存 `AppSettings.Theme`（默认 `auto`，向后兼容）；
- 双语 key：`ThemeMode/ThemeAuto/ThemeLight/ThemeDark` 等同步补 zh/en。

### 3.8 执行策略与验收

- 逐面板替换（Status → Wi-Fi → Tools → 其余），每完成一个面板交叉编译一次（命令见 §10）；
- 验收：全项目硬编码 hex 白名单 ≤6（仅图表渐变等注明豁免）；切换 Windows 深浅色应用实时跟随且无"黑字黑底"；对比度脚本对两套主题重跑全过 AA；深/浅两套全页面截图存档。

---

## 4. U3 反馈体系（≈2 天）

### 4.1 全局 Snackbar 基建（I-01 的解，也是 I-03/I-07 的载体）

- `MainWindow.xaml` 根 Grid 内加 `<ui:SnackbarPresenter x:Name="RootSnackbar" />`（右下角，Panel.ZIndex 最高）；
- DI 注册 `ISnackbarService`（WPF-UI 4.3 自带），`SetSnackbarPresenter(RootSnackbar)`；
- 封装 `ShowFeedback(title, message, severity)`：severity → Info/Success/Warning/Danger 外观与图标。

### 4.2 错误就地可见（I-01）

- `RunUiActionAsync` 的 catch：`Log(ex)` 之外追加 `ShowFeedback("操作失败", ex.Message, Danger)`——**改一个方法，全应用 20+ 处操作受益**（这是现有架构的红利点）；
- `App.OnDispatcherUnhandledException`：写 crash.log 后追加用户可见提示（Snackbar 或 `ui:MessageBox`："应用遇到问题，详情已写入日志"），不再完全静默。

### 4.3 删除防护（I-03）

推荐"直接删 + 5 秒撤销"：`Remove_Click` 暂存被删 profile 与位置 → Snackbar 带"撤销"按钮 → 超时才真正 `SaveSettings()`。备选（更省事）：复用 `ConfirmActionAsync` 弹确认。二选一，推荐前者（符合 Fluent 无阻断删除模式）。

### 4.4 加载指示（I-04）

- 通用忙碌模式：`SetBusy(Button button, bool busy)` 辅助方法——运行中把按钮 Content 换为"16px `ui:ProgressRing (IsIndeterminate)` + 原文案"，禁用并保存原 Content，结束还原；
- 落点：质量检测、网站测试、IP 刷新、诊断报告、扫描、DNS 应用 6 处（测速已有仪表盘反馈，仅补按钮态）；
- 检测中的头部状态点换成 12px ProgressRing（同时解决 A-06 的"色差不可分"）。

### 4.5 空状态补齐（I-09）

为 WebsiteGrid / IpInfoGrid / ReportGrid / DnsGrid / AdapterGrid / QualityProbeGrid 复用现有空状态模式（Tertiary 色文案 + 指向主操作："尚无数据，点击〔运行诊断〕开始"）；接入现 `UpdateEmptyStates()`。双语 key 6 组。

### 4.6 表单校验与成功反馈（I-10、I-07）

- 策略页 3 个数值输入换 `ui:NumberBox`（Min/Max 约束：间隔 1–86400s、失败次数 1–20、冷却 0–86400s），非法值不再静默回退；
- 保存策略/应用 DNS 成功 → Success Snackbar（2s）；
- 监控恢复成功/失败且窗口最小化时 → `_notifyIcon.ShowBalloonTip`（标题含 SSID，双语）；复用 M4 已有的气泡调用模式，成功用 Info 图标、失败用 Error 图标。

### 4.7 状态双通道（A-06）

健康状态大字左侧加状态图标：Healthy=`CheckmarkCircle24`、Warning 类=`Warning24`、Failed=`DismissCircle24`、Recovering=ProgressRing；颜色照旧走状态 token。

验收：断网点扫描当前页立现红色 Snackbar；删除可撤销；6 处长任务运行中按钮内可见 ProgressRing；9/9 列表有空状态；策略页输错即时标红不可保存。

---

## 5. U4 对话框归一与操作层级（2–3 天）

### 5.1 AddWifiDialog 重写（V-01、A-04、A-08）

- 形态：改 `ui:ContentDialog`（主窗口内呈现，自动继承主题与深浅切换；DI 注册 `IContentDialogService` + 主窗口挂 `ContentPresenter`）。若验证流程需要独立窗口（连 Wi-Fi 时主窗口可能被系统对话框遮挡），备选 `ui:FluentWindow`；
- 字段与行为**完全不变**（SSID/认证/密码/保存到 Windows/校验流程），仅换容器与样式：全部色值走 token、输入框描边用 `StrokeInputBrush`（3:1 达标）、密码框换 `ui:PasswordBox`（自带明文切换，A-08）、主按钮走官方 Accent（删除绿色 #69D18B）、关闭按钮 `ui:SymbolIcon Dismiss24` + AutomationProperties.Name；
- 错误文案区保留（token 化为 StatusDanger），SSID 为空聚焦逻辑照旧。

### 5.2 主操作强调（V-05）

每页指定唯一主按钮，套 WPF-UI `Appearance="Primary"`：状态页=立即检测、Wi-Fi 页=扫描、测速页=开始测速、网站页=测试、策略页=保存策略、DNS 页=应用 DNS、报告页=运行诊断。其余按钮维持 Standard。

### 5.3 Wi-Fi 页命令重组（I-06、I-05）

- 顶栏只留列表级命令：`扫描`（主按钮）、`手动添加`；
- 选择条（WifiSelectionBar）右侧按当前选中对象显示上下文按钮：选中附近网络→`加入优先列表` `设置密码`；选中配置→`连接` `设置密码` `导入密码` `移除`；
- 优先列表卡片头部放 `上移` `下移`（图标按钮，保留键盘替代职责）；
- "凭据"列的禁用 ToggleSwitch → 图标徽章（`LockClosed16`=已保存 / 空占位），附 AutomationProperties.Name（I-05）；
- 布局示意：

```
┌ 顶栏: [🔍 扫描(主)] [＋手动添加]                        ┐
│ 选择条: 已选 CoffeeShop-5G (信号 82%) │ [连接] [设密码] [移除] │
│ ┌ 附近网络 ────────────┐  ┌ 优先列表  [↑][↓] ──────┐   │
└ └──────────────────────┘  └───────────────────────┘   ┘
```

### 5.4 焦点环与键盘（A-05、A-07）

- 定义统一 `FocusVisualStyle`（2px 圆角描边、offset 2，深色白/浅色黑），应用于按钮/开关/下拉/表格行；
- InputBindings：`F5`=立即检测、`Ctrl+R`=扫描（Wi-Fi 页可见时）、`Ctrl+,`=设置、`Ctrl+E`=导出日志；按钮文案补 access key（`_`）。

### 5.5 无障碍全量精查（A-02 收口）

- 全部交互控件过一遍 Name/LabeledBy；装饰元素（sparkline、量表刻度）标 `AccessibilityView=Raw`；
- Narrator 走查 5 条关键路径（见 §9），逐条修正播报。

验收：对话框深/浅两套截图与主窗口同语言同皮肤；Narrator 走查 5/5 通过；每页恰一个 Accent 主按钮；Wi-Fi 顶栏 ≤2 按钮 + 上下文命令随选中切换。

---

## 6. U5 现代化打磨（可选池，按需挑选）

| # | 项 | 问题 | 要点 |
|---|----|------|------|
| 1 | 动效三件套 | M-02 | ① 面板切换 150ms 淡入+8px 上移（`ShowPanel` 处统一挂 Storyboard）；② 按钮模板改 VisualStateManager + 100ms ColorAnimation；③ 检测中状态点呼吸动画。WPF 无隐式动画，均需显式 Storyboard；遵守 `SystemParameters.ClientAreaAnimation`（系统关动画则跳过） |
| 2 | 仪表盘自适应 | M-04 | Canvas 外套 `Viewbox Stretch="Uniform"`（最低成本）；刻度文案随 Localizer |
| 3 | 弹性列宽 | M-05 | 表单标签列 `Auto+MinWidth`；DataGrid 定宽列改 `*`+MinWidth；英文语言全页走查无截断 |
| 4 | Mica 透出 | M-06 | 卡片底改半透明（如 #E6272C33），依赖 U2 token；性能回归验证 |
| 5 | 日志页增强 | I-11 | 清空按钮 + 级别着色（Danger/Warning 行前景）；导出对话框由 WinForms FolderBrowserDialog 换 `Microsoft.Win32.SaveFileDialog`；理想态虚拟化 ListView + 级别筛选 |
| 6 | 窗口记忆 | I-12 | Closing 存 Width/Height/Left/Top/State 入 AppSettings（新字段默认值兼容），启动还原 + 多显示器越界校验 |
| 7 | 状态字段化 | V-12 | code-behind 加 `NetworkState? _lastState`，删除 "尚未检测"/"Not checked" 字符串比较 |
| 8 | sparkline 微交互 | M-07 | Tooltip 显示 28s 峰值/均值；点击跳转测速页 |
| 9 | 高对比度 | A-09 | 检测 `SystemParameters.HighContrast` → 切 SystemColors 映射色板；至少保证可读不叠色 |

## 7. 暂缓项（建议单独立项）

**M-03 NavigationView 内容模型重构**：9 个面板拆分为 UserControl/Page、改用 `INavigationView` 内容导航、删除 `PreviewMouseLeftButtonUp` hack。收益是免费页面过渡与更清晰的页面生命周期，但涉及全部 code-behind 事件接线重排，改动面 ≈ MainWindow 全文件，风险与工时（3–5 天）均超本轮任何单阶段。建议：U 系列完成、界面稳定后单独立项；U5-1 的面板淡入动效可先止血 80% 的观感问题。

## 8. 与 M 系列的排期关系（按 M4 已完成的现状修订）

**现状**：M1–M4 已完成（2026-07-07），下一步为 M5 体验补齐。M4 新增的策略页高风险开关、确认框与托盘告警气泡已按现有 UI 模式实现——U2 执行时将这些新控件一并纳入 token 替换范围即可，无需单独返工。

**建议顺序：U1 → U2 → U3（吸收 M5-②③）→ U4 → M5（剩余项）→ U5。**

关键点是 **M5 与 U3 存在两项重叠**，应合并执行避免重复实现：

| M5 计划项 | U 系列对应 | 处置 |
|-----------|------------|------|
| ② 删除 Profile / 注销计划任务确认框 | I-03（U3-4.3） | 并入 U3：删除走"Snackbar 撤销"或确认框，注销计划任务走 ConfirmActionAsync |
| ③ 扫描进度 ProgressRing | I-04（U3-4.4） | 并入 U3 的统一 SetBusy 忙碌模式 |
| ① Profile 即时连接验证 / ④ 探测目标配置化 / ⑤ 首启三步引导 | 无重叠 | 留在 M5；其中 ⑤ 首启引导卡片建议排在 U2 之后，直接使用新 token 与卡片样式 |

U3 的托盘气泡（I-07）可直接复用 M4 已落地的 `ShowBalloonTip` 模式（MainWindow.xaml.cs 已有 Warning 气泡先例），只需扩展到"恢复成功/失败"两个事件并区分图标。

## 9. 验收标准与验证工具

**每阶段通用**：`dotnet build`（Windows）+ Linux 交叉编译均过；`dotnet test` 全绿（当前 66 用例，UI 改动不应触碰 Core）；深/浅（U2 起）全页面截图存档对比。

**专项工具**：

| 工具 | 用途 | 阶段 |
|------|------|------|
| 对比度脚本（评审所用 WCAG 公式脚本，建议固化为 `scripts/check-contrast.py`，输入 token 表输出 PASS/FAIL） | 色板回归 | U2、U4 |
| Accessibility Insights for Windows（FastPass） | 自动扫 Name/对比度/焦点问题 | U1、U4 |
| Narrator 走查清单（5 条路径：① 切换导航到 Wi-Fi；② 扫描并加入一个网络；③ 修改检测间隔并保存；④ 触发一次错误听播报；⑤ AddWifiDialog 全字段填写） | 读屏可用性 | U4 |
| 键盘走查清单（无鼠标完成：检测/扫描/加网络/调策略/删配置/导出日志） | 键盘可用性 | U1、U4 |
| 100%/150%/200% DPI + 1040×660 最小窗口走查 | 布局回归 | U2、U5 |

## 10. 工程注意事项（供执行会话参考）

- Linux VM 无 dotnet：SDK 装 `~/dotnet`（VM 重置需重装，分段 `curl -C -` 下载）；App 交叉编译验证用 `dotnet build -p:EnableWindowsTargeting=true`；
- **挂载层在 Edit 缩短/扩长文件时会产生尾部 NUL 填充或截断**：MainWindow.xaml 这类大文件改动用 bash heredoc/python 整体写入，改完扫描 NUL；
- 工作树有 CRLF/LF 噪声：diff 用 `--ignore-cr-at-eol`；
- 所有新增用户可见文案必须 zh/en 双语 key 同步（Localizer），禁止 Log 裸英文串（U1-6 的回归项）；
- WPF-UI 4.3.0 API 以实际包为准：执行前核对 `ISnackbarService`/`IContentDialogService`/`SystemThemeWatcher`/`ApplicationThemeManager.Changed` 签名（版本间有过变动）。

## 11. 问题覆盖追溯表（40/40）

| 问题 | 阶段 | 问题 | 阶段 | 问题 | 阶段 | 问题 | 阶段 |
|------|------|------|------|------|------|------|------|
| V-01 | U4 | V-07 | U2 | I-01 | U3 | M-01 | U2 |
| V-02 | U2 | V-08 | U1 | I-02 | U1 | M-02 | U5 |
| V-03 | U1(a)/U2(b) | V-09 | U2 | I-03 | U3 | M-03 | 暂缓§7 |
| V-04 | U1(a)/U2(b) | V-10 | U2 | I-04 | U3 | M-04 | U5 |
| V-05 | U4 | V-11 | U2 | I-05 | U4 | M-05 | U5 |
| V-06 | U2 | V-12 | U5 | I-06 | U4 | M-06 | U5 |
| | | | | I-07 | U3 | M-07 | U5 |
| | | | | I-08 | U1 | A-01 | U1 |
| A-02 | U1(关键)/U4(全量) | A-04 | U4 | I-09 | U3 | A-03 | U1 |
| A-05 | U4 | A-06 | U3 | I-10 | U3 | A-07 | U4 |
| A-08 | U4 | A-09 | U5 | I-11 | U5 | I-12 | U5 |

## 12. 风险与缓解

| 风险 | 等级 | 缓解 |
|------|------|------|
| 浅色主题是全新视觉面，token 初值需要人工验收 | 中 | 色板已程序化预验证 AA（19/19 过）；U2 验收含浅色全页截图；不满意只调 Palette.Light.xaml 单文件 |
| 54 处色值替换引入视觉回归 | 中 | 逐面板替换逐次编译；替换前后截图 diff；映射表（§3.4）作为唯一依据，禁止即兴改色 |
| WPF-UI 4.3 API 与方案假设有出入 | 低 | §10 列出 4 个核对点；均有降级路径（如 SnackbarService 不合用则手写 Presenter 包装） |
| ContentDialog 在连 Wi-Fi 场景被系统弹窗遮挡 | 低 | §5.1 保留 FluentWindow 备选，行为不变 |
| U3 撤销式删除引入状态复杂度 | 低 | 备选方案为传统确认框（一行接入现有 ConfirmActionAsync） |
| 动效在低端机拖慢 | 低 | 遵守 ClientAreaAnimation 系统开关；动画仅 Opacity/Transform（GPU 合成友好） |

## 实施进度

- [x] U1 速赢包 —— 2026-07-07 完成。实施记录：I-02 补 return；A-01 删两处 FocusVisualStyle x:Null；A-03 紧凑侧栏改单行 11px + 悬浮提示（新增 FormatRate/FormatRateCompact）；V-03 头部按钮 8px 间距；V-08 图标 Wifi124/Globe24/Shield24；I-08 六处日志文案本地化（含 Check failed 兜底，Localizer zh/en 各 +8 键）；A-02 关键点位（策略页 5 个输入框 LabeledBy、HeaderStateText LiveSetting、StatusDot 动态 Name、对话框关闭钮 Name）。验证：build 0 警 0 错、69/69 测试全绿、11/11 验收断言通过。
- [x] U2 设计 token 与主题系统 —— 2026-07-08 完成。实施记录：新增 Themes/{Tokens.Common,Palette.Dark,Palette.Light}.xaml（自定义语义 token：Status{Healthy/Warning/Danger}、SurfaceCard、StrokeInput、ChartAccent、RowSelected、ChartTrack）；App.xaml 合并 Tokens+Palette.Dark，App.xaml.cs 接线 ApplicationThemeManager.Changed 热替换 Palette + ApplyConfiguredTheme(auto/light/dark) + SystemThemeWatcher；设置页新增主题三选下拉（写 AppSettings.Theme，默认 auto 向后兼容，中英各 +4 键）。色值收敛：MainWindow.xaml ~120 处属性色 + code-behind 5 处状态色 Color.FromRgb → ThemeBrush 资源解析；AddWifiDialog.cs 21 处硬编码色 → ThemeBrush（主按钮改官方 Accent，去掉 #69D18B 绿）。连带清理：删 SectionBorderStyle 阴影（V-11）、行悬停/选中三段渐变改纯色（V-10）、修复自动替换产生的 3 处 GradientStop Color 赋 Brush 的类型 bug。固化 scripts/check-contrast.py（WCAG 公式，两套色板 10 对全 PASS）。验证：交叉编译 0 警 0 错、69/69 测试全绿、Themes 外硬编码色归零。
- [x] U3 反馈体系 —— 2026-07-08 完成。实施记录：U3-1 全局 SnackbarPresenter（MainWindow.xaml 根 Grid，RowSpan=2 右下角高 ZIndex）+ DI 注册 ISnackbarService + ShowFeedback(title,message,severity) 封装（severity→ControlAppearance Info/Success/Caution/Danger + SymbolIcon）。U3-2 错误就地可见：RunUiActionAsync catch 加 ShowFeedback（一处改动全应用 20+ 操作受益）；App.OnDispatcherUnhandledException 加 NotifyUserOfCrash（Snackbar 优先，回退 MessageBox，不再完全静默）。U3-3 删除撤销：Remove_Click 立即删+带"撤销"按钮 Snackbar（原位重插，5s 自然超时+点后禁用防重）；ScheduledStartup_Changed 注销前 ConfirmActionAsync（吸收 M5-②）。U3-4 SetBusy(ButtonBase) 忙碌态（内联 ProgressRing+原文案+禁用，IDisposable 还原），落 7 处长任务按钮含扫描（吸收 M5-③）。U3-5 六处空状态（Website/IpInfo/Report/Dns/Quality/Adapter Grid + UpdateEmptyStates 扩展，中英各 +6 键）。U3-6 表单校验：策略页 3 TextBox→ui:NumberBox（间隔/冷却 Minimum=1、失败次数 1-100 匹配 Normalize；helper 改 double? Value）；保存策略/应用 DNS 成功→Success Snackbar；恢复成功/失败且窗口隐藏时→托盘气泡（Info/Error 图标区分，ShowTrayBalloon 加 icon 参数默认 Warning 兼容 M4）。验证：完整解决方案交叉编译 0 警 0 错、69/69 测试全绿、mnt 源树 0 NUL。工程注记：本轮挂载层反复截断 MainWindow.xaml.cs/App.xaml.cs/Localizer.cs 尾部，全部用 bash python/heredoc 重写 + tr -d 权威 NUL 校验 + 花括号平衡校验修复。
- [ ] U4 对话框归一与操作层级
- [ ] U5 现代化打磨（可选池）
