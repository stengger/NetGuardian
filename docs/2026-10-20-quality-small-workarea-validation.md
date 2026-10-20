# 质量页紧凑窗口阅读验证

## 问题与改动

接续 Wi-Fi 容器一致性修复，核对质量页在较小工作区的实际内容布局。原布局的说明、状态和三组协议指标均为 Auto 行，明细表只占剩余高度。新增 960×500 中英真实文案用例后，表格实测仅 7 DIP，不能阅读。

推荐保留顶部标题、检查／取消／系统设置操作和活动反馈，将说明、指标、探针明细及结果说明放入可滚动正文。相较缩小字体、隐藏指标或固定压缩概览，这样保持原阅读顺序与信息语义。正文 Grid 跟随视口高度，设置 400 DIP 最小内容高度，保证目标紧凑窗口可滚至明细；表格仍在有限高度内布局，未改为无限测量或一次呈现全部行。该最小高度针对已测文案，不是任意长文本的通用承诺。

- `src/NetGuardian.App/MainWindow.xaml`：固定操作区与 QualityBodyScrollViewer 正文分离，说明移入正文；复用现有字号、语义、控件和状态通知，保留探针表内部滚动以及长结果说明的阅读区域。
- `src/NetGuardian.App/Presentation/ScrollWheelChaining.cs`：仅质量正文启用的附加行为。内部滚动区到相应边界、正文仍可滚时，将滚轮交给正文的标准 WPF MouseWheel 处理；不接管内部正常滚动、零增量或带修饰键的输入。关闭属性时解绑处理器，没有静态窗口引用或后台任务。
- `tests/NetGuardian.App.Tests/DesktopLayoutTests.cs`：既有质量场景增加 960×500 中英，保留 1180/1040×660；验证指标与明细可滚入视口、实际首末行、阅读后固定取消操作可达。补表格到顶／到底交接、内部正常滚动及附加行为关闭／重启。离屏夹具同时修正自定义附加属性的展开命名空间，指向真实 NetGuardian 程序集，没有移除或替换生产接线。
- 本验证记录及产品计划台账记录结果和未验收边界。

## 红绿证据

日志在 `.dotnet-home/validation/product-ui/`，完整输出及退出码保留；离屏图使用真实页面 XAML、模拟结果和本地化文案，不是正式业务进程截图。

1. `quality-small-workarea-red.log`：新增尺寸前的实现，2 失败、3 通过，退出 1；两项小窗口错误均为 `QualityProbeGrid 不可达：262,421,657,7`。
2. `quality-small-workarea-green.log`：固定操作与滚动正文后 5/5，退出 0。`quality-small-workarea-row-navigation.log` 补首末行与固定取消后 5/5，退出 0。中间全量 `quality-small-workarea-all-tests.log` 为 Core 206/206、WPF 140/140，退出 0。
3. `quality-scroll-chaining-red.log`：新增滚轮边界测试 2 失败、3 通过，退出 1；错误为“表格到底后，向下滚轮未衔接正文。”。
4. `quality-scroll-chaining-green.log`：第一次加载附加行为时夹具 XAML 解析 5 失败，退出 1。完整错误与堆栈在日志，主错误 `XamlParseException / XamlObjectWriterException：无法设置未知成员“{clr-namespace:NetGuardian.App.Presentation}ScrollWheelChaining.IsEnabled”`。编译已成功，此错误是离屏 XAML 的附加属性未跟随 xmlns 改写，不是运行时或编译器不可用。修正夹具展开名称后 `quality-scroll-chaining-verified.log` 5/5，退出 0。
5. `quality-small-workarea-final-tests.log` 全量 Core 206 通过，WPF 139 通过、1 失败，退出 1；英文 960×500 再次出现“表格到底后，向下滚轮未衔接正文。”，完整堆栈在日志。随后补充修饰键、内外偏移、启用状态现场诊断，针对性 `quality-scroll-chaining-diagnostic.log` 5/5，退出 0；尚未取得失败现场，不能把实时输入状态或浮点边界当成已确认原因，也不能据重跑通过宣布偶发问题已解决。
6. 带诊断的全量 `quality-small-workarea-verified-tests.log`：Core 206/206、WPF 140/140，共 346，无跳过，退出 0。前次偶发失败没有再次复现，仍保留其未知原因和完整日志。构建见 `quality-small-workarea-build.log`。

## 命令与截图

SDK 8.0.422，先执行 `scripts/initialize-build.ps1`，TEMP/TMP 指向既有 workspace product-temp。公共参数：`--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

- `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --configuration Release --filter FullyQualifiedName~质量检查分组指标与取消操作在长文案下仍可读取` 加公共参数：上述针对性红绿日志。
- `dotnet test NetGuardian.sln --configuration Release` 加公共参数：最终全量日志。
- `dotnet build NetGuardian.sln --configuration Release --no-incremental` 加公共参数：退出 0，2 条既有 WFAC010、0 错误；未禁用分析器或移除 WPF 入口所需的 DPI 清单，见构建日志。
- 已实际查看中英紧凑完成态和常规尺寸离屏图。新增 `quality-complete-initial-960-500-en-US.png` 明确先 ScrollToTop 保存初始正文视口，避免把 BringIntoView 后的滚动状态当成初始布局；原 `quality-complete-960-en-US.png` 等属于阅读后的视图。

独立审查首先确认视口绑定未见无限高度反馈、原事件和 UIA peer 名称仍保持；指出嵌套滚轮边界及首末行证据不足，已按上述测试复现并补正。新增附加行为最终只读复核未发现明确阻塞：原事件只处理一次，新事件不走 Preview 不递归，默认不影响其他页，开关可解绑，夹具保留真实接线。审查没有运行测试，不能替代父任务全量结果；从实际单元格发起及硬件输入仍待验。

## 未完成

以上验证是目标尺寸的真实 WPF 离屏窗口和路由事件，不能替代硬件滚轮、触控板、键盘、读屏及真实多屏 DPI 验收。截图含模拟大数值与样本探针，只用于压力布局，不证明网络质量结果本身。更极端的长摘要、实际高对比消息、其他页面的小工作区仍待核查。整体产品目标继续有效，不因质量页阶段通过而标完成。
