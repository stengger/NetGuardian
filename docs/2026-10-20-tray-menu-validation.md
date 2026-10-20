# 托盘菜单一致性整改

用户截图中的右键菜单来自默认 WinForms ContextMenuStrip，与应用主题、字号、间距和状态反馈脱节。本单元保留 NotifyIcon，改用应用 WPF 主题菜单：232 DIP 最小宽度、36 DIP 最小行高、图标与操作分组、状态摘要、禁用原因及辅助技术说明。菜单文本沿用中英文本地化，开始/停止/检测沿用原状态快照和业务入口。

## 修改范围

- `Views/TrayContextMenu.xaml` 与代码：统一菜单外观和命令分发，先关闭菜单再执行命令。
- `Presentation/TrayMenuHost.cs`：隐藏主窗口时在指针位置打开菜单，尝试聚焦弹出窗口，处理关闭与退出释放。
- `Presentation/TrayMonitorPresenter.cs`：状态摘要、菜单标题、可用性和原因统一更新。
- `MainWindow.xaml.cs`：托盘右键接入新菜单，左键双击仍显示主窗口，继续使用原业务及退出清理逻辑。
- 两个菜单测试类：保留原状态/语言检查，补充主题、布局、禁用分发、释放和真实 WPF Popup 生命周期。

## 验证记录

SDK、初始化脚本和工作区 TEMP 沿用项目设置。公共参数为 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln --configuration Release` 加公共参数 | Core 206/206、WPF 85/85，无跳过 | 0 |
| `dotnet build NetGuardian.sln --configuration Release` 加公共参数 | 0 警告、0 错误 | 0 |

红灯：初始测试因 `error CS0246: 未能找到类型或命名空间名“TrayContextMenu”(是否缺少 using 指令或程序集引用?)` 退出 1。首次 XAML 编译因 `error MC3015: “ContextMenu”或其一个基类上未定义附加属性“Popup.PopupAnimation”。` 及上游 `error MSB4181: “MSBuild”任务返回了 false，但未记录错误。` 退出 1。去除不适用的属性、保留无自定义滑入动画的模板后通过。

完整输出位于忽略目录 `.dotnet-home/validation/product-ui/`：`red-tray-context-menu.log`、`tray-context-menu-preview-tests.log`、`tray-context-menu-preview-final.log`、`tray-popup-integration.log`、`tray-menu-release-final-tests.log`、`tray-menu-build.log`。

已检查深色中文、浅色英文渲染 PNG。它们采用模拟后台状态，不是安装业务进程截图。生命周期测试在隐藏测试窗口时真正打开 WPF Popup 并确认呈现源存在，验证点击先关后分发、重开、合成 Escape 路由关闭、再次重开、释放关闭。独立只读复核未发现明确阻塞回归，并复核了新增测试覆盖范围。

## 验收边界

尚未验证真实系统托盘右键入口、原生前台焦点是否成功、硬件键盘输入、外部点击关闭以及多显示器跨 DPI 定位。呈现源非空没有额外断言其 HWND 句柄，不能声称完成原生窗口句柄验收。新增菜单与前一单元弹窗圆角仍需发布、替换安装版并核对实际显示。完整产品目标尚未完成。

## 发布与安装交接

源码提交 `bb89db912252a34c7fd2ab7a6324979c7b2ff713`，包含此前 `f274a93` 的弹窗圆角修复。`dotnet publish src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --self-contained false --no-restore` 加公共参数及独立 output 目录成功，退出 0。发布包位于忽略目录 `dist/NetGuardian-UI-Update-bb89db9`；11 个文件生成 SHA-256 清单后独立复核一致，退出 0。

更新脚本另存 `install-ui-update-bb89db9.ps1`，为新版本保留独立备份路径，不覆盖旧备份。开始前界面互斥未占用。UAC 启动返回 `Start-Process: An error occurred trying to start process 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' with working directory 'C:\Users\Qiu\Documents\Net'. 操作已被用户取消。`；后续等待语句因空进程对象返回 `InvalidOperation: 不能对值为 Null 的表达式调用方法。`。外层 PowerShell 未设置终止错误，因此进程退出码为 0，此退出码不代表安装成功。

更新器未启动，没有新备份或更新日志；安装标识仍是 `2772296`。已向用户说明并等待 UAC 处理结果，没有重复请求管理员授权。正式业务界面和本次安装验收尚未完成。
