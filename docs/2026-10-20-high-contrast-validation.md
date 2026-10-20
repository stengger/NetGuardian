# 高对比主题与动态状态资源验证

## 改动及理由

- `App.xaml.cs`：系统高对比优先于显式明暗主题；订阅 WPF 高对比属性变化并在 Dispatcher 系统消息处理后重新应用，退出时解除订阅。保存的主题选择不变，关闭高对比后恢复。自动主题仍保留原库消息监听，进入高对比时移除，避免库按主题文件名误判自定义主题。未加载窗口不调用 UnWatch。
- `Presentation/AppThemePolicy.cs`：共用主题分派、调色板选择及动态画刷引用；高对比明确调用库 HighContrast，不依赖 ApplySystemTheme 的文件名分类。
- `Themes/Palette.HighContrast.xaml`：语义画刷采用动态 SystemColors；覆盖项目所用库控件的文本、表面、边框、高亮/高亮文字和禁用文本，避免固定库预设色与用户系统色混配。系统模式减少颜色区分，保留状态文字、方向箭头和行边框。
- 明暗调色板新增透明行选中边框；`MainWindow.xaml` 选中行绘制边框，高对比时可见。
- `MainWindow.xaml.cs`、`MainWindow.Quality.cs`：状态、质量及选择提示使用动态资源引用，替换取出旧画刷的快照赋值，主题改变不必等待下一次业务报告。
- `HighContrastThemeTests.cs`：10 组用例覆盖显式明暗/自动主题进入退出分派、全部语义资源、系统色动态更新、选中行边框、状态资源重解析，以及两种库预设与黑白自定义配色的四种组合。后者检查实际单元格文本/背景、普通按钮及库 Primary 按钮。

## 调查与失败记录

官方依据：[ApplicationThemeManager 4.3.0](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Appearance/ApplicationThemeManager.cs)、[SystemThemeWatcher 4.3.0](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Appearance/SystemThemeWatcher.cs)。独立 Application 诊断证实库预设画刷为固定颜色，而非系统色动态表达式。

完整输出及堆栈均保留 `.dotnet-home/validation/product-ui/`：

| 日志 | 结果及退出码 |
|---|---|
| `red-high-contrast-tests.log` | 预先建立测试，AppThemePolicy 尚不存在，6 处 CS0103，退出 1 |
| `high-contrast-tests.log` | 最初 5 项通过，退出 0；独立审查指出分派和配色覆盖不足，未据此结束 |
| `high-contrast-library-diagnostic.log`、`high-contrast-library-resources.log` | 独立夹具运行通过，退出 0；系统未开启高对比，手动调用库 HC；例如库背景 #FFFFFAEF、文本 #FF3D3D3D，Color 值均非动态表达式，不代表实际系统主题截图 |
| `high-contrast-pair-tests.log` | 5 通过、2 失败；ThemesDictionary 不能直接赋 HighContrast，IOException：找不到资源 resources/theme/highcontrast.xaml；退出 1 |
| `high-contrast-native-pair-tests.log` | 改为与管理器一致的具体 HCBlack/HCWhite 入口；5 通过、4 失败，错误地把主窗口普通按钮断言为强调按钮；期望 #FFFFFFFF 实为 #FF000000，或相反；退出 1 |
| `high-contrast-control-pair-tests.log` | 核对真实按钮类型后，分别验证普通按钮与库 Primary 按钮，9/9 通过；退出 0 |
| `high-contrast-release-tests.log` | 加入调用序列验证后 Core 206、WPF 111，通过，退出 0 |
| `high-contrast-final-tests.log` | 增加动态状态回归后 Core 206/206、WPF 112/112，无跳过，退出 0 |
| `high-contrast-final-build.log` | 全量 Release 构建 0 警告、0 错误，退出 0 |

SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe`，执行 `scripts/initialize-build.ps1`，TEMP/TMP 使用工作区忽略目录。测试命令为 `dotnet test NetGuardian.sln --configuration Release`；构建为 `dotnet build NetGuardian.sln --configuration Release`；均加 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。专项命令改项目为 `tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj` 并加 `--filter FullyQualifiedName~HighContrastThemeTests`。独立诊断使用忽略目录项目 `dotnet run --project .dotnet-home/validation/high-contrast-preview/HighContrastPreview.csproj --configuration Release`，未关闭漏洞审计。

已检查 1040×660 模拟黑白主题截图，初始深色衬底固定的问题属于夹具，已在测试中改用动态背景。图片 `high-contrast-HCBlack-black.png` 与 `high-contrast-HCWhite-white.png` 等保留于上述日志目录。独立只读复核：先前两项阻塞均已修复，未发现新增明确阻塞。

## 验收边界

没有更改用户 Windows 主题，没有启动业务主窗口，也没有模拟真实系统高对比消息进入/退出。主题策略测试是共用分派入口的回调序列，资源测试是真实控件和库资源配合模拟系统色；不能替代正式业务进程、Windows 消息链、全部悬停/禁用/焦点状态、读屏和跨 DPI 验收。库升级时必须复核覆盖资源键。

安装仍待前次 UAC 授权处理，本次未重复请求管理员权限或替换安装文件。用户原有验证文档改动不属于此提交。完整产品目标继续有效。

## 发布包续接

源码提交 `01d9363b4dbf5265408d3418d2ca981bdbc21809` 已发布到 `dist/NetGuardian-UI-Update-01d9363`。命令：`dotnet publish src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --self-contained false --no-restore --output dist/NetGuardian-UI-Update-01d9363 --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`；退出 0，完整输出 `high-contrast-publish.log`。

生成安装标识并独立复核 11 个文件 SHA-256，通过，退出 0；结果 `high-contrast-package-check.json`。既有安装仍为 `2772296710cf170e20093f0fadd2196ec7235006`，11 文件与旧清单一致。本次没有发起更新、没有建立新安装备份，不可称为已安装。

沿用已审查更新流程生成独立备份路径脚本 `install-ui-update-01d9363.ps1` 及启动包装 `run-elevated-ui-update-01d9363.ps1`，位于 `.dotnet-home/validation/product-ui/`，UTF-8 BOM；PowerShell 语法检查通过，退出 0。保留包哈希、单实例、界面进程、写权限、备份及回滚校验。安装前仍须处理前次管理员授权结果；不要反复弹 UAC。
