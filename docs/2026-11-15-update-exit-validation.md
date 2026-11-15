# 1.1.3 更新退出交接修复与交付记录

## 问题证据与修复边界

另一台电脑的 1.1.1 已下载 1.1.2。用户提供的作业文件显示 20:37:30 已生成 ready.json 和备份清单，20:39:30 结果为：

```json
{
  "Success": false,
  "Stage": "WaitingForExit",
  "Message": "等待原界面正常退出超时，未强制终止。",
  "RollbackSucceeded": false,
  "Version": "1.1.2"
}
```

因此确认失败发生在退出交接、安装尚未开始。机器与当前用户目录均没有 crash.log。**具体是哪项退出操作阻塞仍未确认**；本次修复使可取消的异步退出等待、退出异常和脚本失败能被更新流程处理，不据此宣称已重现或消除那台电脑的全部阻塞原因。

原更新流程只调用 async void ExitApplication，准备握手结束后不再等待退出或监视作业，失败后可长期保留“正在备份并准备安装”状态。

## 修改文件与原因

| 文件 | 修改原因 |
| --- | --- |
| Directory.Build.props | 正式版本递增为 1.1.3，供手动安装修复包 |
| MainWindow.Updates.cs | 更新流程等待完整退出交接，异常回到可重试状态 |
| MainWindow.xaml.cs | 退出主体返回 Task，普通托盘入口捕获异常；等待网络收尾支持取消 |
| Updates/ApplicationExitCoordinator.cs | 拒绝重复退出；取消后禁止迟到的收尾关闭窗口；检查完成后安全释放监控租约 |
| Updates/UpdateExitCoordinator.cs | 退出交接最多等待一分钟，同时核查协调脚本；失败先取消退出等待 |
| Updates/UpdateInstallLauncher.cs | 将退出纳入安装握手事务；交接失败也写 cancel.request，避免稍后退出触发旧请求 |
| Updates/UpdateSession.cs、Presentation/UpdatePresentation.cs、Core/Localization/Localizer.cs | 单独展示“备份完成、等待退出”阶段，保持互斥，失败恢复按钮与已校验安装包 |
| UpdateExitTests.cs | 验证异常、超时、重复退出、脚本失败、迟到完成及真实监控文件锁的收尾释放 |
| UpdateSessionTests.cs、UpdateLayoutTests.cs | 验证新阶段和失败后的按钮、提示恢复，覆盖中英主题场景 |
| README.md | 说明退出交接时限及旧版手动安装恢复途径 |

未修改网络恢复策略、配置格式、后台守护、安装事务脚本，也没有新增依赖。

## 验证与命令

使用工作区 SDK `.dotnet-home/sdk/dotnet.exe`，设置 TEMP/TMP 到工作区 `.dotnet-home/update-diagnostics-temp`，加载 scripts/initialize-build.ps1。

| 命令 | 结果、退出码 |
| --- | --- |
| dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --configuration Release --filter 'FullyQualifiedName~UpdateExitTests\|FullyQualifiedName~UpdateSessionTests\|FullyQualifiedName~UpdateLayoutTests\|FullyQualifiedName~WindowClosingTests' --verbosity minimal | 最终 14 项通过，0 |
| dotnet test NetGuardian.sln --no-restore --configuration Release --verbosity minimal --logger trx | 租约收尾修正前 Core 305、桌面 421 通过，0；之后由隔离安装包构建完成最终全量验证 |
| tests/Test-UpdateScripts.ps1 | PowerShell 7 通过，0 |
| powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-UpdateScripts.ps1 | Windows PowerShell 5.1 通过，0 |
| 隔离源码 scripts/build-installer.ps1 -Version 1.1.3 | 安装、守护与更新脚本测试通过；最终 Release Core 305、桌面 422，共 727 项通过，跳过 0；自包含 publish 与 Inno 编译通过，退出 0 |
| dotnet publish src/NetGuardian.App/NetGuardian.App.csproj --no-restore --configuration Release --self-contained false | 本机所需框架依赖界面包发布通过，0 |
| git diff --check | 通过，0；仅 Git 换行提示 |
| 真实安装包版本、同名 SHA256 文件及复制后哈希核查 | 通过，0；四个 App/Core/Guardian 程序集均为 1.1.3+a0ffa2386e6924afefe436c1ed629d021969f6ec |
| 管理员部署助手 Inspect / Apply / Verify | 路径与发布形式核实、备份、两处目录哈希核查通过；另行验证启动 PID 和路径 |

保留既有 WFAC010 DPI 配置警告。本次没有通过禁用、跳过或删除测试处理失败。

独立审查指出取消退出可能遗留监控租约，已修复并加入真实文件锁用例：检查期间仍互斥，空闲后可重新取得。复核无新增明确问题，可提交。

## 开发中失败与修正

- 测试先写，首次出现 CS0246/CS0103：尚无 ApplicationExitCoordinator、UpdateExitCoordinator（退出码 1）。首次文件锁用例出现 CS0117：尚无 WaitForIdleAsync（退出码 1）。实现后通过。
- 实现第一次编译出现 `MainWindow.xaml.cs(2928,22): error CS0246: 未能找到类型或命名空间名“ApplicationExitCoordinator”(是否缺少 using 指令或程序集引用?)`，退出码 1；补上命名空间引用。
- 随后出现 `MainWindow.xaml.cs(2939,90): error CS0117: “MainWindow.FeedbackSeverity”未包含“Error”的定义`，退出码 1；改用项目既有 Danger 值。
- Windows PowerShell 未带 ExecutionPolicy 参数的测试出现 `File ...\tests\Test-UpdateScripts.ps1 cannot be loaded because running scripts is disabled on this system. ... CategoryInfo: SecurityError ... FullyQualifiedErrorId: UnauthorizedAccess`，退出码 1；使用项目已有脚本执行方式加 `-ExecutionPolicy Bypass`，通过。
- 隔离安装包首次构建被沙箱拒绝读取 NuGet.Config，退出码 1：`NuGet.targets(174,5): error : 由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。`；内部错误为 `Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied.`。经正常审批取得构建所需访问权限后，原构建流程通过。完整重复错误及命令输出保存在 `.dotnet-home/validation/update-exit/installer-build.log`，成功输出在 `installer-build-elevated.log`。
- 本机预检助手首次未生成结果文件，命令返回“找不到路径 …\Inspect-deployment.json”，退出码 1；为中文助手转换 UTF-8 BOM 后，非管理员运行正确返回“需要 Windows 正常授予管理员权限，未替换文件”，退出码 1。通过 Windows 正常提升权限后预检、部署及独立核验均成功。产品脚本仍按既有打包流程生成 BOM。

## Git 与本机更新

代码提交：`a0ffa2386e6924afefe436c1ed629d021969f6ec`，提交信息“修复更新退出交接超时与失败恢复并准备1.1.3”。安装包从该提交的 git archive 隔离源码构建；本机发布时工作区干净，构建标识与该提交一致。

用户已从托盘手动退出原本机界面。更新目标：

- `C:\Program Files\NetGuardian`
- `C:\Users\Qiu\Documents\Net\dist\NetGuardian`

两处均由 1.1.2+985f28c 更新为 1.1.3+a0ffa23，共替换 12 个文件；核对七个界面发布文件，apphost 内容相同无需替换。备份保存在 `.dotnet-home/validation/update-exit/ui-backup-20261115-212937`，包含旧文件及 SHA256 清单。

配置哈希保持不变；两处后台文件哈希保持不变，后台 PID 91496、路径及创建时间保持原样。未重启后台、切网或重置网络。

新界面 PID 67680，启动路径为 `C:\Program Files\NetGuardian\NetGuardian.exe`，Responding=True。独立 Verify 再次核对两处文件哈希及实际进程路径；不能仅凭部署助手写入 Success 认定启动成功。启动包装命令的 Start-Process -Wait 会继续等待应用进程树，此等待不作为文件替换或应用健康的结论依据。

## 修复包与待验收项

交付文件：`dist/releases/NetGuardian-1.1.3-win-x64-Setup.exe`，77,464,901 字节。

SHA256：`d4df7ca2d3923b990fcfbf454edc62dd1884633582398e66b16bdb4a3e3cb955`。

同名 `.sha256` 已生成并核对，复制到交付目录后再次核对。完整构建、备份和核验 JSON 保存在 `.dotnet-home/validation/update-exit/`，构建产物与配置未提交 Git。本轮没有推送 Git 或发布 GitHub Release。

另一台电脑仍需正常退出旧版后手动安装修复包并验收。没有在另一台电脑执行安装，也没有以替身测试宣称真实 Inno 升级或安装失败恢复已端到端验收。同步阻塞于系统调用或资源清理的情形仍可能阻塞 UI 线程，异步时限不能保证打断这类阻塞；本次没有强制结束进程。
