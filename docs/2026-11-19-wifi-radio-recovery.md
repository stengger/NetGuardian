# Wi-Fi 软件无线电自动启用修复

日期：2026-11-19（北京时间）。版本：1.1.7。

## 原因与修改范围

用户已经开启“Wi-Fi 关闭时自动尝试启用”，监控也正在运行。本机策略快照中 `enableWifiAdapterAutoEnable=true`；09:38:27、09:38:41、09:38:54 的日志均有 `TryingEnableWifiAdapter：WLAN`，随后为 `WifiAdapterEnableFailed`。扫描错误是“无线局域网接口电源关闭，它不支持请求的操作。”因此并非开关未保存或没有触发，而是原启用路径只处理接口管理状态，没有恢复关闭的软件无线电。

- `NativeWifiScanner.Radio.cs`、`IWifiRadioController.cs`：复用原生 WLAN 句柄、枚举与查询，按精确接口名称解析 GUID 并核对 WLAN 枚举结果；解析无线电状态后逐个 PHY 启用软件无线电。硬件关闭或未知时不写入，已经开启的 PHY 不重复写入；保留错误码，释放原生内存和句柄，传播取消。
- `NetshWifiClient.cs`：接口管理命令失败时仍尝试无线电恢复；PCI/USB 禁用设备保持精确 PnP 实例启用路径。无线电失败不能被接口命令成功掩盖。
- `IWifiClient.cs`、`WifiClient.cs`、`MonitoringEngine.cs`：透传启用失败原因到监控事件；仅监控运行且策略允许时自动启用，未启动监控的手动检测不执行启用。
- `NetGuardian.Core.csproj` 与三个测试文件：提供内部逻辑测试入口，新增 28 项覆盖软件无线电、硬件状态、多 PHY、取消、命令失败、监控条件和错误日志的回归。
- `Directory.Build.props`、`README.md`：版本由 1.1.6 升至 1.1.7，说明策略边界。没有修改用户配置或其他恢复策略。

## 验证

使用 SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe`，实际版本为 8.0.422。先设置 `NETGUARDIAN_DOTNET` 并加载 `scripts/initialize-build.ps1`。

- 修复前定向回归有 2 项预期失败，完整断言和调用栈保存在 `radio-red.trx`；修复后定向测试 28/28 通过，0 跳过，退出码 0，记录为 `radio-green.trx`。
- 当前源码验证依据：Core 339/339、0 跳过的独立 TRX，以及 09:58 至 10:01 同一份源码的 WPF 453/453、0 跳过 TRX。上述源码最后修改时间均早于这两次测试，测试后没有再次修改产品代码。
- 完整重跑命令：`dotnet test NetGuardian.sln --no-restore --configuration Release --verbosity minimal --logger "trx;LogFilePrefix=wifi-final" --results-directory .dotnet-home/validation/wifi-auto-enable-20261119/final`。Core 通过 339/339，0 跳过；WPF 长时间未输出，中止本次运行后带进度日志和挂起诊断重跑。中止重跑不算完整测试成功。
- 最终带诊断的完整重跑：Core 339/339、WPF 453/453，共 792 项通过，0 失败、0 跳过，退出码 0；两个独立 TRX 保存在 `diagnostic/`。WPF 实际耗时约 12.89 分钟，逐项测试持续完成，未触发两分钟无进度的挂起收集；不据此确定上一轮没有进度输出的具体原因，也没有修改、删除或跳过测试。
- `tests/Test-InstallerScripts.ps1`：通过，退出码 0。
- `tests/Test-GuardianScripts.ps1`：通过，退出码 0。
- `tests/Test-UpdateScripts.ps1`：按正常审批在真实工作目录重跑通过，退出码 0。以上脚本测试的任务、进程与系统修改调用使用替身，不属于真实安装或切网验收。
- `git diff --check HEAD`：通过，退出码 0；只有版本文件现有 LF 转 CRLF 提示，没有执行全仓库格式化。
- 原生只读查询成功：新版 Core 版本 1.1.7.0，目标 WLAN 接口返回 772 字节、7 个 PHY，软件及硬件状态均为开启。该检查未调用 `WlanSetInterface`，没有主动断网，不能代替真实关闭后自动恢复验收。
- 已完成独立只读代码审查，没有阻塞的 P1/P2 问题。未来可补充可替换原生 API 边界的释放路径测试，本轮不扩大为额外重构。

## 失败记录与证据

完整日志、TRX、只读检查脚本和本机快照保存在 Git 忽略目录 `.dotnet-home/validation/wifi-auto-enable-20261119/`，不提交配置或构建产物。

1. 首次未指定 SDK 的测试初始化失败，退出码 1；完整错误为：

   ```text
   dotnet SDK not found. Install .NET 8 SDK, add dotnet to PATH, or set NETGUARDIAN_DOTNET to the SDK location.
   ```

   指定上述 SDK 后实际执行正常，不是运行时或测试框架不可用。
2. 沙箱内 CIM 查询失败，退出码 1；完整错误为 `Get-CimInstance: 无法从客户端中访问 CIM 资源。` 按正常审批重跑成功，退出码 0。
3. 沙箱更新脚本测试失败，退出码 1；错误为 `拒绝重解析点路径：C:\Users\CodexSandboxOffline\.codex\.sandbox\cwd\4a57449c523efa25`。这是更新脚本主动拒绝沙箱重解析点；按正常审批在真实目录重跑通过，输出在 `update-scripts-authorized.log`。
4. 原生只读查询首次误查虚拟接口 WLAN 4，退出码 1；完整错误为 `接口 WLAN 4 无线电查询错误码 1168`。按原生枚举 GUID 过滤后查询目标 WLAN 成功，原错误保存在 `native-readonly-error.txt`，结果在 `native-readonly-result.json`。
5. 早先两项目使用相同 TRX 文件名，结果文件被覆盖，`full/radio-full.trx` 只保留 WPF 453/453。最终重跑使用 `LogFilePrefix` 保留两项目独立结果，不用覆盖的文件证明 Core 测试。
6. 上述 `wifi-final` 完整重跑在约 12 分钟后仍只有 Core 结果。通过原执行会话发送 Ctrl+C 正常中止，退出码 1，没有额外错误文本或 WPF 最终结果；不将中止运行算作通过或断言失败，也没有改动测试。随后使用 `dotnet test NetGuardian.sln --no-restore --configuration Release --verbosity minimal --logger "console;verbosity=normal" --logger "trx;LogFilePrefix=wifi-diagnostic" --blame-hang --blame-hang-timeout 2m --results-directory .dotnet-home/validation/wifi-auto-enable-20261119/diagnostic` 保留逐项进度及挂起诊断，日志为 `diagnostic-test.log`。

## 本机更新状态

修复验证通过后提交，再从目标提交隔离导出并发布界面及守护组件，附提交构建标识。保持现有依赖框架运行时部署形式，不改为自包含，不创建 GitHub Release。

已核实运行路径为 `C:\Program Files\NetGuardian\NetGuardian.exe` 与 `C:\Program Files\NetGuardian\guardian\NetGuardian.Guardian.exe`，同时需要更新项目 `dist/NetGuardian`。本次修复涉及后台 Core，安装时需要正常停止并重启相关后台，保留用户配置、任务定义和状态；替换前备份及校验，异常回滚。

当前等待用户手动从托盘退出界面并确认。尚未替换安装文件、重启软件或验收真实 Wi-Fi 关闭后自动恢复，不将源码、测试或发布包等同于本机已经更新。

## 已验证提交与隔离发布

- 源码提交：`389fe59e8a9224ad1a14a38590dbceb54e9c7aec`，提交信息“修复监控中无法自动开启 Wi-Fi 软件无线电的问题”。提交 13 个相关文件；提交后工作区干净，配置、日志和构建产物均未暂存。
- 发布入口：`.dotnet-home/validation/wifi-auto-enable-20261119/build-local-package.ps1 -SourceCommit 389fe59e8a9224ad1a14a38590dbceb54e9c7aec`。通过 `git archive` 导出目标提交到 `release-source-389fe59e`，不从共享工作区构建。
- 两个项目分别执行 `dotnet restore <项目> --configfile <隔离源码>/NuGet.Config --verbosity minimal` 和 `dotnet publish <项目> --configuration Release --self-contained false --no-restore --output <发布目录> -p:SourceRevisionId=389fe59e8a9224ad1a14a38590dbceb54e9c7aec`。沿用仓库 `initialize-build.ps1` 和 `package-support.ps1`，发布 UI 与 Guardian，未重新打安装器或发布 GitHub Release。
- 首次恢复依赖因沙箱权限失败，退出码 1。完整错误如下；重复行与脚本调用栈保存在 `local-package-build.log`：

  ```text
  E:\Environment\dotnet-sdk-8.0\sdk\8.0.422\NuGet.targets(174,5): error : 由于未授权访问权限，因此未能读取 NuGet.Config。路径:“C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config”。
  E:\Environment\dotnet-sdk-8.0\sdk\8.0.422\NuGet.targets(174,5): error :   Access to the path 'C:\Users\Qiu\AppData\Roaming\NuGet\NuGet.Config' is denied.
  恢复依赖失败，退出码 1
  ```

  按正常审批使用 `-Resume` 核对源码提交标记后继续，恢复与两个项目发布均成功，退出码 0；完整输出为 `local-package-build-authorized.log`。构建保留原有 `WFAC010` 高 DPI 配置警告，不涉及本次修复，未擅自修改 DPI 设置。
- 发布目录：`.dotnet-home/validation/wifi-auto-enable-20261119/package-389fe59e`。14 个产品组件的存在性与 SHA256 检查通过；UI、Guardian 与两处 Core DLL 产品版本均为 `1.1.7+389fe59e8a9224ad1a14a38590dbceb54e9c7aec`。结果记录在 `publish-result.json`。
- 安装前只读比对通过，退出码 0：重新校验发布包 SHA256，核对 `C:\Program Files\NetGuardian` 与项目 `dist/NetGuardian`，每处 12 个产品组件有差异，两处第三方 DLL 依赖与发布包哈希一致，两份运行时配置无需替换。结果为 `package-target-comparison.json`，没有复制文件到安装目录。实际安装时还需更新旧的 `UIInstalledBuild.json`，避免保留旧提交的安装标识。
- 此处仍是发布包验证，不是本机安装验证；安装和真实关闭后恢复仍等待用户退出确认。
