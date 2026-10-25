# 诊断工具框外操作区修正

## 问题与改动

用户截图显示：业务按钮虽然已放在页面标题下，但“快捷、测速、站点检测、IP 信息、诊断”标签仍被整页内容框包住；测速内部的开始、取消、复制也没有统一。

- `MainWindow.xaml`：诊断外层改为透明承载；标签导航留在内容表面外，只有所选标签正文进入统一卡片；增加测速外部操作宿主。
- `MainWindow.xaml.cs`、`PanelNavigationPresenter.cs`：标签切换共用操作组映射，包含测速；忽略正文内部控件冒泡的选择变化。
- `SpeedTestView.xaml/.cs`：开始、取消、复制合并为一组，移交外部宿主。沿用同一按钮、事件和状态更新；独立显示时仍能内部承载。
- `PanelNavigationTests.cs`：检查五个标签在 1180×760 中文、960×500 英文下，标签及操作与正文表面不重叠；验证测速操作移交后的事件、状态和恢复承载。
- `SmallWorkAreaTests.cs`：Wi-Fi 表面一致性改以仍使用公共卡片样式的 DNS 正文为参照，避免把透明的诊断导航承载误当内容卡片。

## 验证命令及证据

SDK 使用工作区忽略目录 `.dotnet-home/sdk/dotnet.exe` 的 8.0.426。构建遵循现有项目引用，按 Core、App、App.Tests 顺序运行：

```powershell
dotnet build <项目路径> --no-restore -p:MSBuildEnableWorkloadResolver=false -p:BuildProjectReferences=false -p:BuildInParallel=false
dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-build --no-restore
dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-build --no-restore
dotnet build src/NetGuardian.Core/NetGuardian.Core.csproj --configuration Release --no-restore -p:BuildProjectReferences=false -p:MSBuildEnableWorkloadResolver=false
dotnet build src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --no-restore -p:BuildProjectReferences=false -p:MSBuildEnableWorkloadResolver=false
dotnet publish src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --self-contained false --no-restore --output dist/NetGuardian-UI-Update-tools-outside -p:BuildProjectReferences=false -p:MSBuildEnableWorkloadResolver=false
git diff --check -- src tests
```

完整日志保留于工作区忽略目录：

- `.dotnet-home/diagnostic-outside-red.log`：修改前两项布局回归失败，退出 1。中文标签边界 `262,185,42,36`，原表面 `245,168,911,568`，确认标签实际在框内；英文小窗口同样失败。
- `.dotnet-home/diagnostic-outside-green.log`：首次针对性验证 66/66 通过，退出 0。
- `.dotnet-home/diagnostic-outside-full.log`：154 通过、1 失败，退出 1。新增承载恢复测试在布局更新前检查可视树，`Assert.Contains() Failure: Item not found in collection`，`PanelNavigationTests.cs:112`；已在变更承载后刷新布局，未删除断言或跳过用例。
- `.dotnet-home/diagnostic-outside-full-final.log`：修正后完整桌面套件 155/155 通过、无跳过，退出 0。
- `.dotnet-home/diagnostic-outside-core.log`：沙箱内 211 通过、11 失败，退出 1。配置及心跳原子替换出现 `System.UnauthorizedAccessException : Access to the path is denied.`，三项恢复预算测试出现 `Expected: True / Actual: False`；完整堆栈及全部失败名保留于日志。
- `.dotnet-home/diagnostic-outside-core-unsandboxed.log`：相同核心套件放宽沙箱后 222/222，通过、无跳过，退出 0。没有修改持久化代码或测试以回避权限问题。
- `.dotnet-home/diagnostic-outside-publish.log`：Release 界面发布通过，退出 0；此前 Debug/Release App 构建均为 0 错误，保留现有两条 WFAC010 高 DPI manifest 建议。

## 验收边界

布局预览位于 `.dotnet-home/validation/tools-outside/`，由加载真实 XAML 的测试窗口生成，不能当作已安装正式业务进程截图。人工核看中文快捷页及英文小窗口测速页，确认标签与操作在正文框外。

本次不修改后台守护、网络配置及用户原有 `docs/2026-10-18-validation.md`。跨显示器 DPI、实际读屏和完整产品级目标不因本次布局回归通过而自动完成。
