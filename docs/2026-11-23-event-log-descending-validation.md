# 事件日志时间倒序验证（2026-11-23）

## 需求与实现

- 事件日志默认按真实时间 `UiLogEntry.Time` 倒序，最新记录在最上方；时间列的排序标识与实际顺序一致，点击时间列也按真实时间而非显示文本排序。
- 在展示层配置排序，保留 `EventLogStore` 原有升序存储、七天范围、容量及关键事件优先保留规则。
- 导入历史、实时新增和重新勾选“跟随最新”均定位筛选结果中的最新时间，不再把最后一行当成最新记录；手动改变排序也不会选错最新记录。
- 向下滚轮、↓、PageDown、End 表示阅读旧记录并暂停跟随；向上返回最新不自动关闭跟随。垂直滚动条操作仍暂停跟随，程序滚动不暂停。
- 版本从 1.2.1 递增为 1.2.2。本次仅涉及日志界面，不修改或重启网络恢复守护。

## 修改文件

- `src/NetGuardian.App/Presentation/EventLogPresentation.cs`：统一建立倒序视图并查找可见最新记录。
- `src/NetGuardian.App/MainWindow.Log.cs`：接入统一排序和跟随目标，移除旧键盘处理。
- `src/NetGuardian.App/MainWindow.xaml`：时间列使用真实时间排序，键盘事件交由阅读行为组件管理。
- `src/NetGuardian.App/Presentation/LogReadingBehavior.cs`：调整阅读旧记录的滚轮及键盘方向。
- `tests/NetGuardian.App.Tests/EventLogPresentationTests.cs`：覆盖历史、实时新增、筛选、排序标识、存储顺序、空列表及手动排序下的最新目标。
- `tests/NetGuardian.App.Tests/LogReadingBehaviorTests.cs`：覆盖倒序列表的滚轮、六个导航键和原有滚动条行为。
- `Directory.Build.props`：递增正式版本号。

## 验证命令与结果

构建前设置：

```powershell
$env:NETGUARDIAN_DOTNET = 'E:\Environment\dotnet-sdk-8.0\dotnet.exe'
```

1. `E:\Environment\dotnet-sdk-8.0\dotnet.exe --list-sdks`：识别 SDK 8.0.422，退出码 0。最初未设置 SDK 路径时，构建脚本提示 `dotnet SDK not found. Install .NET 8 SDK, add dotnet to PATH, or set NETGUARDIAN_DOTNET to the SDK location.`，退出码 1；这是 SDK 不在 PATH，不是 SDK 不可用。
2. 回归红灯：抽出原有展示行为后运行下面的测试命令，6 失败、9 通过、0 跳过，退出码 1。顺序实际为“最旧记录、中间记录、最新记录”，与要求相反；向下阅读也未暂停跟随。完整断言、堆栈与输出：`.dotnet-home/log-order-red-behavior.log`。
3. 修复后运行：

```powershell
. .\scripts\initialize-build.ps1
& $dotnet test tests\NetGuardian.App.Tests\NetGuardian.App.Tests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~EventLogPresentationTests|FullyQualifiedName~LogReadingBehaviorTests' --verbosity minimal
```

15 通过、0 失败、0 跳过，退出码 0；完整输出：`.dotnet-home/log-order-green.log`。

4. 全量验证和自包含发布：

```powershell
.\scripts\publish.ps1 -OutputDirectory '.dotnet-home\release-1.2.2' -Runtime win-x64 -SelfContained
```

核心测试 401 通过，桌面测试 518 通过，共 919 项；0 失败、0 跳过。界面与守护均成功发布，退出码 0。完整输出：`.dotnet-home/log-order-publish.log`。

首次沙箱内执行还原时，NuGet 读取用户配置失败：`由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。`，并报告 `Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied.`，退出码 1。通过环境审批后原命令重跑成功，没有修改或跳过测试。

现有编译警告 WFAC010：`从 C:\Users\Qiu\Documents\Net\src\NetGuardian.App\app.manifest 中删除高 DPI 设置，并通过 Application.SetHighDpiMode API 或“ApplicationHighDpiMode”项目属性进行配置`。与本次排序修复无关，未扩展修改 DPI 配置。

5. `.\tests\Test-InstallerScripts.ps1`：全部通过，退出码 0；系统调用为测试替身，不代表执行了实际安装。完整输出：`.dotnet-home/log-order-installer-check.log`。
6. `git diff --check`：通过，退出码 0。

## 本机更新状态

- 待部署发布目录：`C:\Users\Qiu\Documents\Net\.dotnet-home\release-1.2.2`，win-x64 自包含。
- 检查时 `C:\Program Files\NetGuardian\NetGuardian.exe` 文件版本为 1.2.1.0；项目启动器使用的 `C:\Users\Qiu\Documents\Net\dist\NetGuardian\NetGuardian.exe` 为 1.2.0.0。
- 发现界面进程 PID 67736 和守护进程 PID 86364；当前查询无法取得这两个进程的可执行路径。未终止任何进程，未替换安装文件，未改动配置，未重启守护，未触发切网。
- 已请用户手动从托盘退出界面；收到退出确认后，继续核实启动路径、备份并校验文件，仅更新必要界面组件和项目启动器目录，再启动并核验。当前不能宣称正在使用的软件已经更新。