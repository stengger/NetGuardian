# 静态面板导航的键盘与辅助技术整改

## 发现与方案

原主窗口用 `PreviewMouseLeftButtonUp` 切换现有面板，导航项未设置 `TargetPageType`。WPF-UI 4.3.0 的按钮激活不会为此类项设置选择；其 UIA 选择入口会进入 Page 导航并抛出目标类型为空异常。因此鼠标可显示页面不能证明键盘或辅助技术可用。

保留现有九个面板、图标、侧栏和控件库模板，不改网络业务。新增 `PanelNavigationView.cs`：派生原控件，为静态面板维护唯一选中项；按钮激活和 UIA Select 共用选择方法，拒绝禁用、未知目标；复用原 Activate/Deactivate 和样式。根 peer 提供单选容器，直接暴露九个真实项目 peer，同时保留菜单按钮及滚动条。

新增 `PanelNavigationPresenter.cs`，抽出原先隐藏/显示面板及标题的纯显示逻辑，主窗口与测试共用。主窗口从选择事件显示面板，初始化明确选中首页；切换语言时重绘当前页面标题。移除鼠标专用接线。库的空 Page Frame 通过派生模板扩展点折叠，避免其空白滚动条进入侧栏。

## 证据与失败诊断

完整 dotnet 输出位于忽略目录 `.dotnet-home/validation/product-ui/`，失败均未跳过、删除断言或改业务来制造通过。

| 记录 | 事实与退出码 |
|---|---|
| `red-panel-navigation.log` | 原代码 4/4 失败。UIA Select：`System.InvalidOperationException: The viewItem.TargetPageType property cannot be null.`，堆栈为 GetNavigationItemInstance → NavigateInternal → OnNavigationViewItemClick → ISelectionItemProvider.Select。Enter/Space：`Assert.Same() Failure`，期望设置项、实际 null。退出 1 |
| `panel-navigation-first-tests.log` | CS0103：当前上下文中不存在名称 GetNavigationParent；MSB4181 上游构建失败。退出 1 |
| `panel-navigation-runtime-tests.log` | CS0117：NavigationView 未包含 GetNavigationParent 的定义；MSB4181。库内部方法不属于可调用 API，改为在 Loaded 和显式选择时关联固定项，不反射库内部实现。退出 1 |
| `panel-navigation-activation-tests.log` | 2 通过、2 失败，键盘事件创建时 inputSource 为 null。派生控件未取得隐式样式，显式动态引用原类型样式后 4/4 通过（`panel-navigation-styled-tests.log`）。退出 1 → 0 |
| `panel-navigation-container-tests.log` | 基类根控件没有创建 peer，`Assert.NotNull() Failure: Value is null`。新增根选择容器 peer。退出 1 |
| `panel-navigation-complete-tests.log`、`panel-navigation-provider-layout-tests.log`、`panel-navigation-uia-connected-tests.log` | 6 通过、1 失败，provider 链仍为空；没有放宽为仅检查数组长度。停止反复改实现，检查 .NET 根连接机制并采集真实树。各退出 1 |
| `panel-navigation-uia-tree-diagnostic.log` | 临时诊断缺少 System.IO 导入，File/Path CS0103。退出 1 |
| `panel-navigation-uia-tree-diagnostic-runtime.log` | 自有窗口 WM_GETOBJECT 返回 65536，原生根已建立；诊断树显示 ItemsControlWrapper 下的 ItemsControlItemAutomationPeer 替换了真实导航项 peer。完整树当时输出在聊天工具记录。退出 1 |
| `panel-navigation-uia-provider-linked.log` | 根 peer 改为暴露实际项目，provider 双向链单项通过。退出 0 |
| `red-panel-navigation-empty-frame.log` | 两组均发现仍可见的 NavigationViewContentPresenter；通过受保护模板属性折叠后通过。退出 1 |
| `red-panel-navigation-presenter.log` | 新 Presenter 尚不存在，CS0103；实现共用纯显示方法后通过。退出 1 |

技术依据：[WPF-UI 项目激活与 peer](https://github.com/lepoco/wpfui/tree/4.3.0/src/Wpf.Ui/Controls/NavigationView)、[.NET 8 AutomationPeer 连接校验](https://github.com/dotnet/wpf/blob/v8.0.0/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Automation/Peers/AutomationPeer.cs)。仅创建 peer 不证明 provider 已连接。测试只向夹具自己的 HWND 发送 WM_GETOBJECT，不操作用户应用。

## 最终验证

SDK `E:\Environment\dotnet-sdk-8.0\dotnet.exe`，初始化脚本与工作区 TEMP/TMP 沿用项目设置。公共参数 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln --configuration Release` 加公共参数 | Core 206/206，WPF 99/99，无跳过 | 0 |
| `dotnet build NetGuardian.sln --configuration Release` 加公共参数 | 0 警告、0 错误 | 0 |

日志为 `panel-navigation-release-tests.log`、`panel-navigation-release-build.log`。

8 组导航用例验证：Enter/Space 路由激活及逻辑焦点保持；UIA 选择不进入 Page 导航；真实根连接后 SelectionItem/Selection 双向协议、非空选中 provider、九项各出现一次；中英九项唯一选择、唯一可见面板、正确标题、禁用/未知目标无状态变化；按钮 OnClick 入口与语言重绘。纯显示方法与主窗口共用，没有实例化含网络副作用的业务 MainWindow。

已检查 1040×660 展开中文与收起英文布局预览，确认空 Frame 滚动条消失。收起预览等待库动画 400ms 后检查宽度小于 80 DIP；不能据动画中间帧判断文字被永久裁剪。侧栏预览采用测试标签和状态，不是正式进程截图，也不是全设置页内容验收。

独立只读审查未发现阻塞问题；审查提出的真实页面/标题同步及 provider 验证缺口已补充。

## 验收边界与风险

当前自动化树针对固定九项平面导航，未来动态、嵌套项或库模板升级须重新评估。保留菜单滚动条 peer，没有为整个导航额外承诺完整 Scroll pattern。键盘路由事件和反射调用 OnClick 只证明控件入口，未验证硬件输入、真实指针、讲述人端到端、系统高对比或跨屏 DPI。

安装版仍待前次被取消的 UAC 授权处理；本轮未重复弹出管理员请求。源码及夹具通过不等同完整商业软件目标完成，台账继续有效。
