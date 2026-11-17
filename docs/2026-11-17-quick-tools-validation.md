# 快捷诊断风险提示与空态说明验证

日期：2026-11-17（北京时间）。

## 修改范围

- `MainWindow.xaml`：快捷页没有结果时显示三项按钮用途说明；结果出现后通过文本绑定自动退出布局，清空结果后重新显示。
- `MainWindow.xaml.cs`：沿用现有语言刷新流程更新说明，不修改操作结果。
- `Localizer.cs`：中文和英文同时修正过时的“后续版本再启用”文案，说明高风险恢复已在恢复策略中可启用、默认关闭、满足恢复条件自动执行，快捷页没有手动重启入口。
- `QuickToolsLayoutTests.cs`：新增 8 项真实 XAML 布局及结果状态回归，覆盖中英文、960×500 小窗口、暗色/亮色/高对比和结果区域的显隐变化。
- 不新增手动重启按钮，不修改恢复条件、用户配置、监控或网卡控制逻辑。

## 命令和结果

SDK：`E:\Environment\dotnet-sdk-8.0\dotnet.exe`，版本 8.0.422。通过 `NETGUARDIAN_DOTNET` 指定并沿用 `scripts/initialize-build.ps1`。

1. 新增回归先执行：
   `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~QuickToolsLayoutTests --logger trx;LogFileName=quick-tools-red.trx --results-directory .dotnet-home/validation/quick-tools-20261117/test-results --verbosity minimal`
   - 原代码 8 项全部因缺少 `QuickToolsHintText` 失败，退出码 1；预期错误：`Assert.IsType() Failure: Value is null`。
2. 修改后的定向回归：
   `dotnet test NetGuardian.sln --no-restore --configuration Release --filter "FullyQualifiedName~QuickToolsLayoutTests|FullyQualifiedName~PanelNavigationTests|FullyQualifiedName~PolicyRiskLayoutTests|FullyQualifiedName~LocalizationAndUrlTests|FullyQualifiedName~HighRisk" --logger trx;LogFileName=quick-tools-focused.trx --results-directory .dotnet-home/validation/quick-tools-20261117/test-results --verbosity minimal`
   - Core 17/17、WPF 25/25；0 失败、0 跳过，退出码 0。
3. 未过滤的完整回归：
   `dotnet test NetGuardian.sln --no-restore --configuration Release --logger trx --results-directory .dotnet-home/validation/quick-tools-20261117/full-test-results --verbosity minimal`
   - Core 311/311、WPF 453/453；0 失败、0 跳过，退出码 0。
   - 此前其他任务的动画测试失败未在本轮复现；没有修改或跳过那些测试，不据此撤销历史失败记录。
4. 审查后修正测试名称并增加目标语言字面前缀断言，再执行快捷页最终回归：`dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~QuickToolsLayoutTests --logger trx;LogFileName=quick-tools-final.trx --results-directory .dotnet-home/validation/quick-tools-20261117/test-results --verbosity minimal`：8/8 通过，0 失败、0 跳过，退出码 0。
5. `git diff --check`：通过，退出码 0。保留仓库现有行尾风格，没有全文件格式化。

首次沙箱内编译被临时目录权限阻止，退出码 1，完整错误如下；随后按正常审批在沙箱外重跑，没有更改测试断言：

```text
error MSB3883: 意外的异常:
error : Access to the path 'C:\Users\Qiu\AppData\Local\Temp\MSBuildTemp\tmp5f04e2c1a4bb41cd907d13d18e900948.rsp' is denied.
```

编译保留已有 `WFAC010` 高 DPI 配置警告，本次没有修改 DPI 设置。

## 视觉核验与限制

预览由既有离屏 WPF 测试夹具加载真实 XAML 生成，不是已安装业务进程截图。人工检查中文暗色 1180×760、英文亮色 960×500；用途说明位于警告下方、没有裁切；另检查中文高对比 960×500，并生成中英文暗色/亮色和结果状态预览。没有实际改变显示器 DPI 或系统高对比设置。测试夹具手动设置本地化文案，验证目标语言文本与结果显隐，不执行真实主窗口的 `ApplyLanguage`；真实语言接线由代码审查核对，不宣称完成真实语言切换验收。

证据保存在 Git 忽略目录：

- `.dotnet-home/quick-tools-red-20261117.log`：预期失败的完整错误和调用栈。
- `.dotnet-home/quick-tools-focused-20261117.log`：定向回归与编译输出。
- `.dotnet-home/validation/quick-tools-20261117/final-quick-tools-tests.log`：最终 8 项回归输出。
- `.dotnet-home/validation/quick-tools-20261117/full-tests.log`：完整回归输出。
- `.dotnet-home/validation/quick-tools-20261117/full-test-results/`：两组完整 TRX。
- `.dotnet-home/validation/quick-tools-20261117/previews/`：界面预览。

## 本机更新安排

保持版本 1.1.6，使用本次提交的构建标识区分界面修正，不创建 GitHub Release。从已验证提交隔离导出并构建；核实后只替换 `C:\Program Files\NetGuardian` 与 `dist\NetGuardian` 的必要界面和本地化组件。现有后台守护、配置和网络保持原状。

修正提交时尚未安装：当时界面运行中，必须先请用户手动从托盘退出，再进行备份、替换、哈希和启动核验。不得将源码或测试预览理解为本机软件已经更新。

## 隔离构建与安装前检查

- 已验证源码提交：`3027e591b76c1460ea898c905df73f0ed5c6123f`（修正快捷诊断风险提示并补充空态操作说明）。通过 `git archive --format=tar --output=.dotnet-home/validation/quick-tools-20261117/source-3027e591.tar 3027e591b76c1460ea898c905df73f0ed5c6123f` 导出并解压到独立 `release-source`；不使用共享工作区继续构建。
- 构建命令：`build-local-package.ps1 -SourceCommit 3027e591b76c1460ea898c905df73f0ed5c6123f`，内部执行 `dotnet restore <隔离目录>/src/NetGuardian.App/NetGuardian.App.csproj --configfile <隔离目录>/NuGet.Config --verbosity minimal` 和 `dotnet publish <隔离目录>/src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --self-contained false --no-restore --output <验证目录>/ui-package -p:SourceRevisionId=3027e591b76c1460ea898c905df73f0ed5c6123f`。
- 首次沙箱恢复依赖失败，退出码 1。完整 NuGet 错误如下；重复错误行也保存在 `local-package-build-sandbox-error.log`。随后按正常审批重跑，依赖恢复和发布均成功，退出码 0，没有改变依赖版本或测试。

```text
E:\Environment\dotnet-sdk-8.0\sdk\8.0.422\NuGet.targets(174,5): error : 由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。 [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\quick-tools-20261117\release-source\src\NetGuardian.App\NetGuardian.App.csproj]
E:\Environment\dotnet-sdk-8.0\sdk\8.0.422\NuGet.targets(174,5): error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied. [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\quick-tools-20261117\release-source\src\NetGuardian.App\NetGuardian.App.csproj]
E:\Environment\dotnet-sdk-8.0\sdk\8.0.422\NuGet.targets(174,5): error :   由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。 [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\quick-tools-20261117\release-source\src\NetGuardian.App\NetGuardian.App.csproj]
E:\Environment\dotnet-sdk-8.0\sdk\8.0.422\NuGet.targets(174,5): error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied. [C:\Users\Qiu\Documents\Net\.dotnet-home\validation\quick-tools-20261117\release-source\src\NetGuardian.App\NetGuardian.App.csproj]
恢复依赖失败，退出码=1
```

- 发布的 `NetGuardian.dll` 与 `NetGuardian.Core.dll` 产品版本均为 `1.1.6+3027e591b76c1460ea898c905df73f0ed5c6123f`。七个根组件摘要检查通过，不包含自带运行时；两处目标已有第三方 DLL 与发布包的哈希一致。
- 部署脚本通过 PowerShell 语法解析。通过 `Start-Process pwsh.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile','-File',<验证目录>/deploy-ui.ps1 -Wait` 执行正常 UAC 管理员只读预检，未传 `-Apply`，退出码 0，没有替换文件、退出界面或停止守护。
- 预检核实当前界面 PID 125012 路径为 `C:\Program Files\NetGuardian\NetGuardian.exe`；后台 PID 77480 路径为 `C:\Program Files\NetGuardian\guardian\NetGuardian.Guardian.exe`。
- 两处目标各计划更新五个有差异的界面根文件及 `UIInstalledBuild.json`；依赖、`.deps.json`、`.runtimeconfig.json` 不需替换。预检 JSON 的备份路径只是计划，尚未创建安装备份或执行安装。
- 新增证据均在 `.dotnet-home/validation/quick-tools-20261117/`：`local-package-build-sandbox-error.log`、`local-package-build.log`、`publish-result.json`、`ui-deployment-preflight.json`。
- 安装前阶段仍等待用户手动从托盘退出界面；当时已安装软件尚未更新。用户退出后的实际安装和启动核验见下一节。

## 用户退出后的安装与启动核验（已完成）

用户确认“已退出”后，于 2026-11-17（北京时间）通过正常 UAC 执行以下步骤；没有使用界面自动化退出或强制结束软件进程。

1. 重新核对发布包七个根组件与 `publish-result.json` 的 SHA256：全部一致。
2. 以管理员执行 `deploy-ui.ps1 -Apply`：退出码 0，安装结果 `Success=true`。脚本再次确认界面不存在，核实后台路径，备份原文件和配置后才替换；每次复制后及最终均检查哈希，保留异常回滚流程，本次没有触发回滚。
   - 更新目标：`C:\Program Files\NetGuardian` 和 `C:\Users\Qiu\Documents\Net\dist\NetGuardian`。
   - 共更新 12 项：两处各五个有差异的界面根文件及 `UIInstalledBuild.json`；每处七个产品根组件的最终哈希检查通过。没有替换第三方依赖、运行时、后台目录或更新脚本。
   - 备份：`C:\Users\Qiu\Documents\Net\.dotnet-home\validation\quick-tools-20261117\ui-backup-20261117-104754`，含两处被替换文件及原配置，备份哈希已验证。
3. 管理员执行 `verify-startup.ps1`：验证两处安装标识、七个组件哈希和产品版本，然后从 `C:\Program Files\NetGuardian\NetGuardian.exe` 重新打开界面。
   - 首次启动核验已写入 `Success=true`、`Started=true`。外层 `Start-Process -Wait` 仍等待其界面子进程关闭；这不是软件启动失败。只结束准确核实路径及命令行的外层 PowerShell 等待器 PID 125004，终端等待会话退出码为 -1、没有错误输出；没有结束界面或守护。
   - 首次读取结果时，沙箱内额外 CIM 查询失败，退出码 1，完整错误：`Get-CimInstance: 无法从客户端中访问 CIM 资源。` 按正常审批重跑只读查询，退出码 0；不据此判断软件或运行时不可用。
   - 最终改用 `Start-Process ... -PassThru` 后调用 `$process.WaitForExit()`，只等待核验助手；再次运行核验时不再启动界面。最终核验退出码 0、`Success=true`，核验时间为 `2026-11-17T10:58:49.2269982+08:00`。
   - 界面 PID 69936，路径 `C:\Program Files\NetGuardian\NetGuardian.exe`，窗口标题 `NetGuardian`、句柄 46208380、`Responding=true`。
4. 两处界面及根目录 Core 产品版本均为 `1.1.6+3027e591b76c1460ea898c905df73f0ed5c6123f`。两处 `NetGuardian.dll` SHA256 均为 `D1132264587BBD697BEAB2FC7E53B8876C753628328E2A7A8F91912388AE3616`。
5. 安装及启动核验确认配置与更新前备份哈希一致，后台文件未变；后台仍为 PID 77480、创建时间 `2026-11-16T17:47:17.490942+08:00`，路径 `C:\Program Files\NetGuardian\guardian\NetGuardian.Guardian.exe`。本次没有停止或重启守护，也没有主动执行切网或网络重置。
6. 实际窗口自动化读取报告目标窗口完整性级别更高，只能获取窗口节点；截图没有呈现目标界面，因此不作为真实快捷页的视觉验收证据。保留此前离屏布局验证，不宣称已完成真实界面文案、实机语言切换或 DPI 验收。

本轮只补充安装记录，没有再次修改产品代码；沿用本次完整回归 Core 311/311、WPF 453/453，最终快捷页 8/8 的结果。`git diff --check` 再次通过。新增证据：同一验证目录中的 `ui-deployment-result.json`、`ui-initial-startup-verification.json`、`ui-startup-verification.json`；备份和日志均保持 Git 忽略，不提交用户配置或构建产物。

## 快捷页实机显示补验（2026-11-17 11:11，北京时间）

用户手动切换到“诊断工具 → 快捷”并确认后，读取原安装进程的窗口（句柄 46208380、标题 NetGuardian），通过窗口公开的 Raise 操作置前后取得真实目标截图。最初被聊天窗口遮挡的截图不作为证据；以下结论来自置前后的快捷页截图。

- 中文暗色界面中，“立即检测”“扫描”“打开网络设置”三个按钮可见；“快捷”标签处于选中状态。
- 黄色风险提示完整显示新文案，明确恢复策略可启用、默认关闭、满足恢复条件后自动执行，以及此页不提供手动重启。提示在当前窗口中自动折行，没有可见裁切或与正文重叠。
- 正文显示“扫描完成，发现 32 个网络”。这是用户切换后的现有结果，本轮没有自动触发扫描，也没有独立复核网络数量或扫描过程。
- 已有结果时不显示用途说明，与设计及此前显隐测试一致。当前截图不能证明首次打开无结果时的用途说明显示，因此实机空态仍未直接验收；该状态沿用先前 8 项快捷页自动化测试与离屏预览证据。
- 自动化控件树仍只有窗口节点；本轮没有切换语言、改变主题、窗口尺寸或显示器 DPI，不扩大为这些场景的实机验收。

本轮仅补充验证文档，不修改产品代码、配置或安装文件，不需重新构建或安装。文档检查执行 `git diff --check`，退出码 0；已有完整回归与本机部署结论保持不变。
