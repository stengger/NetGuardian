# 侧栏接口流量统计验证

- `NetworkTrafficSampler.cs`：按接口 ID 差分并以单调时间计算区间平均；新增、恢复读取或计数回退先建基线，接口消失不再冲抵其他接口的真实增量。无有效样本返回未知，合法零增量保留零。
- `MainWindow.xaml.cs`：替换全接口累计值相减；无有效样本显示“—”并清曲线。提示本轮计入/可读取接口数，新增或重置计数等待下一次采样。工具提示限宽换行，设置自动化帮助文本并复用提示控件。
- `MainWindow.xaml`、`Localizer.cs`：可见标题与副标题明确本机接口合计及字节/秒；提示包含局域网和虚拟接口、可能重复计数，不等同互联网带宽或测速。1024 换算对应 KiB/s、MiB/s，折叠时使用 Ki/Mi。
- `NetworkTrafficSamplerTests.cs`、`DesktopLayoutTests.cs`：接口新增/消失/计数重置/读取中断与零增量、折叠数值和单位，以及中英文紧凑窗口侧栏范围文本布局。

沿用项目 SDK 和工作区 TEMP。公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln` 加公共参数 | Core 203/203、WPF 56/56，无跳过 | 0 |
| 增加可见范围文本后 `dotnet test tests/NetGuardian.App.Tests` 加公共参数 | WPF 58/58，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |

完整日志：忽略目录 `.dotnet-home/validation/product-ui/traffic-tests.log`、`traffic-app.log`、`traffic-build.log`。首次测试因 NetworkTrafficSampler 未实现报 CS0246，退出 1，完整输出保留 `red-traffic-sampler.log`。独立只读复核未发现新增明确 P1/P2；中英文截图已生成并检查英文侧栏可读。

曾怀疑原折叠格式表达式 switch 优先级有问题，实际通过 Add-Type 运行原表达式得到 `512B` 与 `1.0K`，证伪此判断。新增括号只明确分组，不属于缺陷修复；真实变更是准确的二进制单位。

采样仍是操作系统多接口计数合计，不能消除虚拟接口重复计数，也不测单一连接的互联网吞吐。此轮没有真实流量性能、完整读屏或正式业务进程验收，产品完整目标尚未完成。
