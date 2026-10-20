# 安装续接、读取性能与 DPI 清单验证

## 安装与额度续接

普通额度恢复后，额度等待自动化已停用（PAUSED）。首次仅带 id/status 的工具参数被拒绝，补全 heartbeat 字段后成功；未修改任何计费设置或使用重置券。

更新前发布包 11 文件哈希通过，单实例互斥可用，无界面进程、无同名备份；明确先前 UAC 已结束后重新发起一次管理员更新，成功退出 0。安装及独立核验见 `docs/2026-10-20-high-contrast-validation.md` 最新追加。已安装 `01d9363` 包含托盘、弹窗、高对比及此前页面整改，不包含本轮随后发现的 DPI 清单修复。

## 读取性能调查

独立 Release STA 诊断程序直接调用真实 `GuardianStatusStore.Read`，并使用表达式委托调用真实静态 `MainWindow.ReadNetworkByteCounters`，未实例化业务主窗口。每组 1 次首次调用、10 次预热、600 次连续测量。后追加 Dispatcher 的两组各 20 秒测量：16ms Render 优先级计时器与 1s 读取计时器，统计相邻回调间隔超出 16ms 的部分。此指标不是帧率，也不是真实输入响应时间。

复测环境：.NET 8.0.28、64 位、Windows 10.0.26200.0、2 个活动接口。

| 路径 | 首次 ms | 中位数 ms | P95 ms | 最大 ms |
|---|---:|---:|---:|---:|
| 当前权限读取机器后台状态 | 5.494 | 0.054 | 0.126 | 0.205 |
| 有效模拟状态（无报告） | 3.477 | 0.084 | 0.170 | 0.299 |
| 模拟文件缺失 | 0.227 | 0.037 | 0.077 | 0.202 |
| 模拟 JSON 无效 | 2.762 | 0.103 | 0.225 | 0.431 |
| 真实接口枚举及计数 | 17.254 | 8.826 | 9.871 | 12.027 |
| 真实后台读取与接口计数合并 | 8.750 | 8.995 | 10.072 | 13.180 |

20 秒基线与读取组各运行 19 次 1s 回调、800 次探针回调。基线超时 P95/最大为 16.009/17.696ms；读取组为 15.975/19.719ms。计时器受系统调度粒度影响，单次比较未发现明显 P95 退化，不足以证明真实业务界面流畅或需要改造线程。本轮未改采样实现。

当前令牌读取 `C:\ProgramData\NetGuardian\guardian-status.json` 明确返回 UnauthorizedAccessException，`Read` 返回 null；上表这一路是权限失败路径，不是成功读取真实报告。正式程序清单 requireAdministrator，诊断令牌与正式应用权限不同，不能把诊断失败解释为正式产品后台状态不可读。尚需管理员真实报告、慢 I/O、接口变化及真实输入压力验证。

诊断项目位于忽略目录 `.dotnet-home/validation/polling-performance/PollingPerformance.csproj`；命令 `dotnet run --project .dotnet-home/validation/polling-performance/PollingPerformance.csproj --configuration Release --no-restore --verbosity minimal -p:UseSharedCompilation=false -p:BuildInParallel=false`，退出 0。首次运行含 restore 亦退出 0。完整输出 `polling-performance.log`、`polling-performance-dispatcher.log`，最终数值 `polling-performance.json`，位于 `.dotnet-home/validation/product-ui/`。

## DPI 根因与修复

项目属性有 ApplicationHighDpiMode=PerMonitorV2，但未发现生成的 SetHighDpiMode/ApplicationConfiguration 初始化；直接以 LoadLibraryExW(DATAFILE)/FindResourceW 只读提取已安装 EXE 清单，确认无 dpiAware/dpiAwareness。未执行 EXE。源清单也缺少声明。

按[微软 WPF 示例](https://github.com/microsoft/WPF-Samples/blob/main/PerMonitorDPI/readme.md)和[Windows 清单规范](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)，在 asm.v3 application/windowsSettings 下增加 2005 dpiAware=true/pm 与 2016 dpiAwareness=PerMonitorV2, PerMonitor。只改清单，不改窗口尺寸及布局算法。

测试项目嵌入真实源清单；`ApplicationManifestTests` 检查完整层级、命名空间及逐屏设置/回退。红灯 `red-dpi-manifest-tests.log`：Assert.NotNull() Failure: Value is null，完整堆栈保留，退出 1。随后 `dotnet test NetGuardian.sln --configuration Release` 加公共参数：Core 206/206、WPF 113/113，无跳过，退出 0（`dpi-manifest-release-tests.log`）。

编译产生 WFAC010：`从 ...\app.manifest 中删除高 DPI 设置，并通过 Application.SetHighDpiMode API 或“ApplicationHighDpiMode”项目属性进行配置`。此项目使用 WPF 入口，WinForms 仅用于托盘集成，未发现 WinForms DPI 初始化调用；保留警告，不禁用分析器。增量 build 的 0 警告不能掩盖首次编译警告；另以 `--no-incremental` 重建，退出 0、2 个 WFAC010 警告（WPF 临时项目及主项目各一次）、0 错误，完整输出 `dpi-manifest-rebuild.log`。

SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe`；初始化脚本、工作区 TEMP/TMP 与既有方式一致。公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。构建 `dotnet build NetGuardian.sln --configuration Release --no-incremental` 加公共参数。已从 Release EXE 提取并断言两项 DPI 声明，退出 0，输出清单 `dpi-built-manifest.xml`；原安装清单为 `installed-01d9363-manifest.xml`。

独立只读审查无阻塞。尚未运行修复后正式 EXE、验证系统 DPI 上下文、跨屏移动或托盘跨缩放；清单声明仅补齐必要前提，不能当作全场景已通过。完整产品目标未完成。
