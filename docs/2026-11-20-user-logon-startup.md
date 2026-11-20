# 登录 Windows 后自动启动桌面界面

日期：2026-11-20（北京时间）。版本：1.1.8。

## 原因与改动

原设置“启动后自动开始监控”只决定程序打开后的行为；“开机无人值守后台守护”运行在 SYSTEM 会话，不会打开当前用户的桌面界面。原程序没有注册用户登录启动入口。

- `Startup/UserLogonStartup.cs`：新增当前用户的交互登录计划任务，精确绑定用户 SID 和安装目录，无参数、不保存密码，允许电池供电，无运行时长限制。程序要求管理员权限，因此使用最高可用权限的交互任务；启用前检查 Program Files 安装目录及文件权限，拒绝覆盖其他用户或路径的任务。
- `App.xaml.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`Localizer.cs`：注册服务，新增“登录 Windows 时启动 NetGuardian”开关和实际任务状态反馈，支持中英文。注册失败后重新读取 Windows 的真实状态，不把失败报告为已开启；窗口显示方式沿用托盘设置。
- `installer-maintenance.ps1`：卸载时先完成既有后台安全停止流程，再删除指向本安装目录的登录任务；保留其他目录任务。
- `UserLogonStartupTests.cs`、`DesktopLayoutTests.cs`、`PanelNavigationTests.cs`、`Test-InstallerScripts.ps1`：覆盖任务身份、目标路径、权限级别、电池与时长设置、启停、外来任务保护、失败反馈及设置布局、卸载范围。
- `Directory.Build.props`、`README.md`：版本升为 1.1.8，说明登录启动、自动监控及 SYSTEM 后台的不同作用。

本轮不改变 Wi-Fi 恢复策略或用户配置，不替换、停止或重启后台守护组件。

## 验证

SDK：`E:\Environment\dotnet-sdk-8.0\dotnet.exe`，实际版本 8.0.422。先设置 `NETGUARDIAN_DOTNET`，加载 `scripts/initialize-build.ps1`。

- 先新增界面断言，原实现下四项预期失败：`Assert.IsType() Failure: Value is null`，退出码 1。新增服务测试在实现前因缺少 `Startup` 命名空间及 `ILogonTaskStore` 编译失败，CS0234/CS0246，退出码 1。
- 实现后定向测试：`dotnet test tests/NetGuardian.App.Tests --configuration Release --no-restore --filter 'FullyQualifiedName~UserLogonStartupTests|FullyQualifiedName~明暗主题下设置长状态可以滚动至末尾且保留语言主题选择|FullyQualifiedName~全部导航项保持唯一选择且拒绝未知或禁用目标' --verbosity minimal --logger 'trx;LogFileName=logon-focused.trx' --results-directory .dotnet-home/validation/user-logon-20261120`，14/14 通过，0 失败、0 跳过，退出码 0。
- 完整回归：`dotnet test NetGuardian.sln --configuration Release --no-restore --verbosity minimal --logger 'trx;LogFilePrefix=logon-full' --logger 'console;verbosity=normal' --blame-hang --blame-hang-timeout 2m --results-directory .dotnet-home/validation/user-logon-20261120/full`，Core 339/339、WPF 461/461，共 800 项通过，0 失败、0 跳过，退出码 0。两项目分别保留独立 TRX。
- `tests/Test-InstallerScripts.ps1`：先验证旧实现失败，错误“卸载应只移除指向当前安装目录的登录任务。”，退出码 1；实现后通过，退出码 0。
- `tests/Test-GuardianScripts.ps1`、`tests/Test-UpdateScripts.ps1`：均通过，退出码 0。脚本中的系统调用使用替身，不能代替真实注册任务或安装验收。
- `dotnet build NetGuardian.sln --configuration Release --no-restore --verbosity minimal`：通过，0 警告、0 错误，退出码 0。
- `git diff --check`：通过，退出码 0；只有既有 LF 转 CRLF 提示。

完整断言、调用栈、日志、TRX、构建包、部署脚本及备份保存在 Git 忽略目录 `.dotnet-home/validation/user-logon-20261120/`，不提交配置和构建产物。

## 环境失败记录

- 沙箱内 MSBuild 退出码 1，完整错误：`error MSB3883: 意外的异常:`、`Access to the path 'C:\Users\Qiu\AppData\Local\Temp\MSBuildTemp\tmpc31f2a2965584f1792ef8dbad5b4c30b.rsp' is denied.` 按正常审批在真实临时目录重跑后通过，不是 SDK 或测试框架不可用。
- 沙箱 CIM 查询退出码 1：`Get-CimInstance: 无法从客户端中访问 CIM 资源。` 中权限读取 SYSTEM 任务返回 `0x80070005`，不能据此判定任务不存在；实际安装验收使用正常 UAC 管理员上下文。
- 首次更新脚本测试的自动审批服务断连，操作没有执行：`Automatic approval review failed: stream disconnected before completion: Upstream service temporarily unavailable`；`The action was not executed because automatic approval review could not be completed.`；`This is a review failure, not a determination that the action is unsafe.` 重新走正常审批后测试通过。
- 首次暂存被沙箱阻止，退出码 1：`fatal: Unable to create 'C:/Users/Qiu/Documents/Net/.git/index.lock': Permission denied`。按正常审批暂存和提交成功，不属于构建故障。
- 最终独立构建的自动审批检查曾超时，命令未执行，工具原文：`The automatic permission approval review did not finish before its deadline. Do not assume the action is unsafe based on the timeout alone. You may retry once, or ask the user for guidance or explicit approval.` 按允许的重试完成，退出码 0，0 警告、0 错误。

## 真实 Windows 读取回归

首次源码提交为 `4e1d0ea2b1ba799079d12238d4723240fd1c2e1b`。从该提交隔离发布后，安装前只读调用真实产品服务发现异常：Windows 动态 COM 将任务不存在的 `0x80070002` 映射为 `System.IO.FileNotFoundException`，原代码只捕获 `COMException`。当时尚未替换安装文件或注册任务，旧包不部署。

只读复现以 `$ErrorActionPreference='Stop'` 保留完整错误及调用栈到 `native-read-error.txt`，退出码 1。关键原文：

```text
System.Management.Automation.MethodInvocationException: Exception calling "IsEnabled" with "0" argument(s): "系统找不到指定的文件。 (0x80070002)"
 ---> System.IO.FileNotFoundException: 系统找不到指定的文件。 (0x80070002)
   at Microsoft.CSharp.RuntimeBinder.ComInterop.ComRuntimeHelpers.CheckThrowException(Int32 hresult, ExcepInfo& excepInfo, UInt32 argErr, String message)
```

新增只读集成测试“真实任务计划程序中不存在的登录任务应读取为关闭”，只查询随机名称，不创建或删除系统任务。修复前单项回归按预期失败，错误同上，退出码 1，完整调用栈位于 `native-read-red.trx`。随后把任务不存在的判断改为精确匹配 HRESULT，继续传播权限及其他错误。

最终完整重跑使用同一 `dotnet test NetGuardian.sln` 命令，将日志前缀改为 `logon-final`、结果目录改为 `.dotnet-home/validation/user-logon-20261120/final`。Core 339/339、WPF 462/462，共 801 项通过，0 失败、0 跳过，退出码 0；新增真实 Windows 查询回归通过。编译保留原有 `WFAC010` 高 DPI 配置警告，本轮未修改 DPI 设置。

## 本机更新与验收范围

通过验证后提交源码，从目标提交隔离导出并发布 UI，附提交构建标识。更新 `C:\Program Files\NetGuardian` 与项目 `dist\NetGuardian` 中有差异的界面组件、README 和卸载维护脚本，先备份并校验 SHA256；保持后台文件、运行进程及原 SYSTEM 任务不变。

实际部署结果将在完成后补记。登录任务试启动可以验证任务能够打开安装目录中的新版程序，仍需用户下次真实重启并登录 Windows 后确认端到端效果，本轮不重启电脑。
