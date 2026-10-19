# DNS 与 DHCP 操作边界整改验证

## 本次范围

继续产品级 UI/UX 总计划的第二单元，处理 DNS 目标、方案、结果反馈及 DHCP 范围。不更改真实网络配置，不替换安装目录，不以此宣布全应用完成。

- `MainWindow.xaml`：DNS 当前地址与待应用 IPv4 方案分开；接口、首选、备用字段有标签与自动化关联；应用操作与结果固定在底部，表格使用剩余高度；DHCP 有明确目标选择；适配器关键列保留可读最小宽度，窄窗口允许横向滚动；移除这两页的重复内部标题。
- `MainWindow.xaml.cs`：接口按稳定 ID 保留选择；没有唯一启用 IPv4 默认网关候选时不猜目标；不再根据地址匹配自动/手动来源。确认框列出接口与变更方案，执行期间冻结编辑，执行前复核目标。DHCP 只提供已启用 DHCP 的接口并在执行前重新读取 DHCP 条件。刷新不得覆盖命令结果；异常结束执行中状态，并明确配置可能改变。
- `NetworkConfigurationService.cs`：注入项目现有命令执行器；执行前验证全部地址与目标，不接受空目标或通配符；续租参数包含单个接口；首选写入后备用失败、取消或启动异常保留配置可能改变的信息。
- `NetworkAdapterSelection.cs`：提取默认选择及最新 DHCP 条件验证规则，不根据名称判断虚拟接口或实际互联网能力。
- `Localizer.cs`：新增中英文标签、目标确认、部分成功、中断和刷新失败文案。成功仅报告系统命令完成，不保证实际联网。
- `NetworkConfigurationTests.cs`：14 项命令及选择边界回归。`DesktopLayoutTests.cs` 增加两个窗口尺寸、25 行模拟接口、长名称、控件可达性和列宽回归。
- WPF 窗口测试的五个测试类归入 `DesktopTestCollection`：避免 WindowChrome 进程级字典并发初始化冲突；仍执行全部测试与断言，无跳过。

## 验证命令和结果

SDK：`E:\Environment\dotnet-sdk-8.0\dotnet.exe`。先执行 `scripts/initialize-build.ps1`，TEMP/TMP 指向 `.dotnet-home/product-temp`。dotnet 公共参数：`--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj` 加公共参数 | 198/198，通过，无跳过 | 0 |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj` 加公共参数 | 29/29，通过，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告，0 错误 | 0 |
| `python scripts/check-contrast.py` | 两种色板 14 对全部通过 | 0 |

命令完整输出：忽略目录 `.dotnet-home/validation/product-ui/network-full-core.log`、`network-full-app.log`、`network-build.log`、`network-contrast.log`。提交前另执行 `git diff --check`，不暂存用户原有 `docs/2026-10-18-validation.md` 修改。

## 失败、纠正及独立审查

- 测试先行运行因新增 API 尚不存在报 CS1729/CS1061/CS0246/CS0103，退出 1；初次测试参数化构造还存在 CS8752，已纠正测试写法后保留缺 API 的红灯，再实现。
- 窗口新标签不存在的断言先失败，退出 1；实现后两个尺寸均通过。
- 适配器名称最初在 1180 和 1040 宽窗口中仅约 145/79 DIP，回归退出 1。设置名称最小宽度后，再发现 WPF 将 IPv4 列压到 133/86.5 DIP，新增地址列回归退出 1；对关键列设置最小宽度并允许横向滚动后通过。没有削弱断言来接受截断。
- 独立审查发现 DHCP 确认后未重读 DHCP 条件，以及命令/刷新异常可能让页面停留在“正在续租”。补充最新条件回归及专用异常收尾后，独立复核确认两项已修复，未发现新增明确 P1/P2。
- 一次全量 WPF 运行 28 过、1 失败，退出 1：`InvalidOperationException: Operations that change non-concurrent collections must have exclusive access`，堆栈为 `DependencyObjectPropertyDescriptor.AddValueChanged → WindowChromeWorker._SetWindow → FluentWindow.OnSourceInitialized`。将创建真实窗口的测试归入非并发集合后，完整 29 项通过。不是禁用测试或删除断言。

## 视觉验证及未完成事项

沿用真实 MainWindow XAML 的屏幕外 WPF 窗口夹具；业务事件接线被移除，数据为模拟接口，不能当作真实配置或正式进程验收。已检查 `DnsPanel-1040.png`、`DnsPanel-1180.png`、`NetworkPanel-1040.png`、`NetworkPanel-1180.png`；图片位于上述忽略目录。关键输入、应用/续租按钮可达；多行表格在剩余空间内滚动；窄窗口横向滚动保留完整名称及 IPv4。

DNS 自动/手动来源仍明确未确认，而非已经实现来源读取。未执行真实 DNS 变更、DHCP 续租、跨屏 DPI、读屏或安装版验收。适配器类型本地化、链路/互联网语义统一、IPv6 展示一致性、普通刷新/启动读取异常的完整生命周期、质量页、日志、托盘等总计划事项继续有效。
