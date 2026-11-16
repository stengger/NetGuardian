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

修复、完整验证、安装包构建与正式发布已完成。2026-11-16 用户确认从托盘退出本机界面后，已完成本机安装目录与项目启动目录更新、后台正常重启及安装核验；配置与任务启用状态保留。另一台电脑升级后的实际 Wi-Fi 显示与列表验收仍待用户确认。

## 构建与正式发布

- 修复提交：`6230e3531bcda3d5f746613898c348efe16ba57d`（`修复中文WiFi名称混合编码与附近恢复列表匹配并准备1.1.4`），已推送 master。注释标签 `v1.1.4` 指向该实际构建提交。
- 使用 `git archive` 导出该提交到 `.dotnet-home/validation/wifi-mixed-encoding/source`，设置 `SourceRevisionId` 为完整提交哈希，从独立源码执行 `scripts/build-installer.ps1 -Version 1.1.4 -OutputDirectory dist/releases`。安装、后台、更新三个 PowerShell 脚本检查通过；隔离源码再次通过核心 311、桌面 423，共 734 项 Release 测试，跳过 0。自包含界面和 Guardian 发布、Inno Setup 编译均成功，退出码 0。
- 安装包 `NetGuardian-1.1.4-win-x64-Setup.exe`，77,474,615 字节；产品版本去除 Inno 资源尾部填充空格后为 `1.1.4`。SHA256：`ea2cb709a2378b709c012eb1690e9cf07739b259fc00ae270a761cd63183e5bf`。SHA256 附件 103 字节，其文件摘要为 `4b636703adfc0e234b8ad2746e2f542ebc7d1258a493f30d666f92ded822a8b8`。GitHub 上传摘要与本地逐个核对一致。
- GitHub Release `stengger/NetGuardian` 的 `v1.1.4` 已正式发布，非草稿、非预发布，匿名查询最新正式版和远程标签提交均一致。发布地址：`https://github.com/stengger/NetGuardian/releases/tag/v1.1.4`。
- 本机依赖系统运行时，因此从同一测试通过的独立源码分别执行界面和 Guardian 的 `dotnet publish --configuration Release --runtime win-x64 --self-contained false --no-restore`，生成 18 个组件文件，退出码 0。四个产品程序集构建标识均为 `1.1.4+6230e3531bcda3d5f746613898c348efe16ba57d`；另存部署清单和文件摘要。
- 构建完整日志、安装包核验与发布记录保存在 `.dotnet-home/validation/wifi-mixed-encoding/` 和 `.dotnet-home/validation/release-1.1.4-published.json`，均属于忽略的验证产物。

## 本机部署与启动核验

2026-11-16 用户确认已从托盘退出，随后只读查询确认本机无 NetGuardian 界面进程。通过系统正常管理员授权运行 `pwsh -NoProfile -File .dotnet-home/validation/wifi-mixed-encoding/deploy-reviewed.ps1 -Apply`；部署记录 `Success=true`，日志记录退出码 0。

- 备份目录：`.dotnet-home/validation/wifi-mixed-encoding/deployment-backup-20261116-174711`。逐文件备份核对摘要后，向已核实路径的后台写入正常停止请求，等待原后台收尾；恢复原任务启用状态后由 NetGuardian 计划任务启动新版后台。
- 安装目录 `C:\Program Files\NetGuardian` 与项目启动目录 `C:\Users\Qiu\Documents\Net\dist\NetGuardian` 各替换 12 个差异文件，共 24 个；两个目录各 18 个组件均与已验证部署包的 SHA256 一致。界面、Guardian 及共用核心程序集的构建标识均为 `1.1.4+6230e3531bcda3d5f746613898c348efe16ba57d`。
- 机器配置与用户配置均已备份，部署阶段分别核对原 SHA256，内容未变。NetGuardian 与 NetGuardian-Watchdog 任务保持原启用状态。
- 通过系统正常管理员授权运行 `pwsh -NoProfile -File .dotnet-home/validation/wifi-mixed-encoding/verify-installed.ps1`，记录 `Success=true`（成功路径退出码 0）。重新打开的唯一界面 PID 为 75596，实际路径为 `C:\Program Files\NetGuardian\NetGuardian.exe`，启动 3 秒后仍运行；后台 PID 为 77480，实际路径为安装目录下 `guardian\NetGuardian.Guardian.exe`，心跳进程号匹配、状态为 `Healthy`、控制协议版本为 1。再次核对两个目录各 18 个组件摘要通过。
- 部署日志、结果与安装核验保存在同目录的 `deployment.log`、`deployment-result.json`、`installed-verification.json`。无需回滚；未使用强制结束进程或界面自动化退出。

另一台电脑尚未执行升级后验收。应安装正式 1.1.4 后重新扫描，确认当前中文 SSID 显示和已保存且附近的恢复条目；旧版若卡在等待退出阶段，先从托盘退出界面，再运行发布的安装包。

## 生产更新接口验收

`pwsh -NoProfile -File .dotnet-home/validation/wifi-mixed-encoding/verify-public-release.ps1` 通过，退出码 0。加载本次源码构建的 NetGuardian.Core，使用真实 GitHubUpdateService、匿名 HttpClient 查询：`CheckAsync(1.1.3)` 返回正式版 `1.1.4`；`DownloadAsync` 下载完整 77,474,615 字节安装包并通过 SHA256 校验，摘要与已发布构建一致；`CheckAsync(1.1.4)` 返回无更新。结果保存在 `public-release-verification.json`，下载文件保存在同目录 `public-download/`。此验收覆盖更新查询与下载，不替代另一台机器上的实际安装和 Wi-Fi 列表验收。
