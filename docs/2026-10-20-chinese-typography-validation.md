# 中文字体地区字形修复

## 用户实测与本轮重点

用户对已安装 `40dcb64` 反馈：托盘新菜单可正常关闭；添加 Wi-Fi 弹窗圆角及操作可达；跨屏显示清晰。记录为用户实际检查结果，不扩展为全分辨率、全部主题或完整读屏通过。

随后用户指出“目前的字体看起来很怪，有点像繁体中文”。本轮优先修复该实际问题；高缩放小工作区的最小窗口边界仅做了调查，未改代码，仍待继续。

## 根因证据

主窗口、测速视图、托盘菜单及添加 Wi-Fi 弹窗都固定使用 Segoe UI，但没有把 WPF Language 与应用所选语言同步。当前 WPF 默认 Language=en-us。

在独立 STA 渲染诊断中，用真实 FormattedText 绘制“网络 诊断 连接 测速 恢复 设置 验证 Mbps 123”，遍历实际 GlyphRun.GlyphTypeface.FontUri：

| 字体设置与语言 | 实际字体文件 |
|---|---|
| 原 Segoe UI + en-US | 中文混合 YUGOTHM.TTC（Yu Gothic）与 MSJH.TTC（Microsoft JhengHei UI），英文 SEGOEUI.TTF |
| 原 Segoe UI + zh-CN | 中文 MSYH.TTC，英文 SEGOEUI.TTF |
| 新显式字体栈 + en-US/zh-CN | 中文 MSYH.TTC，英文 SEGOEUI.TTF |

本机安装字体枚举确认 Microsoft YaHei UI/YaHei、JhengHei UI、Segoe UI 均存在。问题是地区字形回退，不是文案被转换成繁体。

技术依据：[微软 FontFamily 回退说明](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.fontfamily?view=windowsdesktop-8.0)、[Microsoft YaHei 字体说明](https://learn.microsoft.com/en-gb/typography/font-list/microsoft-yahei)。

## 修改范围

- `Themes/Tokens.Common.xaml`：集中定义 `AppFontFamily=Segoe UI, Microsoft YaHei UI, Microsoft YaHei`，保留英文数字优先 Segoe UI，中文优先简体 UI 字体。
- `MainWindow.xaml`、`Views/SpeedTestView.xaml`、`Views/TrayContextMenu.xaml`、`AddWifiDialog.cs`：统一引用字体令牌。
- `MainWindow.xaml.cs`、`Views/SpeedTestView.xaml.cs`、`Presentation/TrayMonitorPresenter.cs`、`AddWifiDialog.cs`：WPF Language 跟随 Localizer.Language，覆盖主窗口语言切换和独立弹出内容。
- 不改文案、字号、网络操作或系统字体；图标保留 WPF-UI SymbolIcon 自身字体。

## 验证

新增 `ChineseTypographyTests.cs` 共 3 组：四种界面外壳字体及真实中文 GlyphRun 字体；中英文下弹窗、测速和托盘实际语言属性。主窗口语言入口以源码复核，不把夹具直接调用语言显示更新冒充业务主窗口端到端。

SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe`；执行 `scripts/initialize-build.ps1` 并沿用工作区 TEMP/TMP。公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令（加公共参数） | 结果 |
|---|---|
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --configuration Release --filter FullyQualifiedName~ChineseTypographyTests`（修复前） | 2 失败、1 通过，退出 1；字体未包含 YaHei，中文语言期望 zh-cn、实际 en-us |
| `dotnet test NetGuardian.sln --configuration Release` | Core 206/206、WPF 116/116，无跳过，退出 0 |
| `dotnet build NetGuardian.sln --configuration Release --no-incremental` | 退出 0，2 条既有 WFAC010、0 错误，完整输出 `chinese-typography-build.log` |

完整失败堆栈、测试及诊断输出位于 `.dotnet-home/validation/product-ui/`：`red-chinese-typography.log`、`chinese-typography-release-tests.log`、`font-glyph-diagnostic.log`、`font-glyph-preview.log`。独立预览项目 `.dotnet-home/validation/font-preview/FontPreview.csproj` 使用 `dotnet run --configuration Release --no-restore`，退出 0。图 `chinese-font-before-after.png` 左为原回退、右为修复设置，已检查；它是文字渲染比较，不是正式应用截图。

一次预览 dotnet run 在全量测试会话完成确认前启动，两者都成功退出，未发现文件冲突；后续构建按顺序执行。后续不得以这次无冲突结果为并行构建提供依据。

独立只读审查无阻塞。当前测试固定字符及 Normal 字重，依赖本机已经验证存在的微软雅黑；没有覆盖全部 Unicode、所有字重或缺少中文字体的系统。真实安装界面字体效果仍需用户复核，整体产品目标尚未完成。
