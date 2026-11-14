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

更新仅替换两个目录各五个必要界面文件，备份旧文件并检查哈希、用户配置与守护文件。真实安装、重新启动路径与密码读取接口探测结果在完成后另补记录；当前文档不声称本机已更新。
