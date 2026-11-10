# 应用内更新实施计划

> 在当前会话使用 superpowers:executing-plans 逐项执行；设计已经用户确认，不重复请求同一方案批准。

**目标：** 在 NetGuardian 设置页完成检查、下载、校验、安装与重启。

**架构：** 核心 HTTP 服务负责公开 Release 和内容验证，桌面会话负责可取消交互，独立 PowerShell 进程负责退出后的安装与恢复。

**技术栈：** .NET 8、C#、WPF-UI 4.3.0、PowerShell、Inno Setup 7。

**设计：** `docs/superpowers/specs/2026-11-10-in-app-update-design.md`。

## 全局约束

公开更新源固定为 stengger/NetGuardian。不新增依赖，不修改原配置、网络恢复算法和 IPC。新版本为 1.1.0。下载阶段可取消，安装前明确确认；任务和进程只操作已核对的当前安装目录。

## 第一步：版本及下载服务

文件：新增 `Directory.Build.props`、`src/NetGuardian.Core/Updates/UpdateRelease.cs`、`GitHubUpdateService.cs`，新增 `tests/NetGuardian.Tests/UpdateServiceTests.cs`。

接口：`IUpdateService.CheckAsync(Version, CancellationToken)` 返回较新的 `UpdateRelease?`；`DownloadAsync(UpdateRelease, string, IProgress<UpdateProgress>?, CancellationToken)` 返回已校验的 `DownloadedUpdate`。

- [ ] 先写 HTTP 替身回归：v1.0.1 不提示升级；v1.2.0 正式版本识别；非本仓库 URL、缺少校验文件、错误 SHA256、取消下载均不能生成完整安装包。
- [ ] 运行定向测试，确认缺少更新服务导致失败。
- [ ] 实现固定来源、严格附件命名、受限 JSON/校验内容长度、流式下载、临时文件清理与原子落盘。
- [ ] 统一版本 1.1.0；构建与全量测试通过后提交。

示例测试契约：

```csharp
var release = await service.CheckAsync(new Version(1, 0, 1), default);
Assert.Equal(new Version(1, 2, 0), release!.Version);
await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release, cache, null, default));
Assert.Empty(Directory.GetFiles(cache));
```

## 第二步：安装协调与恢复

文件：新增 `scripts/update-netguardian.ps1`、`tests/Test-UpdateScripts.ps1`、`src/NetGuardian.App/Updates/UpdateInstallLauncher.cs` 与 `UpdateStorage.cs`；修改 `scripts/package-support.ps1`、`scripts/publish.ps1`、`scripts/build-installer.ps1` 和打包脚本测试。

接口：`UpdateInstallLauncher.PrepareAsync(DownloadedUpdate, CancellationToken)` 准备受保护的安装请求、启动独立协调进程并等待准备信号；只有成功返回后界面退出。PowerShell 接收 `-RequestFile`，请求包含已验证安装包路径、SHA256、目标版本、当前界面 PID/启动时间和安装目录。

- [ ] 先用隔离目录及假系统任务写成功、外来任务拒绝、守护停止超时和安装失败恢复测试。
- [ ] 运行脚本测试观察缺少协调流程时失败。
- [ ] 实现受保护缓存、目录/重解析点校验、任务 XML 和卸载信息快照、文件备份、准备握手、正常等待退出、调用安装器、版本核验和恢复。
- [ ] 发布脚本传递并校验共享版本，包内加入协调脚本。
- [ ] 运行脚本检查、构建和全量测试，通过后提交。

脚本测试必须断言：停止超时后安装调用次数为 0；安装失败后原文件哈希、任务定义和启用状态恢复；回滚失败时不启动不完整程序。

## 第三步：设置页交互

文件：新增 `src/NetGuardian.App/Updates/UpdateSession.cs`、`Presentation/UpdatePresentation.cs`、`MainWindow.Updates.cs`、桌面更新测试；修改设置 XAML、语言资源、主窗口初始化和收尾。

- [ ] 先测试 Idle、Checking、Available、Downloading、Ready、Cancelled、Failed、Installing 状态下的可见操作与取消结果。
- [ ] 实现设置卡片、当前版本、行内状态、发布说明、进度、检查/下载/取消/安装按钮；所有新增文案加入中英资源。
- [ ] 安装确认使用现有 Fluent 对话框。外部协调准备完成后走原退出收尾；关闭窗口取消未完成的检查和下载。
- [ ] 验证最小窗口、键盘焦点、长说明及错误状态；运行构建、全量测试，通过后提交。

## 第四步：发布与本机验证

- [ ] 用正式公开 Release 验证匿名检查，当前 1.1.0 不将 1.0.1 识别为更新。
- [ ] 构建自包含 v1.1.0 安装包并核对 SHA256；检查暂存目录没有配置和日志。
- [ ] 检查工作区、提交和远程状态；推送 master 与 v1.1.0 标签。
- [ ] 按项目规则请用户退出旧界面，备份并更新本机必要组件及项目启动目录，保留无关守护运行。
- [ ] 启动核对版本、路径与新设置页，记录真实 UI/安装流程未覆盖的验证边界。
- [ ] 上传已核验安装包和校验文件，发布正式 v1.1.0，核对远程摘要与标签提交。
