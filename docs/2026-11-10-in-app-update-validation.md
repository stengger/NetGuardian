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
