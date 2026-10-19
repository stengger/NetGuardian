# 连接质量数据与交互整改验证

## 改动及设计理由

- `Core/Connectivity/ConnectivityQualitySummary.cs`：网关 Ping、DNS、HTTPS 分开统计；仅将成功、有限且非负的计时计入各组均值。分别保留尝试数、成功数、有效计时数；没有样本用空值，合法 0 ms 保留。不能通过异类平均推断网络延迟。
- `MainWindow.xaml`：质量页移除百分制评分与混合平均延迟，改为连接结论、检查通过数、三组耗时及样本。摘要和操作固定，明细占剩余空间；长错误可滚动；取消与真实执行活动指示只在运行期间显示。
- `MainWindow.Quality.cs`：独立管理质量页读取、运行、取消、错误和重绘，复用注入的连接检测器；令牌传递到 Wi-Fi 与探针。结束前检查取消；切语言从保存会话重绘，不丢结果；退出取消，结束后不刷新已清理窗口。状态改变发出实时区域自动化事件。
- `Presentation/QualityCheckSession.cs`：隔离轮次，开始清除旧报告与错误，阻止重复启动；取消中的迟到成功不能成为结果；终态不接受旧事件。
- `MainWindow.xaml.cs`：移除旧评分计算及展示；诊断报告改为健康结论与明确标注的检查通过数，避免另一个页面继续输出旧评分。
- `Localizer.cs`：中英文说明检查范围：无线基础与网关基于 Wi-Fi，DNS/HTTPS 使用 Windows 当前出口，不保证请求绑定无线接口，不代表游戏 Ping；结果表叫“探针耗时”，避免把超时等待称作成功响应延迟。
- `ConnectivityQualitySummaryTests.cs`、`QualityCheckSessionTests.cs`、`DesktopLayoutTests.cs`：覆盖协议隔离、失败/无效样本、零与未知、取消/重试/旧轮次，以及中英文紧凑窗口。

## 验证证据

沿用项目 SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe` 与 `initialize-build.ps1`，TEMP/TMP 为 `.dotnet-home/product-temp`。公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test tests/NetGuardian.Tests/NetGuardian.Tests.csproj` 加公共参数 | Core 201/201 | 0 |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj` 加公共参数 | WPF 34/34 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |
| 最终 `dotnet test NetGuardian.sln --no-build` 加公共参数 | Core 201 + WPF 34 全过，无跳过 | 0 |

完整输出在忽略目录 `.dotnet-home/validation/product-ui/quality-{core,app,build,all-tests}.log`。提交前执行 `git diff --check`，仅暂存本单元相关文件；不提交用户已有文档修改。

测试先运行时因尚无汇总/会话类型分别出现 CS0103、CS0246，退出 1；会话类型尚不存在时还产生 Throws 重载的 CS0619 级联诊断，实现同步 Begin 后消失。随后 3 项汇总和 2 项会话行为通过，最终全量通过；没有跳过或删除断言。

独立只读审查核对了真实调用链、令牌传递、取消优先与终态、切语言和退出，无新增明确 P1/P2。可选补强项：请求取消后依赖抛非取消异常时，当前诚实显示“检查未完成”，没有完整结果；正常取消链返回 OperationCanceledException。

## 视觉与验收边界

真实 MainWindow XAML 夹具输出并检查了 `quality-running-1040-zh-CN.png`、`quality-running-1040-en-US.png`、`quality-complete-1040-en-US.png` 等。进行中以“—”表示尚无样本；完成态使用模拟大数值、长 SSID、12 行模拟探针压测排版。字段、取消按钮和明细高度均有断言；没有使用截图数据作为真实网络结果。

尚未执行正式进程中的真实检查、取消和语言切换，未完成讲述人、高 DPI 与系统高对比度验收。DNS/HTTPS 仍遵循系统路由；未把底层检测器改成以太网通用诊断。普通页面空/错状态、日志、流量口径、托盘与安装发布等总目标事项仍需继续。
