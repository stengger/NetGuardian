# NetGuardian UI/UX 设计审查报告

- **审查日期**：2026-07-07
- **审查范围**：`src/NetGuardian.App/App.xaml`、`MainWindow.xaml`（9 个页面面板）、`MainWindow.xaml.cs`、`AddWifiDialog.cs`
- **审查方法**：源码静态审查 + WCAG 对比度程序化计算（28 组前景/背景组合，Mica 底色按 ≈#202020 取近似值）
- **对标基准**：本项目为 Windows 桌面 WPF 应用（基于 WPF-UI/Fluent 组件库），因此以 **Microsoft Fluent Design 2 / Windows 11 HIG** 为主要对标标准；Material Design 3 与 Apple HIG 用于交叉校验通用原则（iOS/Android 平台规范不直接适用于桌面端）。

---

## 1. 总体评估

**综合评分：6 / 10**（分项：视觉界面 6.5，交互体验 5.5，现代化 6，无障碍 4.5）

这是一个基础扎实、明显高于"随手写的工具软件"水准的深色主题桌面应用：选用了 WPF-UI 库获得 Mica 背板、Fluent 导航与标题栏，文本对比度经实测 28 组组合**全部通过 WCAG AA**（最低 5.17:1），关键列表有空状态、危险操作有确认对话框、全量 tooltip 和中英双语也都做了。但它离大厂出品还有明确差距：**没有设计 token 体系**（主窗口 54 个硬编码色值 + 对话框 12 个色值且两者零重叠，等于一个应用两套皮肤）、**主题被锁死在深色**（不跟随系统）、**零动效**、**无障碍基线缺失**（0 处 AutomationProperties、表格键盘焦点被显式移除、存在 7.2px 字号），且错误反馈大量沉入日志页而非就地呈现。按 Google/Apple/Microsoft 的评审口径，这属于"结构合格、系统性缺失"的典型状态——问题集中且模式清晰，集中修复投入产出比很高。

---

## 2. 视觉界面问题清单

### V-01 新增 Wi-Fi 对话框是"另一个应用"
- **位置**：`AddWifiDialog.cs`（整个文件）
- **问题描述**：对话框使用 12 个硬编码颜色，与主窗口 54 色**零重叠**：主窗口是蓝灰色系（背景 #11161A、强调 #6EA8FF），对话框却是绿色系（背景 #181D17、主按钮 #69D18B）；自绘无边框窗口 + 手写标题栏，关闭按钮用字母 "x"（第 57 行）而非图标字形；输入框、按钮全部内联样式，不复用主窗口任何资源。
- **对标标准**：Fluent 的 `ContentDialog` 与 M3 的 Dialog 都要求对话框继承全局主题 token，与宿主窗口同一设计语言；关闭按钮应为 `ChromeClose` 字形或图标控件。
- **优先级**：严重
- **优化建议**：改用 WPF-UI 的 `ui:ContentDialog`（或至少 `ui:FluentWindow`），删除全部内联色值，复用 App 级主题资源；关闭按钮换 `ui:SymbolIcon Dismiss24`。

### V-02 无色彩 token 体系，54 个魔法色值
- **位置**：`MainWindow.xaml` 全文件（grep 实测 54 个不重复 hex）
- **问题描述**：仅"灰白色文字"就有 #CDD6E2、#AEB8C5、#9AA7B8、#8FA0B5、#B9C3D0、#9FB0C4、#91A2B8、#D9E2EF、#DDE5EF、#D5DCE6、#D8E0EA、#DDE6F2 等十余种近似色并存，肉眼不可分但代码上各自为政；WPF-UI 提供的 `TextFillColorPrimaryBrush` 等主题资源几乎未使用（全文件仅 1 处 DynamicResource）。
- **对标标准**：Fluent 2 文本色只有 Primary/Secondary/Tertiary/Disabled 四层；M3 用约 26 个 color role 覆盖全部界面。大厂规范的核心就是"色彩必须经由 token 引用"。
- **优先级**：严重
- **优化建议**：在 App.xaml 建立语义化资源字典（如 `TextPrimary`、`TextSecondary`、`SurfaceCard`、`StrokeCard`、`AccentDefault`、`StatusWarning` 等 12-16 个 token），全部改 `{DynamicResource}` 引用，一次性合并近似灰。这是后续支持浅色主题的前置条件。

### V-03 页头三个核心按钮零间距，全应用间距无节奏
- **位置**：`MainWindow.xaml` 第 289-291 行（CheckNowButton / StartMonitorButton / StopButton 均无 Margin，水平 StackPanel 中完全紧贴）；另 Wi-Fi 页按钮间距 6px（第 371-379 行）、工具页 8px（第 510-512 行）两种规格并存
- **问题描述**：应用最显眼的三个全局操作按钮边框贴边框连成一排，是最直观的"未打磨"信号；全应用 margin 值散布 4/6/8/10/12/14/16/18/20/24，不在统一网格上。
- **对标标准**：Fluent/WinUI 相邻按钮标准间距 8px；Material 与 Fluent 都要求间距取自 4px 基准网格的有限档位。
- **优先级**：严重（三按钮贴合）/ 应优化（间距系统）
- **优化建议**：三按钮各加 `Margin="8,0,0,0"`（立刻见效）；随后定义间距 token（4/8/12/16/24）并全局对齐。

### V-04 字号 13 种无阶梯，含 7.2px 极端小字
- **位置**：`MainWindow.xaml`（grep 实测 FontSize 共 13 种：7.2 / 10 / 10.5 / 11 / 12 / 16 / 18 / 20 / 24 / 28 / 30 / 34 / 36，另有默认 14）；7.2px 与 10.5px 在收起侧栏的速度显示（第 254-265 行）
- **问题描述**：字号取值随手指定，无 type ramp；7.2px 低于所有平台的物理可读下限。
- **对标标准**：Fluent type ramp 仅 Caption 12 / Body 14 / Subtitle 20 / Title 28 / Display 40+ 等固定档位；M3 为 15 个 type role，最小 label-small 11sp；Apple HIG 最小 caption2 11pt。
- **优先级**：严重（7.2px 部分，同 A-03）/ 应优化（字阶系统）
- **优化建议**：收敛为 6 档 token（Caption 12、Body 14、BodyStrong 14/SemiBold、Subtitle 20、Title 28、Display 34）；紧凑侧栏速度只显示数值+缩短单位（"MB/s"→"M"），字号提升到 ≥11px。

### V-05 所有按钮同权重，无主操作强调
- **位置**：`MainWindow.xaml` 第 136-174 行（全局隐式 Button 样式）；受影响的主 CTA：CheckNowButton、SpeedRunButton、SavePolicyButton、DnsApplyButton 等
- **问题描述**：全应用按钮只有一种"深底描边"样式，主操作与次级操作视觉完全等权；用户在 9 个按钮的 Wi-Fi 工具栏（见 I-06）里无法一眼找到推荐动作。唯一的"主按钮"样式反而出现在风格失联的 AddWifiDialog 里（绿色）。
- **对标标准**：Fluent 有 AccentButton（强调填充）/ Standard / Subtle 三级；M3 有 Filled / Tonal / Outlined / Text 四级。每屏应有且仅有一个视觉主操作。
- **优先级**：应优化
- **优化建议**：每页选定一个主操作（状态页=立即检测、测速页=开始测速、策略页=保存），套用 WPF-UI `Appearance="Primary"` 的强调样式（自动使用系统 Accent 色），其余保持现状。

### V-06 卡片与背景几乎无层次分离（海拔方向还反了）
- **位置**：`MainWindow.xaml` 第 18-29 行 `SectionBorderStyle`（卡片 #11161A、描边 #2A3038）
- **问题描述**：实测卡片底色 vs Mica 背景对比 1.12:1，描边 vs 卡片 1.37:1——卡片轮廓靠"眯眼"才能确认；且卡片比窗口背景更**暗**，违反深色主题"海拔越高表面越亮"的通用规则（Fluent Layer 填充、M3 surface elevation 均为叠白提亮）。
- **对标标准**：Fluent 深色 Layer = 背景上叠 rgba(255,255,255,0.05+)；M3 dark elevation 每级叠加半透明白。
- **优先级**：应优化
- **优化建议**：卡片底色改为比背景亮一档（如 #262B31 方向，或直接用 WPF-UI `CardBackground` 资源），描边提到 ≥#3A424D；阴影可顺势移除（见 V-11）。

### V-07 圆角四种规格并存
- **位置**：卡片 8（第 22 行）、按钮 9（第 151 行）、徽章 10（第 402 行）、强调条 2（第 573 行）
- **问题描述**：8 与 9 的差异属于"看不出但不整齐"的典型 token 漂移。
- **对标标准**：Fluent 全局仅两档：控件 4px、卡片/浮层 8px。
- **优先级**：应优化
- **优化建议**：收敛为 4（小元素）/ 8（卡片与按钮）两档 token。

### V-08 导航图标尺寸混用、语义重复
- **位置**：`MainWindow.xaml` 第 194-204 行
- **问题描述**：图标混用 20/24 两种光学尺寸（`WifiSettings20`、`GlobeSearch20` vs `Home24`、`Apps24` 等）；Policy 与 Log 两个不同功能共用同一图标 `DocumentBulletList24`，破坏图标的可辨识性。
- **对标标准**：Fluent 侧边导航统一 24 号图标；HIG/M3 都要求"一图标一语义"。
- **优先级**：应优化
- **优化建议**：统一 24 号：Wi-Fi 用 `Wifi124`，DNS 用 `Globe24`；Policy 改 `Shield24` 或 `TaskListLtr24`，Log 保留 `DocumentBulletList24`。

### V-09 不透明度魔数散布
- **位置**：`MainWindow.xaml`（Opacity 0.18 / 0.7 / 0.72 / 0.74×10 / 0.78）
- **问题描述**：用不透明度模拟"次级文字"（DragHintText 0.72、HighRiskText 0.74、PolicyStatusText 0.78），与直接指定灰色的做法混用，同一层级文字在不同页面亮度不同。
- **对标标准**：Fluent/M3 次级文字统一走 token（TextFillColorSecondary），不鼓励逐处调 Opacity。
- **优先级**：应优化
- **优化建议**：删除文字类 Opacity，统一替换为 `TextSecondary` token。

### V-10 表格行悬停/选中使用三段渐变
- **位置**：`MainWindow.xaml` 第 97-121 行（GridRowStyle 的 IsMouseOver / IsSelected 触发器）
- **问题描述**：行状态用三段 LinearGradientBrush 渲染，视觉上产生轻微"鼓包"感，与 Fluent 扁平 state layer 风格不符，也让 6 个数据表的状态色难以与全局 token 联动。
- **对标标准**：Fluent ListView 行状态为纯色 SubtleFillColorSecondary(hover)/Tertiary(pressed)；M3 为 state layer 8%/12% 叠加。
- **优先级**：锦上添花
- **优化建议**：改纯色叠加（hover = 白 6%，selected = Accent 15% + 左侧 3px Accent 指示条）。

### V-11 每张卡片挂软件渲染阴影
- **位置**：`MainWindow.xaml` 第 24-28 行（SectionBorderStyle 的 DropShadowEffect，BlurRadius 18）
- **问题描述**：WPF 的 DropShadowEffect 走软件渲染，9 个面板的每张卡片都挂：深色背景上视觉收益≈0，滚动/重绘时白耗 CPU。
- **对标标准**：Fluent 深色模式基本不用投影，靠描边与填充分层。
- **优先级**：锦上添花
- **优化建议**：删除 Effect，配合 V-06 的"亮一档表面+描边"方案。

### V-12 硬编码文案比较判断状态
- **位置**：`MainWindow.xaml.cs` 第 1487 行（`HealthStateText.Text == "尚未检测" || ... == "Not checked"`）
- **问题描述**：用界面显示文本反推状态，切换语言时靠字符串枚举兜底，脆弱且已经出现中英硬编码。
- **优先级**：锦上添花
- **优化建议**：引入 `NetworkState? _lastState` 字段记录状态，渲染层按状态取文案。

---

## 3. 交互体验问题清单

### I-01 错误反馈黑洞：异常一律沉入日志
- **位置**：`MainWindow.xaml.cs` 第 1718-1728 行（`RunUiActionAsync` catch→`Log()`）、第 652-679 行（扫描失败仅 Log）、`App.xaml.cs` 第 77-95 行（全局异常写 crash.log 后 `e.Handled=true` 静默）
- **问题描述**：几乎所有操作失败只写入"日志页"和状态页的"最近事件"。用户在 Wi-Fi 页点扫描失败后，当前页面**没有任何可见变化**；应用级未处理异常更是零提示。错误从"就地可见"降级成"需要主动去别的页面找"。
- **对标标准**：M3 Snackbar、Fluent InfoBar：错误必须出现在用户当前注视的上下文中；Apple HIG 要求失败必须有即时、明确的反馈。
- **优先级**：严重
- **优化建议**：主窗口挂载全局 `ui:Snackbar`（WPF-UI 自带），`RunUiActionAsync` 的 catch 分支改为 Snackbar 弹错 + Log 双写；全局异常处理器追加一个"应用遇到问题，已写入日志"的对话框或 Snackbar。

### I-02 功能 bug：设置密码成功后必弹"请先选择 Wi-Fi"假错误
- **位置**：`MainWindow.xaml.cs` 第 188-201 行（`SetPassword_Click`）
- **问题描述**：第一个 if 分支（已选中配置 → 配置凭据）**缺少 `return`**，await 完成后代码落到末尾的 `SetWifiSelectionMessage(T("SelectWifiFirst"))`。结果：用户成功设置密码后，选择条上刚写好的成功状态立刻被"请先选择 Wi-Fi"错误文案覆盖。旁边的 `AddSelected_Click`（第 172-181 行）同结构却有 return，证明是笔误。
- **对标标准**：反馈必须与操作结果一致（任何规范的第一原则）。
- **优先级**：严重
- **优化建议**：第 193 行 `RunUiActionAsync` 后补 `return;`。一行修复。

### I-03 删除 Wi-Fi 配置无确认、不可撤销
- **位置**：`MainWindow.xaml.cs` 第 263-277 行（`Remove_Click` 直接 `_profiles.Remove` + `SaveSettings()`）
- **问题描述**：破坏性操作零防护：无确认对话框、无撤销。对比之下，重连 DHCP、改 DNS 都有确认——防护等级配错了对象（删数据比刷新网络更不可逆）。
- **对标标准**：HIG/M3：破坏性操作需要确认或提供 Undo；Fluent 推荐"删除 + Snackbar 撤销"模式。
- **优先级**：严重
- **优化建议**：最低成本：复用现有 `ConfirmActionAsync` 加确认；更优：直接删 + Snackbar 5 秒"撤销"按钮（配合 I-01 的 Snackbar 基建）。

### I-04 全应用没有一个加载指示器
- **位置**：全局（grep 实测 0 个 ProgressRing/ProgressBar）；典型场景：`RunQualityCheckAsync`（2172 行）、`RunSpeedTestAsync`（2276 行）、`ScanWifiAsync`（652 行）、`RefreshPublicIpInfoAsync`（2365 行）
- **问题描述**：所有异步任务的加载态 = 按钮禁用 + 一行文字变化。质量检测、网站测试等多秒级任务期间界面看起来像"死了"；测速有仪表盘动画属于特例。
- **对标标准**：Fluent：≥1 秒的操作必须有 ProgressRing（不确定时长）或 ProgressBar；M3 同理。
- **优先级**：应优化
- **优化建议**：运行中的按钮内容换成 `ui:ProgressRing (IsIndeterminate, 16px)` + "检测中…"；表格类结果区加载时显示居中 ProgressRing。

### I-05 用禁用的开关控件冒充"已保存"状态徽章
- **位置**：`MainWindow.xaml` 第 433-439 行（ProfileCredentialColumn 的 `ui:ToggleSwitch IsEnabled="False"`）
- **问题描述**：凭据是否已保存是**只读状态**，却用一个被禁用的交互控件展示：视觉上传达"这里本可以操作但被禁止"，禁用态还自带低对比度；读屏用户听到的是"开关，已禁用"而不是"凭据已保存"。
- **对标标准**：M3/Fluent 都明确：状态展示用图标/徽章，开关只用于可切换设置。
- **优先级**：应优化
- **优化建议**：改为图标列：已保存 = `Checkmark/LockClosed` 徽章 + AutomationProperties.Name="凭据已保存"；未保存 = 中性占位。

### I-06 Wi-Fi 页 9 个平级按钮堆成一排
- **位置**：`MainWindow.xaml` 第 370-380 行（扫描/添加选中/设置密码/导入密码/手动添加/上移/下移/移除/连接）
- **问题描述**：9 个纯文字按钮不分组、无图标、全部等权。哪些作用于"附近网络"、哪些作用于"优先列表"只能靠试（靠禁用态暗示）；上移/下移/移除这类行级操作放在页面顶部，与操作对象空间上脱节。
- **对标标准**：Fluent CommandBar：主命令 2-3 个 + 分组分隔符 + 溢出菜单；M3：行内操作放行尾（trailing actions），列表级操作放顶栏。
- **优先级**：应优化
- **优化建议**：顶栏保留"扫描 | 手动添加"两个列表级命令；"添加所选/设置密码/导入密码/连接"移到选中行的行内图标按钮或右键菜单；"上移/下移/移除"移到优先列表卡片头部并加图标（拖拽已支持，按钮做键盘替代）。

### I-07 成功反馈太轻，托盘通知未利用
- **位置**：`SavePolicy_Click`（295 行，成功=一行文字）、`ApplyDnsSettingsAsync`（1931 行同）、恢复成功事件（`Engine_ProfileRecoveryResult`）无任何前台提示
- **问题描述**：保存策略、应用 DNS 成功只改一行小字；对这个应用最核心的时刻——"断网后自动恢复成功"——用户如果最小化到托盘则完全无感知（NotifyIcon 的 balloon/toast 能力闲置）。
- **对标标准**：Fluent：设置类成功可轻量（InfoBar/Snackbar），但后台关键事件应发系统通知；Windows 11 支持 toast 通知。
- **优先级**：应优化
- **优化建议**：成功走 Snackbar（绿色 2 秒）；监控中的恢复成功/失败调用 `_notifyIcon.ShowBalloonTip`（已在托盘时）。

### I-08 部分运行时文案未本地化，中文界面夹英文
- **位置**：`MainWindow.xaml.cs` 第 289-291 行（"Connecting to X." / "Connection command sent"）、574-577 行（"Monitor started." / "Monitor stopped."）、745 行（"Wi-Fi profile already exists"）
- **问题描述**：这些字符串绕过 `_localizer.T()` 直写英文，中文用户的"最近事件"与日志里混杂英文句子，观感割裂（其余文案本地化做得很全，反衬明显）。
- **对标标准**：全量走本地化管线，无裸串。
- **优先级**：应优化
- **优化建议**：为这 5 处补 Localizer key；顺手在 code review 清单加"禁止 Log 裸英文串"。

### I-09 九个数据表中六个没有空状态
- **位置**：WebsiteGrid、IpInfoGrid、ReportGrid、DnsGrid、AdapterGrid、QualityProbeGrid（对比：NearbyGrid、ProfileGrid、RecentEventsList 三处**有**空状态，`UpdateEmptyStates` 第 1500 行）
- **问题描述**：首次进入网站测试/IP 信息/诊断报告页，看到的是一行表头 + MinHeight 320px 的大片空白，没有引导文案告诉用户"点上方按钮开始"。
- **对标标准**：M3/Fluent 空状态规范：说明性文字 + 指向主操作的引导。
- **优先级**：应优化
- **优化建议**：复用现有空状态模式，为 6 个表补"尚无数据，点击〔运行检测〕开始"文案（居中、TextSecondary 色）。

### I-10 策略表单非法输入被静默丢弃
- **位置**：`MainWindow.xaml.cs` 第 1301-1310 行（`ReadTimeSeconds` 解析失败静默 fallback 旧值）、第 1288 行（failures 同）
- **问题描述**：输入"abc"或"0"点保存，界面提示"已保存"，实际值悄悄回退为旧值——反馈与实际不一致。
- **对标标准**：M3/HIG 表单规范：非法输入要有内联错误文本 + 控件错误态描边，阻止保存或明确告知修正。
- **优先级**：锦上添花（因有 fallback 不会写坏数据）
- **优化建议**：改用 `ui:NumberBox`（自带范围约束），或解析失败时输入框标红 + 保存按钮提示。

### I-11 日志页仍是一个大 TextBox（导出已补，其余缺）
- **位置**：`MainWindow.xaml` LogPanel（日志导出按钮已于 M3 加入，本条已据当前工作区状态复核）
- **问题描述**：导出功能已具备，但仍无级别着色、无过滤/搜索、无清空按钮；且导出用的是 WinForms `FolderBrowserDialog`，与 Fluent 视觉脱节。对一个以诊断为卖点的工具，日志页可用性仍偏低。
- **对标标准**：现代诊断工具日志视图 = 虚拟化列表 + 级别徽章 + 筛选框（参考 Windows Terminal / VS Code 输出面板）。
- **优先级**：锦上添花
- **优化建议**：加"清空"按钮 + 错误行标色；导出对话框换 `Microsoft.Win32.SaveFileDialog`（原生新式样式）；理想态换虚拟化 ListView + 级别筛选。

### I-12 窗口尺寸与位置不记忆
- **位置**：`MainWindow.xaml` 第 7-11 行（每次启动固定 1180×760 居中）
- **优先级**：锦上添花
- **优化建议**：Closing 时把 Width/Height/Left/Top/WindowState 存入 AppSettings，启动时还原（注意多显示器越界校验）。

---

## 4. 现代化程度问题清单

### M-01 主题锁死深色，不跟随系统
- **位置**：`App.xaml` 第 8 行（`<ui:ThemesDictionary Theme="Dark" />`）；未使用 WPF-UI 的 `SystemThemeWatcher`（grep 确认 0 处）
- **问题描述**：2026 年主流桌面应用的底线是"跟随系统深浅色 + 可手动覆盖"。当前浅色模式用户得到一个纯黑应用；而 54 个硬编码色值意味着即使切了 ThemesDictionary 也会碎一地——主题能力被 V-02 连坐锁死。
- **对标标准**：Windows 11 应用默认跟随系统主题（Fluent）；macOS 同理（HIG）；M3 dynamic color 更进一步。
- **优先级**：严重
- **优化建议**：完成 V-02 token 化后，启动时调 `SystemThemeWatcher.Watch(this)`，为 token 提供 Light/Dark 两套取值；设置页加"跟随系统/浅色/深色"三选。

### M-02 全应用零动效
- **位置**：全局（grep 实测 0 个 Storyboard/Animation/Transition）；页面切换 = `Visibility` 硬切（`ShowPanel`，1553 行）；按钮 hover/pressed 颜色瞬变（136-174 行）
- **问题描述**：所有状态变化都是 0ms 硬切。这是"看起来像 2015 年 WinForms 皮肤"观感的最大单一来源——现代感的一半来自 150-300ms 的连贯过渡。
- **对标标准**：Fluent motion：页面进入 fade+slide 约 150ms、控件状态 83-167ms；M3 有完整 motion token（duration + easing）。
- **优先级**：应优化
- **优化建议**：三个低成本高收益点：① 面板切换加 150ms 淡入（Opacity 0→1 + TranslateY 8→0）；② 按钮模板颜色触发器换 VisualState + 100ms ColorAnimation；③ 状态圆点检测中加呼吸动画。

### M-03 NavigationView 内容模型被绕过
- **位置**：`MainWindow.xaml` 第 192-206 行（NavigationView 无 Content，仅当菜单用）+ 第 297-791 行（9 个面板叠在同一 Grid 手动切 Visibility）+ `NavigationItem_PreviewMouseLeftButtonUp` hack（491 行）
- **问题描述**：绕过控件自带的页面导航/过渡机制，用鼠标事件 hack 补选中项重复点击；所有页面常驻同一可视树（9 个面板的控件全部实例化并参与布局），既丢了免费的切换动画，也放大了内存与首帧成本。
- **对标标准**：WinUI/WPF-UI NavigationView 标准用法：`INavigationView.Navigate(pageType)` + Frame 内容区，自带过渡。
- **优先级**：应优化（涉及结构重构，建议排入 M4+ 里程碑）
- **优化建议**：把 9 个面板拆为 UserControl/Page，走 NavigationView 内容导航；短期先做 M-02① 的淡入过渡止血。

### M-04 测速仪表盘是固定尺寸画布
- **位置**：`MainWindow.xaml` 第 543-559 行（Canvas 420×270，刻度 0/10/50/100/250/500/1000 全部绝对坐标）
- **问题描述**：窗口拉大时仪表盘不缩放，孤零零居中；刻度文本是手摆的 Canvas 坐标，改字体/DPI 微调都会错位。
- **对标标准**：现代仪表类控件应矢量自适应（Viewbox 或按容器尺寸计算几何）。
- **优先级**：应优化
- **优化建议**：最低成本：Canvas 外套 `Viewbox` 等比缩放；理想态：几何按 ActualWidth 计算。

### M-05 固定像素列宽埋着本地化截断雷
- **位置**：策略表单标签列 180px（`MainWindow.xaml` 第 460 行）、设置页 120px（768 行）、各 DataGrid 大量 90-150px 定宽列
- **问题描述**：中文标签下刚好，切英文后 "Failures before recovery" 这类长标签面临截断/换行风险；表头如 "Credential"（88px 列）同理。
- **对标标准**：Fluent/M3 表单布局用 Auto + MaxWidth 弹性；本地化友好设计要求文案区可伸缩。
- **优先级**：应优化
- **优化建议**：标签列改 `Width="Auto"` + `MinWidth`；DataGrid 文本列尽量用 `*` 比例 + MinWidth。

### M-06 Mica 材质被不透明卡片盖没了
- **位置**：`MainWindow.xaml` 窗口启用 `WindowBackdropType="Mica"`（第 13 行），但内容区 90% 面积被不透明卡片覆盖
- **问题描述**：Mica 的价值在于大面积背景透出桌面色调，当前只剩边缘缝隙可见，材质感基本无效。
- **优先级**：锦上添花
- **优化建议**：配合 V-06：卡片用半透明表面（如 #14FFFFFF 叠加）让 Mica 隐约透出，即可获得"毛玻璃/材质感"的现代观感。

### M-07 侧栏速度迷你图无微交互
- **位置**：`MainWindow.xaml` 第 211-214 行（Polyline sparkline）
- **问题描述**：迷你趋势图不可悬停查看数值、无峰值标注，只能看个形状。
- **优先级**：锦上添花
- **优化建议**：加 ToolTip 显示近 28 秒峰值/均值；或点击跳转测速页。

---

## 5. 无障碍问题清单

> 亮点先说：28 组文本前景/背景组合实测**全部通过 WCAG AA**（最低 5.17:1，多数 >7:1），深色配色的可读性下了功夫。以下为缺口。

### A-01 表格键盘焦点指示被显式移除（WCAG 2.4.7 失败）
- **位置**：`MainWindow.xaml` 第 95 行（DataGridRow `FocusVisualStyle="{x:Null}"`）、第 126 行（DataGridCell 同）
- **问题描述**：9 个数据表里键盘用户 Tab/方向键移动时**完全看不到焦点在哪**（选中高亮仅在触发选择时出现，焦点≠选中）。这是硬性 WCAG 2.4.7 Focus Visible 违规。
- **对标标准**：WCAG 2.4.7（AA）；Fluent 焦点环规范：2px 高对比描边 + 1px 内衬。
- **优先级**：严重
- **优化建议**：删除两处 `x:Null`；或提供 Fluent 风格 FocusVisualStyle（2px #FFFFFF 圆角描边）。同时按钮的默认虚线焦点框建议升级为同款焦点环（见 A-05）。

### A-02 全项目零 AutomationProperties，读屏不可用
- **位置**：grep 实测 `MainWindow.xaml`、`MainWindow.xaml.cs`、`AddWifiDialog.cs` 均为 0 处
- **问题描述**：状态圆点（StatusDot）、速度迷你图、测速仪表盘对读屏完全静默；策略页 IntervalTextBox/FailuresTextBox 等输入框未用 LabeledBy 关联标签，Narrator 播报"编辑，空白"；I-05 的禁用开关播报语义错误；AddWifiDialog 的 "x" 关闭按钮播报为字母 x。
- **对标标准**：Windows App Accessibility 基线：所有交互控件有可编程名称；HIG/M3 同等要求。
- **优先级**：严重
- **优化建议**：分三步：① 输入框补 `AutomationProperties.LabeledBy`；② 状态类元素（圆点、仪表盘）补 `AutomationProperties.Name` 并随状态更新；③ 纯装饰元素（sparkline）标记 `AutomationProperties.AccessibilityView="Raw"` 排除。

### A-03 7.2px / 10.5px 字号低于一切平台下限
- **位置**：`MainWindow.xaml` 第 254-265 行（收起侧栏的速度单位 7.2px、数值 10.5px）
- **问题描述**：7.2px 在 100% 缩放下物理高度约 1.4mm，低视力用户完全不可读；对比度过关救不了尺寸不过关。
- **对标标准**：Fluent Caption 最小 12epx；M3 label-small 11sp；HIG caption2 11pt。
- **优先级**：严重
- **优化建议**：同 V-04：紧凑模式减少信息量（只保留数值），字号提到 ≥11px。

### A-04 交互组件描边对比不足（WCAG 1.4.11）
- **位置**：`AddWifiDialog.cs` 第 184 行（输入框描边 #4A5146/#20251F，实测 1.90:1）；`MainWindow.xaml` 卡片描边 1.37:1（第 20 行）
- **问题描述**：WCAG 1.4.11 要求交互组件边界与相邻色 ≥3:1。输入框是交互组件，1.90:1 不达标；卡片属非交互容器不算硬违规，但叠加 V-06 后界面"找不到边"。
- **对标标准**：WCAG 1.4.11（AA）；Fluent 输入框在深色下描边 ControlStrokeColorDefault 且聚焦时 Accent 下划线强化。
- **优先级**：应优化
- **优化建议**：输入框描边提亮到 ≥#6B7466（3:1），聚焦态换 Accent 色 2px。

### A-05 按钮焦点框停留在 WPF 默认虚线
- **位置**：`MainWindow.xaml` 第 144-173 行（自定义 Button 模板未定义焦点视觉，回落系统虚线框）
- **问题描述**：能用（未违规），但 1px 虚线在深色高密度界面上可见性差，与 Fluent 焦点环观感差距大。
- **优先级**：应优化
- **优化建议**：统一 FocusVisualStyle：2px 白色圆角描边、offset 2px，应用到按钮/开关/下拉。

### A-06 状态传达依赖颜色（有文字兜底，风险受控）
- **位置**：StatusDot（闲置灰 #9AA7B8 vs 检测中琥珀 #F59E0B，两色互差仅 1.14:1，色弱不可分）；HealthStateText 蓝/琥珀/红仅靠色相区分状态严重度
- **问题描述**：均有相邻文字兜底（"就绪/检测中"、状态名），不构成 WCAG 1.4.1 硬违规，但色觉障碍用户获得的冗余信号为零。
- **对标标准**：M3/Fluent：状态指示 = 颜色 + 图标/形状双通道。
- **优先级**：应优化
- **优化建议**：健康状态大字旁加状态图标（✓/⚠/✕ 对应 CheckmarkCircle/Warning/DismissCircle）；检测中圆点换成小号 ProgressRing（顺带解决 I-04 的感知问题）。

### A-07 无键盘快捷键与访问键
- **位置**：全局（无 InputBindings、无 access key）
- **问题描述**：高频操作（立即检测、扫描）无快捷键；好的一面：拖拽排序有"上移/下移"按钮做键盘替代，值得保持。
- **优先级**：应优化
- **优化建议**：F5=立即检测、Ctrl+R=扫描、Ctrl+,=设置；按钮文案加 access key（如 "立即检测(_C)"）。

### A-08 密码框无显示开关与 CapsLock 提示
- **位置**：`AddWifiDialog.cs` 第 15 行（原生 PasswordBox）
- **优先级**：锦上添花
- **优化建议**：换 WPF-UI `ui:PasswordBox`（自带小眼睛）；聚焦时检测 CapsLock 弹 tooltip。

### A-09 高对比度主题未适配
- **位置**：全局（54 个硬编码色不响应 Windows 高对比度主题）
- **优先级**：锦上添花
- **优化建议**：完成 V-02 token 化后，用 `SystemParameters.HighContrast` 检测并切换到系统色（SystemColors），或至少验证不出现"黑字黑底"。

---

## 6. Top 5 高优先级优化建议（按影响力排序）

### ① 建立设计 token 体系并解锁浅色主题（解决 V-02 / V-04 / V-07 / V-09 / M-01 / A-09）
**影响力**：这是所有视觉问题的"根"。54+12 个魔法色值合并为 ~16 个语义 token 后，主题切换、高对比度、后续所有视觉调整都从"全文件搜索替换"变成"改一处 token"。
- **改进前**：`Foreground="#AEB8C5"`、`Foreground="#9FB0C4"`、`Foreground="#91A2B8"` 三个页面三种"次级灰"；主题锁死深色，浅色系统用户看到突兀黑窗；对话框自成一套绿色皮肤。
- **改进后**：`Foreground="{DynamicResource TextSecondary}"` 一个 token 全局一致；`SystemThemeWatcher` 让应用随系统在 5 分钟内获得合格的浅色模式；对话框自动继承同一套皮肤。
- **落地顺序**：先建 ResourceDictionary（半天）→ 机械替换（一天）→ 提供 Light 取值 + Watch（半天）。

### ② 无障碍基线修复包（解决 A-01 / A-02 / A-03 + I-02）
**影响力**：三个"严重"级 WCAG 违规 + 一个必现假错误 bug，全部是低工作量高确定性的修复；这是从"个人工具"跨到"可发布软件"的门槛线。
- **改进前**：键盘用户在表格里按方向键——屏幕上没有任何变化，不知道焦点在哪；Narrator 用户听到"编辑，空白""开关，已禁用"；收起侧栏里 7.2px 的 "KB/s" 无人可读；设置密码成功后界面弹"请先选择 Wi-Fi"。
- **改进后**：焦点环清晰可见（删 2 处 `x:Null` + 1 个 FocusVisualStyle）；输入框播报"检测间隔，编辑，30"；紧凑速度显示 ≥11px；`SetPassword_Click` 补一行 `return`，反馈与结果一致。
- **工作量**：合计 ≤1 天，包含验证。

### ③ 反馈体系重构：Snackbar + ProgressRing + 删除确认（解决 I-01 / I-03 / I-04 / I-07）
**影响力**：直接决定"用起来放不放心"。错误就地可见、长任务有进度、删除可反悔，是用户信任的三根支柱。
- **改进前**：Wi-Fi 页点"扫描"抛异常→页面纹丝不动，错误躺在日志页；点"移除"→配置瞬间消失且不可找回；质量检测运行 5 秒，唯一线索是按钮变灰。
- **改进后**：失败时当前页面顶部滑出红色 Snackbar"扫描失败：无线服务未启动"；删除后 Snackbar 提供 5 秒"撤销"；运行中按钮内嵌 16px ProgressRing。全部基于 WPF-UI 现成控件（`ui:Snackbar`、`ui:ProgressRing`），无需引新依赖。

### ④ AddWifiDialog 并入主设计语言（解决 V-01，连带 A-04 / A-08）
**影响力**：这是普通用户最容易撞见的"质感崩塌点"——主界面颇为精致，一点"添加 Wi-Fi"却弹出配色、控件、标题栏全然不同的窗口，对整体质感的破坏远超其代码占比。
- **改进前**：绿色主按钮 #69D18B、字母 "x" 关闭钮、无边框自绘窗口、输入框描边 1.9:1。
- **改进后**：`ui:ContentDialog` 在主窗口内呈现（Fluent 标准模态），自动继承 token 与深浅主题，密码框换带显示开关的 `ui:PasswordBox`，描边/焦点态达标。
- **工作量**：约 1 天（对话框逻辑简单，重写成本低）。

### ⑤ 操作层级与主按钮强调（解决 V-03 / V-05 / I-06）
**影响力**：决定"第一眼专业度"与新用户上手速度：页头三个贴死的按钮是最先被看到的瑕疵；9 按钮工具栏是最先被用到的困惑。
- **改进前**：CheckNow/StartMonitor/Stop 三按钮 0px 间距连成一块；Wi-Fi 页 9 个等权文字按钮，找"连接"要逐个读。
- **改进后**：按钮统一 8px 间距；每页一个 Accent 主按钮（立即检测/开始测速/保存策略），视线落点明确；Wi-Fi 命令按对象分组：列表级（扫描、手动添加）留顶栏，行级（连接、设密码、移除）移入选中行操作区。
- **工作量**：间距与 Accent 半天；Wi-Fi 工具栏重构 1-2 天。

---

## 附录：量化数据一览

| 指标 | 实测值 | 大厂基准 |
|---|---|---|
| 不重复颜色值（主窗口 / 对话框） | 54 / 12（零重叠） | ~16 语义 token |
| 不重复字号 | 13 种（7.2–36px） | 5-7 档 type ramp |
| 圆角规格 | 4 种（2/8/9/10） | 2 档（4/8） |
| AutomationProperties | 0 处 | 全部交互控件覆盖 |
| 动画/过渡 | 0 处 | 页面/控件状态全覆盖 |
| 加载指示器 | 0 个 | ≥1s 任务必有 |
| 文本对比度 AA 达标率 | 28/28 组通过 ✅ | AA 全过 |
| 非文本(描边)对比 3:1 | 2 组不达标 | 全过 |
| 空状态覆盖 | 3/9 个列表 | 全覆盖 |
| 主题模式 | 仅深色（硬编码） | 跟随系统+手动 |

*对比度计算方法：WCAG 2.x 相对亮度公式；Mica 背景取近似值 #202020；半透明色按 alpha 合成后计算。*
