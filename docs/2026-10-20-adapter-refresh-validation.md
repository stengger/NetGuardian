# 接口信息与读取失败反馈验证

## 改动

- `Presentation/NetworkAdapterPresentation.cs`：统一接口类型、链路状态与 DNS 地址文本；不把 Up 解释成互联网可达。DNS 同时保留 IPv4/IPv6 并去重，未知状态不用原始枚举名冒充用户文案。
- `Presentation/NetworkConfigurationRefresh.cs`、`MainWindow.NetworkInfo.cs`：启动、手动刷新与命令后刷新共用失败边界。预期系统读取异常返回失败，清理不完整行和操作目标，显示读取失败；不报告刷新成功。普通程序逻辑错误仍向上传播。接口行保留原始类型和链路状态，切语言只重绘文本。
- `Presentation/NetworkOperationMessage.cs`：DNS/DHCP 结果保存状态键、参数和刷新失败标志。切语言重新格式化结果并追加读取警告，避免丢失部分改变、退出码和中断信息。
- `MainWindow.xaml.cs`、`Localizer.cs`：接入以上组件；两个页面明确 IPv4/IPv6 范围，类型为可读文本，状态明确指链路；普通刷新不覆盖读取错误，DNS 缓存命令后的刷新保留本次命令文字；DHCP 在释放忙状态后按实际目标列表恢复按钮。
- 新增 `NetworkAdapterPresentationTests.cs`、`NetworkConfigurationRefreshTests.cs`、`NetworkOperationMessageTests.cs`；更新真实窗口夹具的类型、链路与 IPv6 文案。

## 验证

使用 SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe`，项目初始化脚本及工作区 TEMP。公共参数：`--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| 新增 App 用例先运行 | 缺展示/刷新/操作消息类型，CS0103/CS0246，符合红灯阶段 | 1 |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj` 加公共参数 | 40/40，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |
| `dotnet test NetGuardian.sln --no-build` 加公共参数 | Core 201 + WPF 40 全过，无跳过 | 0 |

完整日志在 `.dotnet-home/validation/product-ui/adapter-refresh-{app,build,all-tests}.log`；红灯日志为同目录 `red-adapter-presentation.log`、`red-refresh-boundary.log`、`red-operation-language.log`。提交前检查状态、差异与 `git diff --check`，不提交原有用户文档改动。

独立审查发现切语言会把命令结果覆盖为单一读取错误。改为操作消息模型后补中文→英文回归，同时验证退出码、部分改变及读取警告仍存在；独立复核确认该 P2 修复，无新增明确 P1/P2。

已重新检查 1040×660 主窗口 XAML 的适配器截图，可读类型、链路状态、IPv4 及横向滚动正常；这是模拟数据夹具，不是正式进程和真实接口读取验收。没有执行 DNS 修改、续租或切网。

## 剩余边界

单个接口读取异常目前会把该页本次读取作为失败，并清空整页不完整结果，允许刷新重试；没有以部分行冒充完整清单。仍需正式进程的接口热插拔、权限异常、语言切换及跨 DPI 验收。托盘、日志、流量范围、其他工具页面生命周期与部署等总目标继续有效。
