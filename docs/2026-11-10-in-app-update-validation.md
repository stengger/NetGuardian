# 应用内更新源码验收记录

本记录覆盖 v1.1.0 发布前的源码验证；真实本机部署和远程 Release 的哈希核验由发布流程另外记录。

## 改动与原因

| 文件或模块 | 改动目的 |
| --- | --- |
| `Directory.Build.props` | 以 1.1.0 统一程序集版本 |
| `NetGuardian.Core/Updates` | 匿名检查固定仓库的正式版本，限制附件来源与大小，流式下载并核对 SHA256，取消及失败清理临时文件 |
| `NetGuardian.App/Updates` | 管理可取消状态、受保护缓存、安装准备握手及维护互斥 |
| `App.xaml.cs` | 安装维护期间阻止新界面开始初始化 |
| `MainWindow.Updates.cs`、设置 XAML、`UpdatePresentation` | 显示当前版本、发布说明、下载进度、取消及安装，沿用令牌、按钮和 Fluent 确认框 |
| `MainWindow.xaml.cs`、`Localizer` | 接入初始化、语言切换、退出清理；仅新增中英更新文案 |
| `update-netguardian.ps1` | 独立预检、备份、安全停止、安装、版本核验、失败恢复及结果记录 |
| `package-support.ps1`、`build-installer.ps1` | 打包协调脚本并阻止源码、程序集、安装包版本不一致 |
| 新增及更新的测试、README | 验证关键失败路径并说明使用方式 |

未改配置格式、IPC、网络检测与恢复算法，未新增 NuGet 依赖。正式安装包内所有组件版本为 1.1.0；本机仅部署必要界面组件时保留无关守护进程。

## 已执行验证

先设置 `NETGUARDIAN_DOTNET` 指向工作区 `.dotnet-home/sdk/dotnet.exe`，加载 `scripts/initialize-build.ps1` 后执行以下命令。

| 命令 | 结果与退出码 |
| --- | --- |
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --filter FullyQualifiedName~UpdateServiceTests` | 14 项通过，0 |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --filter 'FullyQualifiedName~UpdateLayoutTests\|FullyQualifiedName~UpdateSessionTests\|FullyQualifiedName~UpdateMaintenanceTests'` | 会话、布局、维护锁用例通过；随后新增 ACL 用例的定向运行也通过，0 |
| `dotnet build --no-restore --verbosity minimal` | 通过，0；完整重编译时保留既有 WFAC010 DPI 配置警告 |
| `dotnet test --no-build --no-restore --verbosity minimal` | Core 303、WPF 249，共 552 项通过，跳过 0，退出 0 |
| `tests/Test-UpdateScripts.ps1`（PowerShell 7 与 Windows PowerShell 5.1） | 两种宿主均通过，0 |
| `tests/Test-InstallerScripts.ps1` | 通过，0 |
| `tests/Test-GuardianScripts.ps1` | 通过，0；任务及进程为替身 |
| `git diff --check` | 通过，0；仅 Git 换行提示 |
| 生产 `GitHubUpdateService.CheckAsync(1.1.0)` 匿名调用 | 通过 Windows 默认代理访问公开仓库，当前远程 v1.0.1 未被误判为更新，0 |

测试按失败后实现的流程执行：更新服务初次因缺少 `NetGuardian.Core.Updates` 编译失败（CS0234，退出 1）；会话、展示及维护门分别因缺少类型失败（CS0234/CS0103，退出 1），实现后通过。脚本测试也先因缺少协调实现、权限或锁内重开行为失败，修复后在两个宿主通过。

开发中一次新增测试漏写 `System.IO`，产生 CS0246（IOException），补充引用后通过。一次与尚未退出的 testhost 重叠构建产生 MSB3026 文件占用重试警告，原运行结束后复制成功，退出 0；最终验证按顺序执行。未禁用、删除或跳过测试。

## 审查与覆盖范围

独立审查发现并修复了准备超时遗留安装请求、并发事务、维护期间重开界面、通用 ACL 写权限遗漏，以及停止脚本子目录信任问题；定向复核通过。

真实 Inno 安装包的 ProductVersion 使用固定长度字段并包含尾部空格，补充了去除元数据空格和构建后缀的转换回归；两个 PowerShell 宿主及真实安装包的只读版本验证均通过。未运行安装包来代替隔离测试。

本机原安装为依赖已安装 .NET 的形式。第一次部署预检发现自包含暂存目录不匹配，在替换任何文件前中止；随后使用同一源码的框架依赖界面发布，保留原后台。受保护下载目录已通过真实提升权限的生产辅助类验证。界面按原配置启动到托盘；用户打开后已确认当前版本 1.1.0、检查更新及界面操作正常。初次启动核验以主窗口句柄判断失败，后续确认是既有托盘启动配置，而非界面崩溃。

测试覆盖旧版本/草稿/预发布、无效来源、缺少校验附件、大小/摘要/文件名不符、取消清理、限流、状态重试、迟到通知、最小窗口与长说明、跨线程维护互斥、ACL 通用写权限、安全停止超时零安装、外来任务拒绝、安装失败恢复、恢复失败不启动和启动失败不错误回滚。

桌面布局使用真实主窗口 XAML，替换事件接线，避免实际网络和配置副作用；已检查明暗主题、中文与英文的最小窗口预览。安装事务测试使用隔离目录及系统调用替身，**不能替代真实 Inno 安装、任务注册和整机故障恢复验收**。

## 前后差异

之前需要自行打开 GitHub 下载和运行安装包，程序集版本与外部发布版本也存在差异。现在设置页可检查、阅读说明、下载、校验和确认安装；更新协调进程维护原后台状态并保留恢复记录。程序不会启动自动检查，也不会强制安装。

## 手动验证清单

- [ ] 从正式安装目录打开，设置页显示当前版本 1.1.0；中文与英文资源完整。
- [ ] 最新版本点击检查显示已是最新；网络不可用、GitHub 限流后可重试。
- [ ] 有较新正式版本时显示版本与说明，说明可展开且长内容可滚动到末尾。
- [ ] 下载进度持续更新；取消后不出现安装按钮，重新下载可完成。
- [ ] 校验失败不允许安装；开发目录的安装按钮禁用并解释原因。
- [ ] 安装确认取消后界面继续可用，确认后等待备份，再正常退出。
- [ ] 真实维护环境中确认后台任务按原启用/运行状态恢复，配置与日志保留。
- [ ] 维护期间从快捷方式启动的新界面被拒绝，安装或恢复成功后可打开。
- [ ] 在测试机验证安装失败与恢复失败，核对日志、原文件和任务；不要在生产机制造损坏。
- [ ] 深色、浅色、高对比主题及 100%/125%/150% DPI 下检查按钮、进度、焦点和提示。
- [ ] 最小窗口下无横向溢出；Tab 顺序可达全部更新操作，确认框 Esc/Enter 正常。

发布 v1.1.0 后，从 1.1.0 升级到下一正式版本的真实端到端路径仍需后续版本或专用测试环境验证，不能以本轮替身测试宣称已经完成。

## 正式发布与本机交付

- 正式 Release：[v1.1.0](https://github.com/stengger/NetGuardian/releases/tag/v1.1.0)，公开匿名可访问，已设为 latest；安装包和同名 `.sha256` 均已上传并核对 GitHub 附件摘要。
- 功能提交和标签目标：`653c7cf93b1a77d2fcb8cf0f5ee980918a393894`。本任务初建的未发布标签先指向 `d417dd9`，发现真实安装包版本空格后，以精确旧标签对象的 `force-with-lease` 校正到最终修复；正式发布后没有改写标签。
- 安装包：`NetGuardian-1.1.0-win-x64-Setup.exe`，77,437,633 字节，SHA256 为 `6902a85155d01be138c77707f714f29a9e485903532b024a15c23f58329abdc6`。
- 最终自包含安装包从干净提交 `653c7cf` 构建；Release 模式 Core 303、WPF 249，共 552 项通过。App、Core、Guardian 的 ProductVersion 均为 `1.1.0+653c7cf93b1a77d2fcb8cf0f5ee980918a393894`。暂存目录未混入用户配置和日志。
- 使用生产 `GitHubUpdateService` 匿名检查从 1.0.1 到 1.1.0，完整下载正式安装包并验证上述 SHA256，通过；以当前 1.1.0 再检查返回无更新。未启动安装包。
- 本机更新目录为 `C:\Program Files\NetGuardian` 和项目的 `dist\NetGuardian`。界面和 Core 使用已验证的 `d417dd9` 构建，后续只更新 `653c7cf` 的协调脚本，避免为脚本补丁再次退出已确认正常的界面；正式安装包包含最终提交的全部组件。
- 本机界面 PID 91084、路径来自 Program Files、响应正常，用户已确认设置页 1.1.0 和检查更新正常。后台保持 PID 84392 与原启动时间；没有重启后台、切网或重置网络，配置未被部署覆盖，启动后崩溃日志未新增。
- 协调脚本在两处目录的 SHA256 均为 `c2a10167ca95c651ab5a147cd99561917d6c39cd0208fcec2f14f0faf6fdb3c0`；真实下载缓存 ACL、停止脚本保护链及 Inno 版本读取已核验。临时补丁文件已清理，备份保留。

本机临时部署助手曾因 PowerShell 5.1 将空备份参数转换为空路径而中止，并因混用 UTC 与本地 DateTime 产生守护时间误报；两次均回退并核对哈希，分别用显式备份路径和统一 UTC 修正。最终核验为成功，这些助手兼容问题没有改动产品的恢复算法。

完整构建日志和机器核验记录保存在工作区 `.dotnet-home/validation/release-1.1.0-final-build.log`、`update-1.1.0-deployment/final.json`、`update-public-verification.json` 和 `release-1.1.0-published.json`。这些本机记录及构建产物被 Git 忽略，没有提交用户配置、令牌或凭据。

## 完整改动文件

```text
Directory.Build.props
README.md
docs/2026-11-10-in-app-update-validation.md
docs/superpowers/plans/2026-11-10-in-app-update.md
docs/superpowers/specs/2026-11-10-in-app-update-design.md
scripts/build-installer.ps1
scripts/package-support.ps1
scripts/update-netguardian.ps1
src/NetGuardian.App/App.xaml.cs
src/NetGuardian.App/MainWindow.Updates.cs
src/NetGuardian.App/MainWindow.xaml
src/NetGuardian.App/MainWindow.xaml.cs
src/NetGuardian.App/Presentation/UpdatePresentation.cs
src/NetGuardian.App/Updates/UpdateInstallLauncher.cs
src/NetGuardian.App/Updates/UpdateMaintenanceGate.cs
src/NetGuardian.App/Updates/UpdateSession.cs
src/NetGuardian.App/Updates/UpdateStorage.cs
src/NetGuardian.Core/Localization/Localizer.cs
src/NetGuardian.Core/Updates/GitHubUpdateService.cs
src/NetGuardian.Core/Updates/UpdateRelease.cs
tests/NetGuardian.App.Tests/UpdateLayoutTests.cs
tests/NetGuardian.App.Tests/UpdateMaintenanceTests.cs
tests/NetGuardian.App.Tests/UpdateSessionTests.cs
tests/NetGuardian.Tests/UpdateServiceTests.cs
tests/Test-InstallerScripts.ps1
tests/Test-UpdateScripts.ps1
```
