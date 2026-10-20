# 结果区域辅助技术名称验证

## 范围

主窗口 12 个结果表、列表及日志详情框直接绑定现有标题、选项卡或导航项的本地化文本，避免重复维护资源。Wi-Fi 恢复列表开关以完整 SSID 命名，以所在列标题说明用途，保留原有双向状态和业务事件。没有调整可见布局、网络操作或持久化逻辑。

新增 `AccessibleResultsTests.cs`：同一夹具中依次更新中文和英文标题，验证真实控件 peer 名称；用两个不同 SSID、相反启用状态的内存样本验证开关名称、说明和 IToggleProvider.ToggleState。测试另断言资源键已翻译，防止缺失键原样返回而假通过。源码复核所有绑定源均与真实本地化赋值一致，包括日志的 LogSelectedDetails。

## 验证

SDK：`E:\Environment\dotnet-sdk-8.0\dotnet.exe`。运行前执行 `scripts/initialize-build.ps1`，TEMP/TMP 指向工作区忽略目录。以下公共参数为 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令（均加公共参数） | 结果 | 退出码 |
|---|---|---|
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --configuration Release --filter FullyQualifiedName~AccessibleResultsTests`（修复前） | 3/3 失败，原有表格及开关名称为空 | 1 |
| 同上（修复后） | 3/3 通过 | 0 |
| `dotnet test NetGuardian.sln --configuration Release` | Core 206/206，WPF 102/102，无跳过 | 0 |
| `dotnet build NetGuardian.sln --configuration Release` | 0 警告、0 错误 | 0 |
| `git diff --check` | 无空白错误；用户原有文档有换行格式提示，未修改该文档 | 0 |

完整失败信息、堆栈及成功输出保留于 `.dotnet-home/validation/product-ui/red-accessible-results.log`、`accessible-results-tests.log`、`accessible-results-release-tests.log`、`accessible-results-release-build.log`。红灯的核心差异为期望完整 SSID 或“立即检测”，实际值为空字符串；没有放宽名称断言或禁用测试。

独立只读审查无阻塞：本地化绑定源正确，行开关名称及状态能区分样本。

## 未完成项及边界

夹具没有启动含网络副作用的业务 MainWindow；测试直接更新真实标题控件，没有执行真实语言切换完整流程，也没有连接正式读屏客户端。不能据此宣称讲述人朗读、UIA 事件、硬件键盘或跨屏 DPI 已通过。

另发现系统高对比主题在应用自定义调色板选择中被映射为深色，此处没有修改；后续需要单独解决高对比优先级、系统色及主题恢复。正式安装仍为先前版本，管理员授权尚未完成，本次未重复发起 UAC。完整产品目标尚未完成。
