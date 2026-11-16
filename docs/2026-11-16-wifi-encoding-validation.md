# 1.1.4 中文 Wi-Fi 名称与附近恢复列表验证

## 证据与范围

另一台电脑截图显示附近 SSID 为 `8b304北_5G_Wi-Fi5`，当前连接为 `8b304鍖梍5G_Wi-Fi5`；配置摘要有 55 条，附近恢复列表为 0。附近扫描使用原生 WLAN API，当前连接及保存配置详情使用 netsh。原命令读取器以严格 UTF-8 解码整个缓冲区，遇到系统代码页中文字段后对整个缓冲区回退，连 UTF-8 SSID 也按 GBK 解码。真实子进程重放混合字节后，旧代码精确产生截图中的 `鍖梍`，证明该编码缺口能够同时造成显示乱码与精确 SSID 匹配失败。

只修改 netsh 的 SSID 字段解码：保留严格 UTF-8 整段快速路径；在系统代码页输出中，分别读取字段名和 SSID 字节。当前连接、保存配置详情、扫描的编号 SSID 均覆盖。中文详情中系统代码页的弯引号与其中的 UTF-8 SSID 分开处理。配置名称及密钥文本继续采用原系统代码页，不猜测或改写用户保存的 SSID，不改变连接、密码、恢复策略或配置格式。ProcessCommandRunner 增加可选字节解码委托，其他命令维持原行为。

新增核心 6 项及桌面 1 项回归。桌面测试用真实字节读取、配置导入和 NearbySavedWifiView 验证：已保存且附近的网络重新出现，保留原对象、优先级和真实 Windows 配置名。版本由 1.1.3 递增为 1.1.4，README 安装包命令同步。

另一台电脑的完整 netsh 原始字节、实际运行版本及更新后的列表尚未验证，不能据截图和重放测试认定其所有运行环境因素都已经排除。

## 验证命令与结果

SDK：`.dotnet-home/sdk/dotnet.exe`。TEMP/TMP 指向工作区 `.dotnet-home/update-diagnostics-temp`，DOTNET_CLI_HOME 指向工作区 `.dotnet-home`。

- `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~NetshMixedEncodingTests --verbosity minimal`：旧代码重放测试 2 失败、2 通过，退出码 1。首个失败：`Expected: "8b304北_5G_Wi-Fi5"; Actual: "8b304鍖梍5G_Wi-Fi5"`；另一个包含首尾空格及冒号的 SSID 同样被误解码。
- 新增系统代码页弯引号测试最初 1 失败、5 通过，退出码 1：`Expected: " 北_5G "; Actual: " 鍖梍5G "`，随后补齐引号内 SSID 的独立解码并通过。
- 核心定向过滤 `NetshMixedEncodingTests|NetshOutputEncodingTests|SavedWifiProfileImportTests|CurrentWifiConnectionTests|SystemCommandTests`：35/35 通过，跳过 0，退出码 0。
- `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-build --no-restore --configuration Release --filter FullyQualifiedName~NearbySavedWifiViewTests --verbosity normal`：3/3 通过，退出码 0。
- `dotnet test NetGuardian.sln --no-restore --configuration Release --verbosity minimal --logger "console;verbosity=normal" --blame-hang-timeout 90s --blame-hang-dump-type none --results-directory .dotnet-home/validation/wifi-mixed-encoding/test-results`：核心 311/311、桌面 423/423，共 734 项，跳过 0，退出码 0。完整输出保存在 `.dotnet-home/validation/wifi-mixed-encoding/full-tests.log`。
- 首次静默输出的完整测试主动中断（退出码 1），当时仅已确认核心 311 项通过；逐项日志的完整重跑最终通过。未据此认定沙箱、SDK 或 WPF 不可用。
- 回归夹具最初采用 Windows PowerShell 5.1 不支持的单参数 Stream.Write，子进程退出码 1，4 项测试在进程退出码断言处失败；已改为三参数写入。该轮不作为产品故障证据。
- `git diff --check`：通过，退出码 0。既有 WFAC010 DPI 清单警告保留，没有通过禁用或跳过测试消除警告。

## 交付状态

修复源码与上述验证先提交。安装包构建、GitHub 发布及本机部署结果在完成后追加。本机界面仍运行时须用户从托盘手动退出后再替换；共用核心模块更新需要后台正常收尾并重新加载，保留配置与任务启用状态。
