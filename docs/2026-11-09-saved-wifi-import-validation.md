# Windows 已保存 Wi-Fi 自动导入验证

日期：2026-11-09。范围：源码实现、构建、自动化回归及本机只读核验；不安装、不替换正在运行的程序，不执行真实切网。

## 行为

- 界面启动及点击“扫描”时读取 Windows 已保存配置，不依赖网络当前是否在附近。
- 按真实 SSID 精确去重，保留大小写及首尾空格；当前连接的网络在新导入项中优先，新项排在已有优先级之后。
- 保留已有网络的优先级、启用状态、自动连接选项、备注和历史。新项默认启用并允许自动连接。
- 单独保存 Windows 配置名与真实 SSID，手动连接和恢复都使用正确的配置名。
- 记住手动移除的 SSID，撤销移除或手动重新添加后解除排除；忽略临时验证配置。
- 不导出密码，不因导入而连接或切换网络。导入不代表已验证网络能够上网。
- 查询不完整、失败或超过 30 秒时保留原配置；先保存独立快照，再更新界面和监控。并行导入请求复用正在进行的任务。
- 自动启动监控等待本次导入结束。后台仍只同步已有候选的可用状态，新增网络通过界面启动或扫描导入。

## 修改文件与原因

| 文件 | 原因 |
| --- | --- |
| `src/NetGuardian.Core/Wifi/SavedWifiProfileImporter.cs` | 保存配置元数据及合并逻辑，处理去重、排序、排除与已有状态保留。 |
| `src/NetGuardian.Core/Wifi/NetshWifiClient.cs` | 只读查询配置详情，读取真实 SSID 和认证方式；支持中文输出用于包围 SSID 的弯引号。 |
| `src/NetGuardian.Core/Wifi/IWifiClient.cs`、`WifiClient.cs` | 提供和转发已保存网络读取接口。 |
| `src/NetGuardian.Core/Models/WifiProfile.cs` | 区分 Windows 配置名和 SSID，兼容旧配置。 |
| `src/NetGuardian.Core/Wifi/WifiProfileAvailability.cs` | 后台按 Windows 配置名核对凭据可用状态。 |
| `src/NetGuardian.Core/Monitoring/MonitoringEngine.cs` | 当前网络重连及备用切换使用正确的 Windows 配置名。 |
| `src/NetGuardian.Core/Settings/AppSettings.cs`、`SettingsStore.cs` | 持久化自动导入排除列表，兼容旧配置及空值。 |
| `src/NetGuardian.App/MainWindow.WifiImport.cs` | 自动导入、超时、并发请求合并、界面发布和手动连接配置名映射。保留已有行对象，避免干扰正在编辑的弹窗。 |
| `src/NetGuardian.App/Presentation/WifiProfileImportTransaction.cs` | 在独立配置快照中合并并保存，写入失败不发布修改。 |
| `src/NetGuardian.App/MainWindow.WifiScan.cs` | 扫描前同步已保存网络。 |
| `src/NetGuardian.App/MainWindow.xaml.cs` | 接入启动导入、移除及撤销记录、手动重新添加和连接逻辑。 |
| `src/NetGuardian.Core/Localization/Localizer.cs` | 补充导入成功、查询失败和超时的中英文消息。 |
| `tests/NetGuardian.Tests/SavedWifiProfileImportTests.cs` | 覆盖导入顺序、幂等、原设置保留、名称精确匹配、排除、失败、持久化、旧配置、配置名连接、只读命令、取消及中文弯引号。 |
| `tests/NetGuardian.Tests/MonitoringTests.cs` | 测试替身记录实际配置名连接参数，供端到端恢复状态机测试断言。 |
| `tests/NetGuardian.App.Tests/WifiProfileImportTransactionTests.cs` | 覆盖独立快照、当前界面列表、保存失败及无变化不写盘。 |
| `README.md` | 说明导入触发时机、默认行为和使用边界。 |
| 本文件 | 记录验证命令、结果、失败及限制。 |

## 验证命令和结果

以下 `dotnet` 均指仓库内 `.dotnet-home/sdk/dotnet.exe`，实际运行版本为 8.0.426；设置 `DOTNET_CLI_HOME` 为仓库的 `.dotnet-home`。完整日志位于本地忽略目录 `.dotnet-home/validation/wifi-import/`，不提交验证配置、运行产物或本机网络清单。

| 命令 | 结果与退出码 | 日志 |
| --- | --- | --- |
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --filter 'FullyQualifiedName~SavedWifiProfileImportTests\|FullyQualifiedName~WifiProfileAvailabilityTests\|FullyQualifiedName~NetshConnectionTests\|FullyQualifiedName~NativeWifiConnectionTests' --logger 'console;verbosity=normal'` | 初轮相关测试 29/29，通过，退出码 0。 | `focused-tests.log` |
| `dotnet build NetGuardian.sln --no-restore -v minimal` | 最终构建通过，0 错误，2 条 WFAC010 警告，退出码 0。 | `final-build.log` |
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --logger 'console;verbosity=minimal'` | 加入本机发现的中文弯引号回归后 259/259，通过，退出码 0。 | `core-final-tests.log` |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~WifiProfileImportTransactionTests' --logger 'console;verbosity=normal'` | 保存事务测试 4/4，通过，退出码 0。 | `import-app-tests.log` |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~TrayContextMenuTests' --logger 'console;verbosity=normal'` | 正常桌面权限下 5/5，通过，退出码 0。 | `tray-tests-normal-permissions.log` |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-build --no-restore --logger 'console;verbosity=minimal'` | 正常桌面权限下完整桌面回归 159/159，通过，退出码 0。 | `app-all-normal-permissions.log` |
| `dotnet test NetGuardian.sln --no-build --no-restore --logger 'console;verbosity=minimal'` | 最终正常桌面权限下 Core 259/259、App 159/159，合计 418/418，通过，无失败、无跳过，退出码 0。 | `final-solution-tests.log` |
| `git diff --check` | 无空白错误，退出码 0；Git 提示一个文件的 LF 将按仓库设置转换为 CRLF。 | 工具输出 |

初次构建出现两条 CS0103 错误：`MainWindow.WifiImport.cs(37,92)`、`(44,81)` 找不到 `UiLogLevel`，命令退出码 1。补充 `NetGuardian.App.Presentation` 命名空间引用后构建通过。完整错误如下：

```text
C:\Users\Qiu\Documents\Net\src\NetGuardian.App\MainWindow.WifiImport.cs(37,92): error CS0103: 当前上下文中不存在名称“UiLogLevel” [C:\Users\Qiu\Documents\Net\src\NetGuardian.App\NetGuardian.App_uycgj4j4_wpftmp.csproj]
C:\Users\Qiu\Documents\Net\src\NetGuardian.App\MainWindow.WifiImport.cs(44,81): error CS0103: 当前上下文中不存在名称“UiLogLevel” [C:\Users\Qiu\Documents\Net\src\NetGuardian.App\NetGuardian.App_uycgj4j4_wpftmp.csproj]
```

## 环境相关的失败与处理

1. PATH 中 `dotnet --version` 退出码 1，报告 `No .NET SDKs were found.`；实际运行仓库内 SDK 得到 8.0.426，后续构建及测试均成功，未误判运行时或测试框架不可用。
2. 受限权限下首轮完整回归：Core 256/256，App 158/159，命令退出码 1。失败为 `TrayContextMenuTests.隐藏窗口时真实弹出菜单可关闭并重复打开` 第 120 行，Escape 后 `Assert.False(menu.IsOpen)` 得到 True。包含该测试的单独复测也失败，退出码 1。完整错误及堆栈见 `solution-tests.log`、`app-focused-tests.log`。正常桌面权限下未修改任何托盘代码，5 项托盘测试及 159 项桌面测试全部通过。
3. 本机只读验证工具首次 `dotnet run --project .dotnet-home/validation/wifi-import/readonly-check/ReadonlyCheck.csproj` 因不能访问用户级 NuGet 配置失败，退出码 1；随后显式 `dotnet restore ... --configfile NuGet.Config` 仍有相同权限错误，退出码 1。完整输出见 `readonly-check.log`、`readonly-restore.log`。最终直接调用 SDK 自带 Roslyn 编译器，引用现有 .NET 8 参考程序集和已构建 Core DLL，避免触发 NuGet；编译退出码 0，完整命令保存在验证会话工具记录，参数文件为 `readonly-check/compile.rsp`。
4. 同一只读工具在受限权限下不能读取 Windows 配置详情，退出码 2，Windows 文本为“请求的操作需要提升。”，完整错误保留在 `readonly-result.log`。应用自身清单原本就要求管理员权限。正常权限下查询成功，未增加新的系统权限要求。

NuGet 权限错误原文：

```text
error : 由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。
error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied.
```

## 本机只读核验

运行 `dotnet exec .dotnet-home/validation/wifi-import/readonly-check/ReadonlyCheck.dll`，正常权限下最终退出码 0，完整输出见 `readonly-final.log`。

- 读取 58 份 Windows 保存配置，按真实 SSID 去重后导入 57 个网络。
- 当前连接的网络识别成功，首次导入排序正确。
- 再次导入新增 0 个网络，配置无变化。
- 保存到忽略目录下独立验证配置，重新读取 57 个网络，全部配置名和 SSID 映射一致。
- 系统无线配置、连接和监控均未修改，没有执行连接验证或切网。

首次本机读取虽然成功，但当前网络匹配失败。核查发现中文 netsh 的 `SSID 名称` 使用 `“…”` 作为显示引号，旧解析器仅移除 `"…"`。已补充成对中文弯引号解析，并覆盖名称首尾空格和名称本身包含弯引号的回归；之后本机当前网络匹配成功。

## 限制

- 尚未打包、安装或替换用户正在使用的版本；没有启动真实管理窗口验证自动导入的完整交互。
- 导入不验证密码或公网可用性，已保存但不可用的网络仍可能作为候选，需要按实际需要关闭或移除。
- 保留已有 WFAC010 高 DPI 清单警告，本次没有更改 DPI 设置。若需要清理，可另行检查 WinForms 托盘与 WPF 跨屏显示，避免无关改动影响现有显示行为。
