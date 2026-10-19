# 测速页面优化验证记录

## 改动

- 独立测速视图替换大仪表盘和最大速度汇总；三项指标固定顺序、数字与单位分离、窄布局堆叠、正文滚动、操作区固定。
- 阶段、每项状态、耗时、部分失败、取消、技术详情、结果复制与完整中英文文案。
- “延迟”准确命名为网站响应耗时；显示测试目标、约 6 MB 流量、采样及计时范围，明确不同于 Ping 或宽带上限。
- 服务保留原有负载和计时口径，增加阶段开始/完成、取消传播、下载流 20 秒截止时间、截断响应检测、进度节流。
- 会话模型隔离轮次及终态，失败或取消不把临时速度当作结果。
- 网络地址事件绑定触发时的取消令牌；退出取消任务，切页保留任务。
- 新增方向色与对比度检查；状态变化和复制反馈发出自动化实时区域事件，计时更新不重复播报。

## 环境与命令

SDK 为 `E:\Environment\dotnet-sdk-8.0\dotnet.exe`，8.0.422。使用既有初始化脚本，测试临时目录限定在项目忽略目录。

```powershell
$env:NETGUARDIAN_DOTNET = 'E:\Environment\dotnet-sdk-8.0\dotnet.exe'
. ./scripts/initialize-build.ps1
$env:TEMP = (Resolve-Path .dotnet-home/speed-temp).Path
$env:TMP = $env:TEMP
$env:NETGUARDIAN_PREVIEW_DIR = (Resolve-Path .dotnet-home/validation/speed-ui).Path

& $dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj --no-restore --verbosity minimal -m:1 -nodeReuse:false -p:BuildInParallel=false -p:UseSharedCompilation=false
& $dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --verbosity minimal -m:1 -nodeReuse:false -p:BuildInParallel=false -p:UseSharedCompilation=false
& $dotnet build NetGuardian.sln --no-restore --verbosity minimal -m:1 -nodeReuse:false -p:BuildInParallel=false -p:UseSharedCompilation=false
python scripts/check-contrast.py
git diff --check
```

最终结果：Core 182/182、WPF 13/13，无失败、无跳过；构建 0 警告 0 错误；两套主题 14 组颜色对均通过；差异空白检查通过。所有最终命令退出码均为 0。未引入依赖、未禁用测试。

最终完整输出位于忽略目录 `.dotnet-home/validation/speed-ui/` 的 `final-core.log`、`final-app.log`、`final-build.log`、`final-contrast.log`。

## 失败与纠正

- 初次直接解析 SDK 失败：`dotnet SDK not found. Install .NET 8 SDK, add dotnet to PATH, or set NETGUARDIAN_DOTNET to the SDK location.` 改为项目记录中的 SDK 后验证可用。
- 默认临时目录下 Core 基线有 11 项文件移动权限失败：`System.UnauthorizedAccessException : Access to the path is denied.` 栈指向 `SettingsStore.SaveCore` 和持久化恢复预算；命令退出 1。将 TEMP/TMP 定向项目目录后基线 173/173，退出 0，不修改相关产品逻辑。
- WPF 默认构建调度返回“生成失败，0 个警告，0 个错误”，退出 1；完整输出 `app-diagnostic.log`。改用单节点、禁止节点复用和共享编译后实际构建、测试均正常。
- Core 新增行为测试首次因缺少会话类型、阶段完成字段及 HTTP 构造参数失败，退出 1，完整错误 `red-core.log`；实现后定向 9/9 通过。
- WPF 新增视图测试首次因缺少视图类型失败，退出 1，完整诊断 `red-app.log`；其后测试辅助代码缺 `System.IO` 导入导致 CS0103，修正导入后通过。
- 滚动回归首次失败：`取消操作滚出了视口：422,-625,90,39`，退出 1，完整堆栈 `red-scroll.log`。将操作区移出滚动容器后通过。
- 独立审查复现异步 Progress 的顺序缺陷：同步 HEAD/下载成功、上传立即失败时，已完成结果被排队回调丢弃。新增实际服务与会话集成回归，首次因缺少进度适配器 CS0246 退出 1（`red-progress.log`），实现同步有序适配器后通过（`green-progress.log`）。

## 视觉与验收边界

- 使用真实测速 UserControl、实际 WPF-UI 主题和项目调色板离线渲染，覆盖 520、746、886 DIP，中英文、浅/深色、长数字、初始、运行、完整和部分失败状态。
- 已检查生成图的可读性、信息层级、换行、按钮与指标；长数字宽度和滚动后取消可见性有自动化断言。
- 图中数值来自测试夹具，不是真实公网测速结果。截图位于同一忽略目录，例如 `speed-complete.png`、`speed-ready.png`、`speed-746-en-US-light.png`。
- 本次没有启动正式主窗口、触发真实测速、切换真实网络或替换已安装程序；没有更改全局最小窗口尺寸，以避免影响其他页面。
- 混合 DPI 跨屏、Windows 高对比度、讲述人实际播报和真实网络取消耗时仍需现场验收；离线布局测试不能替代这些验证。网络条件变化只检测本机网络地址事件，不能保证识别地址不变的所有路由或 SSID 变化。
- 上传仍在结束后报告平均值；响应仍是两个网站的 HTTP 耗时；本次未升级为持续采样或多连接带宽测量。
