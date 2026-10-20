# Wi-Fi 页面内容容器一致性验证

## 用户反馈与方案

用户截图显示 Wi-Fi 页功能按钮、选择提示及扫描反馈在页面背景上，而诊断等页面的本页操作位于内容卡片中。这使同层操作呈现不同归属。本次统一为一个页面内容表面：操作、反馈、周围网络与恢复优先级全部纳入与其他页相同的容器；两表以标题和间距分组，移除内部重复卡片边框和内边距，避免层层套框。

未采用只给工具栏追加一个独立卡片的方案，因为它仍不能表达本页操作、反馈与结果的共同归属。本次不调整九个操作的业务含义、事件、权限、选择行为及列表内容，不扩展为全应用重构。

## 文件与实现

- `src/NetGuardian.App/MainWindow.xaml`：WifiPanel 改为复用 SectionBorderStyle 的 Border，内部保留原 Grid；NearbyNetworksCard 和 PriorityNetworksCard 移除二级卡片样式，元素名和类型保留。页面边距、表面、边框、圆角随现有明暗及高对比资源。业务消费者按 FrameworkElement/UIElement 使用 WifiPanel，未发现 Grid 强转。
- `tests/NetGuardian.App.Tests/SmallWorkAreaTests.cs`：4 项新回归覆盖中英、明暗、1180×760 与 960×500，检查操作向上归属唯一内容表面，并与诊断页的表面色及内边距一致；按钮、提示及列表均完整位于容器内。既有首行及末列可读性用例扩展至中英文，使用实际本地化按钮、标题、说明及列头，两表各放一行样本，检查首行完整处于真实 ScrollContentPresenter 视口，横向滚动可读最后一列。
- `docs/2026-10-20-chinese-typography-validation.md`：补记用户实际回复“字体正常了”，关闭中文字体实际观感这一项；不扩大为整体产品验收。
- 产品计划台账同步当前结果与剩余验收。

## 验证证据

日志与离屏预览位于 ignored 目录 `.dotnet-home/validation/product-ui/`。

公共参数为 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`，SDK 8.0.422；初始化脚本和 TEMP/TMP 与既有验证一致。

1. `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --configuration Release --filter FullyQualifiedName~无线操作反馈和列表归属于一致的页面内容表面` 加公共参数：原实现 4 失败、0 通过，退出 1；错误为 `Assert.Single() Failure: The collection was empty`，操作栏无页面内容表面。完整堆栈 `wifi-page-surface-red.log`。
2. 实现后以 `--filter FullyQualifiedName~SmallWorkAreaTests`：13/13，退出 0，`wifi-page-surface-green.log`。独立审查未发现实现阻塞，指出英文真实行覆盖不足；补齐中英文带行场景后 14/14，退出 0，`wifi-page-surface-localized.log`。
3. `dotnet test NetGuardian.sln --configuration Release` 加公共参数：Core 206/206、WPF 138/138，共 344，无跳过，退出 0；`wifi-page-surface-all-tests.log`。
4. `dotnet build NetGuardian.sln --configuration Release --no-incremental` 加公共参数：退出 0，2 条既有 WFAC010、0 错误，完整输出 `wifi-page-surface-build.log`。未屏蔽分析器或移除 WPF 入口需要的 DPI 清单。
5. 人工查看真实 XAML 离屏图：默认尺寸中英/明暗表面、小窗口中英布局、英文带数据 960×500 首行及回到左端后的表格。截图路径 `wifi-page-surface-1180-zh-CN-False.png`、`wifi-page-surface-1180-en-US-True.png`、`wifi-page-surface-960-en-US-False.png`、`small-workarea-wifi-with-data-en-US.png` 等。模拟数据和夹具文案只用于布局压力验证，不代表正式业务进程或扫描状态验收。

## 验收边界

当前验证能证明页面容器归属一致，以及上述尺寸与文案下的按钮边界、首行和末列可达。不代表实际安装界面、真实 Wi-Fi 扫描、硬件键盘、读屏、任意高缩放或所有页面都已验收。紧凑窗口仍通过横向滚动承载完整辅助列，不以压缩到不可读宽度来消除滚动条。

质量页紧凑内容检查顺延，原有完整产品目标继续有效；本次单元通过不能宣称整体完成。

## 发布与安装

源码提交 `a076668710b00136ec9ab4b4f9d023596a67fb4e`（统一无线管理页操作与结果的内容容器）。`dotnet publish src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --self-contained false --no-restore --output dist/NetGuardian-UI-Update-a076668` 加其余公共参数，退出 0，2 条既有 WFAC010，日志 `wifi-page-surface-publish.log`。发布文件与既有 11 文件白名单一致，生成 SHA-256 安装清单、带 BOM 的管理员更新脚本，语法检查通过。

`wifi-page-surface-preflight.json` 确认互斥可用、界面进程数 0、备份目录不存在；按用户既有自动替换授权执行一次管理员更新，退出 0，日志 `install-ui-update-a076668-elevated.log`。实际安装 `C:\Program Files\NetGuardian` 的 11 文件及清单匹配当前包；唯一回退目录 `installed-ui-backup-a076668` 的 11 文件匹配上一版 `0dfe4c8`。标准启动包 `dist/NetGuardian` 同步当前界面根文件，guardian 子目录和实际安装的后台两文件哈希保持不变。已安装 EXE 清单独立检查 PerMonitorV2, PerMonitor / true/pm 通过，输出 `installed-a076668-manifest.xml`，退出 0。

按用户清理冗余旧版本的既有授权，先确认两个绝对目标均在工作区、无重解析点且无 Git 跟踪文件，再用 PowerShell Remove-Item -LiteralPath 删除 `dist/NetGuardian-UI-Update-0dfe4c8` 与内容为 `5089dee` 的 `installed-ui-backup-0dfe4c8`，共 2 目录、24 文件、17,552,982 字节。明细 `wifi-page-surface-cleanup-targets.json`。清理后当前更新包、标准完整包、实际安装与唯一上一版回退的 11 文件全部再次通过各自清单校验，结果 `wifi-page-surface-installed-check.json`，退出 0。未改配置、后台或用户原有验收文档。

安装文件已验证，但本工具没有打开或操作正式业务窗口；本次容器调整的实际运行观感仍待用户确认，不能用离屏夹具替代。
