# 已保存 Wi-Fi 密码查看验证

用户选择在详情中点击显示。恢复优先级列表中已有 Windows 凭据的网络，展开行详情后可点击“显示密码”，密码文本可选中复制，再点击“隐藏密码”清空显示。开放网络提示无密码；读取失败显示现有权限或未保存提示，可重试。

## 实现范围

- `MainWindow.xaml`：恢复优先级详情加入密码查看组件，保留原紧凑字段详情。
- `MainWindow.xaml.cs`：注入已有 `GetSavedPasswordAsync`，同步界面语言。
- `Presentation/WifiPasswordDetails.cs`：只在点击时读取；隐藏、收起、选择变化、上下文变化、卸载时清空并取消；10 秒超时及请求身份检查拒绝迟到结果。密码不进入业务模型、配置或日志，失败不输出底层异常内容。
- `Presentation/ResultTableInteraction.cs`：复合详情模板仍识别紧凑详情，保留透明度动画、统一时长及系统减少动画策略。
- `Core/Localization/Localizer.cs`：增加显示、隐藏、读取中、开放网络中英文文案。
- `WifiPasswordDetailsTests.cs`：点击前不读取、目标 SSID、可复制文本、隐藏、取消与迟到结果、数据更换、读取失败与重试、开放网络、未保存网络及未选中行的选择切换。
- `WifiDetailRegressionTests.cs`：保留原动画、字段与收起行高回归；新增密码行只增加恢复列表总高度预算，原字段详情仍限制在 120 DIP，其他列表预算不变。

本次没有修改扫描、连接、恢复、后台守护或配置格式。右侧列表继续沿用项目已有的“附近且已有凭据”视图。

## 验证

使用项目 `.dotnet-home/sdk/dotnet.exe`（SDK 8.0.426），先执行 `scripts/initialize-build.ps1`，TEMP/TMP 指向审查目录。

```powershell
dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~WifiPasswordDetailsTests --nologo
dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~WifiPasswordDetailsTests|FullyQualifiedName~WifiDetailRegressionTests' --nologo
dotnet build NetGuardian.sln -c Release --no-restore --nologo
dotnet test NetGuardian.sln -c Release --no-build --no-restore --nologo --logger trx --results-directory artifacts/ui-audit/wifi-password-20261114/results
git diff --check
```

- 旧界面入口测试失败：`Assert.NotNull() Failure: Value is null`，退出码 1。
- 首轮 9 项中 7 项通过，两个测试直接替换 `DataGridRow.DataContext`，未改变详情呈现器的实际内容。调整为真实替换 ItemsSource，以及详情上下文复用后，9 项通过，退出码 0。
- 独立审查发现选择未选中行与复合模板动画边界。新增选择测试先失败（旧文本未清空、请求未取消）；原动画测试也先失败（透明度始终为 1）。修复后相关 22 项通过，退出码 0，审查问题关闭。
- 完整 Release 构建通过，退出码 0，当前增量构建 0 警告、0 错误；前面重新编译界面时出现项目原有两条 WFAC010，未改变 manifest。
- 完整测试：核心 305/305，桌面 395/395，0 失败、0 跳过，退出码 0。
- `git diff --check` 通过，退出码 0。
- 深色、浅色、高对比资源主题的密码显示预览已渲染复核；文本可选中复制。未把离屏主题测试视为真实系统高对比、跨屏或读屏器验收。

完整日志、TRX 与包含测试密码的预览位于 Git 忽略目录 `artifacts/ui-audit/wifi-password-20261114/`：`red.log`、`green.log`、`green2.log`、`details-red.log`、`selection-red.log`、`fixed.log`、`build.log`、`full-test.log`、`results/`、`previews/`。失败的完整错误、堆栈及命令输出保留在相应日志中。

## 本机更新准备

本机窗口清单核实当前界面从 `C:\Program Files\NetGuardian\NetGuardian.exe` 启动，安装目录与启动器目录当前均为 `1.1.1+4830bc9…`。更新脚本已通过 PowerShell 语法检查；待提交后发布到独立审查目录，校验源文件及依赖，再按 AGENTS.md 请用户手动托盘退出。

更新计划仅替换两个目录各五个必要界面文件，备份旧文件并检查哈希、用户配置与守护文件。准备阶段未替换本机文件，实际安装与核验结果见下节。

## 本机更新结果

- 软件提交 `360db9c66f2505227f7e010a87e0ffe2cf726c07`：支持在已保存无线网络详情中显示和隐藏密码。
- `dotnet publish src/NetGuardian.App/NetGuardian.App.csproj -c Release --no-restore --self-contained false --nologo --output artifacts/ui-audit/wifi-password-20261114/release` 通过，退出码 0，保留原有两条 WFAC010。构建标识 `1.1.1+360db9c66f2505227f7e010a87e0ffe2cf726c07`；依赖及 runtimeconfig 与安装目录一致。
- 用户确认从托盘退出后，核实界面进程已退出，通过标准 UAC 执行更新脚本；安装结果 Success=true。更新了 `C:\Program Files\NetGuardian` 和项目 `dist\NetGuardian`，两目录共 10 文件与发布清单哈希一致。安装后再次独立核对文件哈希通过；管理员安装助手核实两份用户配置在替换前后、启动后保持一致，独立复核 Roaming 界面配置也保持一致。
- 备份：`artifacts/ui-audit/wifi-password-20261114/ui-backup-20261114-233653/`，包含旧界面文件、原文件及配置哈希清单。完整安装结果为审查目录的 `install-result.json`。
- 新界面 PID 78736，管理员安装助手核实实际路径 `C:\Program Files\NetGuardian\NetGuardian.exe`；独立复核仍在运行。后台守护 PID 99664 保持不变，守护及其他文件未替换。
- 使用发布的 Core 程序集实际调用当前网络与已保存密码只读接口，结果 `CurrentNetworkAvailable=true`、`SavedPasswordReadable=true`；没有输出或保存真实 SSID、密码或命令输出，没有执行连接、切网或凭据写入。
- 真实密码读取与本机安装已核验；密码按钮、复制文本、隐藏、收起与选择切换由真实 WPF 组件测试覆盖，尚未做安装窗口中的人工点击验收。

## 工具权限记录

- 普通沙盒的 Git 暂存/提交退出码 1，完整错误：`fatal: Unable to create 'C:/Users/Qiu/Documents/Net/.git/index.lock': Permission denied`。使用实际审批机制获得 Git 写权限后，提交成功，退出码 0。
- 非管理员独立复核 ProgramData 配置时退出码 1，完整读取错误：`Get-FileHash: Access to the path 'C:\ProgramData\NetGuardian\settings.json' is denied.`；后续比较因未取得哈希而报告 `安装后用户配置哈希变化。`。这是读取权限不足，不能据此判断配置变化。标准管理员安装助手已经在替换前后及启动后核实该文件哈希一致；独立复核仅检查全部安装文件、可读取的 Roaming 配置与进程，结果保存在 `independent-check.json`，没有绕过文件权限。
