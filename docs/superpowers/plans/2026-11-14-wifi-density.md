# Wi-Fi 管理页密度优化实施计划

执行方式：当前会话分批执行。用户已回复“继续”，接受审查报告的推荐项，无需再次确认设计。

目标：默认 1180×758 两表各至少六行完整数据；保留业务逻辑、原功能入口和中文原文，新增批准的展示文字。

架构：显式引用的共用工具栏、分区头、滚动模板；Wi-Fi 页有界 Grid 分栏和摘要；UI 比例/折叠偏好独立存于 HKCU，不改业务配置。断点为窗口 960 DIP，标称最小尺寸保持 1040×660。

技术栈：.NET 8、WPF、WPF-UI 4.3.0、现有 xUnit；不新增依赖。

依据：本机 `artifacts/ui-audit/wifi-density-20261114/审查与布局方案.md` 和当前会话用户确认。

## 约束

- 中文说明、注释和提交。
- 不改守护、IPC、恢复/扫描/连接执行路径、公开类型名称、配置格式及业务模型。
- 新共用样式显式采用，其他页面默认值不变。
- 原低频入口进入更多；次要字段保留行详情与复制。
- 新增中文仅为“更多”“最后扫描：{0}”“恢复优先级：已启用 {0}/{1}”。

## 批次一：令牌与显式共用组件

文件：Themes/Tokens.Common.xaml、Buttons.xaml、Common.xaml；新增 Views/ListSectionHeader.xaml(.cs)、Themes/ScrollBars.xaml；桌面共用组件测试。

- [x] 增加实际工具按钮 32 DIP、忙碌内容不改变尺寸、分区头工具布局测试。
- [x] 构建并运行新测试，确认因资源/组件缺失失败。
- [x] 增加工具栏高度/间距/滚动令牌及显式工具栏样式；分区头提供 Heading/Tools/IsExpanded 槽，工具高 32；WifiSectionExpanderStyle 将展开按钮与工具分离。
- [x] 覆盖滚动模板保留 DataGrid 原生名称和行为，只有 Wi-Fi 样式采用；最小拇指、视觉变宽不改变数据宽度。
- [x] Release build、完整 test、组件截图和 git diff --check；审查相关 diff 并中文提交。

## 批次二：摘要、分栏、工具分组与 UI 偏好

文件：MainWindow.xaml Wi-Fi 部分、ResponsiveLayout.cs、MainWindow.CurrentWifi.cs、MainWindow.WifiScan.cs、MainWindow.xaml.cs 的呈现部分；新增 Presentation/WifiWorkspaceLayout.cs、WifiLayoutPreferences.cs；本地化新增展示字符串；对应布局测试。

- [x] 增加默认两表六行、宽度 959/960/961、摘要至多 64、空状态动作完整、无横向溢出测试，先记录原布局失败。
- [x] 摘要使用真实快照与恢复列表计数，选中原文留作提示和原有反馈；成功扫描时间属于 UI 状态。
- [x] 共用头接入；更多绑定原按钮可用性并调用原事件；主操作样式根据现有 ConnectButton.IsEnabled 切换，取消入口保留。
- [x] 有界分栏布局用同一 Grid 的三行/三列按方向切换；分隔条调整星号比例。折叠事件重新布局；测试夹具默认不读写真实注册表，业务窗口显式启用偏好。
- [x] 标称最小尺寸及其他页保持原样；列宽不足时只改变显示，原值保留行详情。
- [x] 更新旧布局断言为批准的新契约，保留连接、筛选、互斥、复制等行为测试。
- [x] Release build、完整 test、同尺寸截图、git diff --check；中文提交。

## 批次三：行状态、空状态、主题和细节

文件：共用 Wi-Fi 行模板、显式主题资源、UI 布局相关测试与 UiAuditCaptureTests.cs。

- [x] 增加每行仅一处信号图标、当前连接普通字重、单一主垂直滚动、间距令牌、虚拟化容器数量和长文字测试，先观察缺口。
- [x] 当前行改为已连接徽标；选中/悬停淡底+细左条；保留行详情和拖动入口。
- [x] 空状态在有限数据区内呈现，按钮和原文完整可达；说明移到可聚焦提示。
- [x] 低饱和语义和高对比系统色使用新增显式资源；键盘焦点复用现有环，点击/忙碌不改变布局尺寸。
- [x] 复跑三主题、各密度、0/5/32/60 和长名矩阵；补忙碌、错误、筛选空、折叠图；实际系统 DPI 验证边界单列。
- [x] Release build、完整 test、配色脚本、git diff --check；中文提交，保存前后证据与剩余事项。

## 本机更新

- [ ] 核实进程和 Program Files/dist 实际安装目录，隔离构建已验证提交。
- [ ] 若界面在运行，请用户手动托盘退出；不自动关闭，不重启无关守护。
- [ ] 备份和校验现有 UI 文件，只替换必要组件，保留配置，失败回滚。
- [ ] 核对文件哈希/构建标识，启动并核实更新目录与后台健康；如实记录不可验证环节。

每批标准命令：初始化项目 SDK 与项目 TEMP/TMP 后，`dotnet build NetGuardian.sln -c Release --no-restore`，`dotnet test NetGuardian.sln -c Release --no-build --no-restore --logger trx`，`git diff --check`。完整日志和生成图保留在本机忽略的 artifacts/ui-audit/wifi-density-20261114 下，不提交生成物。

验收记录：docs/ui-audits/2026-11-14-wifi-density.md。源码三批验证完成；实际系统 DPI/跨屏边界和安装状态单独记录，不把输出密度位图视为真实桌面验收。
