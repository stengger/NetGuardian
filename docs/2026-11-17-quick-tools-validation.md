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

本记录提交时尚未安装：现有界面运行中，必须先请用户手动从托盘退出，再进行备份、替换、哈希和启动核验。不得将源码或测试预览理解为本机软件已经更新。

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
- 当前仍等待用户手动从托盘退出界面；本机已安装软件尚未更新。实际安装、回滚保障和重新启动核验将在用户退出后继续。
