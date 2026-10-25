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

## 安装续接排查

用户允许管理员更新后，首次安装退出 1，尚未写入安装文件。Windows PowerShell 5.1 按本地代码页读取了无 BOM 的 UTF-8 脚本，导致中文字符乱码及解析错误：`install.ps1:16 字符:85` 的 `UnexpectedToken`，另有第 34、55、68、77 行解析错误。完整错误与堆栈保留于 `.dotnet-home/validation/tools-outside/install-attempt1.log` 和 `install-result-attempt1.json`。

只将忽略目录下的 `install.ps1`、`run-install.ps1` 写为带 BOM 的 UTF-8，未修改产品代码。使用 `powershell.exe -NoProfile -Command` 做验证时，语法检查通过，但执行试运行受执行策略限制，退出 1，`PSSecurityException / AuthorizationManager check failed / UnauthorizedAccess`。随后与实际更新启动方式保持一致，使用 `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command`，同时执行 `Parser.ParseFile` 语法检查及 `install.ps1 -WhatIf`，退出 0。该执行策略仅作用于本次进程，未修改系统执行策略。

重试继续使用原更新范围：11 个界面根目录文件及 `UIInstalledBuild.json`，备份并逐文件校验，持有界面单实例互斥期间才写入，不修改 `guardian/` 或配置。

编码修正后管理员重试已执行，但退出 1：`install.ps1:23`，`界面仍在运行或另一更新正在执行；没有替换文件。请从托盘退出后重试。`。该次完整错误已保存到 `install-attempt2.log`、`install-result-attempt2.json`。沙箱内 CIM 查询被拒绝（`无法从客户端中访问 CIM 资源。`），打开全局互斥也报 `Access to the path 'Global\NetGuardian.UI' is denied.`；放宽沙箱后只读查询确认界面进程 ID 为 `63832`。未强制终止进程，已请用户从托盘退出。

此时实际安装清单仍为 `8b19a4afe6929585c8e73d4ec1337e61ebff5069`，本次目标为 `3f7c6a8654b36aea4754e63a291cded1e8124dae`；不能把管理员授权通过当作更新成功。

## 安装完成核验

用户确认从托盘退出后，放宽沙箱的 CIM 检查确认无界面进程，再启动原范围的管理员更新。更新脚本退出 0，`install-result.json` 的 `Success=true`，`install.log` 记录 11 个文件验证成功。未再修改产品代码，没有重复运行与此编码和安装操作无关的产品测试。

2026-10-25 14:08（Asia/Shanghai）独立读取安装目录，逐一与发布包计算 SHA-256：

- 安装提交与目标提交均为 `3f7c6a8654b36aea4754e63a291cded1e8124dae`。
- 检查 11 个界面文件，哈希不匹配数量为 0。
- 安装清单文件与发布包清单的哈希一致。
- 核验命令退出 0，结果保存在 `.dotnet-home/validation/tools-outside/installed-verification.json`。
- 更新前文件备份位于 `.dotnet-home/validation/tools-outside/installed-backup/`，仅用于本次更新回退，不是另一套正在运行的应用。

此次已确认安装文件替换完成；未把文件核验当作新版正式进程的实机布局验收，后者需重新打开应用检查。
