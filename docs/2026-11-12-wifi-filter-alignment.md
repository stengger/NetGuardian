# Wi-Fi 筛选输入框对齐修复

用户截图指出搜索框观感异常。桌面夹具复现：32 DIP 高的搜索框内，19 DIP 高的占位文字及输入内容从顶部 1 DIP 开始；排序文字也偏上，网络数量徽标被横向容器拉伸到整行高度。

根因是紧凑筛选行将垂直内边距设为 0，却保留库模板的顶部内容对齐。之前审查只验证了整体视口和控件高度，遗漏了模板内部文字的实际位置。

## 修改

- `Themes/Filters.xaml`：共用紧凑输入框、下拉框样式显式居中内容，继续使用 WPF-UI 模板及现有尺寸令牌；数量徽标按内容高度居中。
- `Themes/Common.xaml`：合并筛选资源字典。
- `MainWindow.xaml`：搜索框、排序框和两处数量徽标使用共用样式。
- `WifiFilterLayoutTests.cs`：检查占位文字、输入内容、排序内容和徽标的实际布局边界；覆盖三主题、最小及默认窗口、36 条网络、长输入和禁用状态，验证状态切换不改变行高。

没有修改中文文案、功能入口、网络逻辑、配置、依赖或控件固定高度。

## 验证

输出根目录：`artifacts/ui-audit/`。

- 修复前专项：2 个用例失败，明确复现预期 Center、实际 Top；日志 `filter-red.log`，退出码 1。
- 修复后专项：6/6 通过；日志 `filter-green.log`，退出码 0。
- 修复前后图：`filter-comparison.png`（上为修复前，下为修复后）；完整图位于 `filter-before/` 和 `filter-after/`。
- 实测修复后占位内容 y=6、高 19，输入框高 32，上下留白差 1 DIP，符合像素取整约束。
- `dotnet build NetGuardian.sln --configuration Release --no-restore` 成功，0 错误、2 条既有 WFAC010 警告；`dotnet test NetGuardian.sln --configuration Release --no-build --no-restore` 核心 305/305、桌面 330/330 通过，无跳过，退出码 0。完整日志为 `filter-build.log`、`filter-full-test.log`。

本轮截图为 WPF 布局夹具输出，未作为真实系统 DPI 或读屏器验证。程序仍在运行，本机替换需按项目规则由用户手动从托盘退出后执行。
