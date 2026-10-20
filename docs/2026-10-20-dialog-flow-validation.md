# 添加 Wi-Fi 弹窗完整交互验证

## 问题与修改

此前圆角修复只验证按钮边界，不能证明正文、说明和错误可见。本轮保持业务字段和返回结果含义，限于 `AddWifiDialog.cs` 与对应测试：

- 默认高度由 430 调至 520 DIP，普通中英文表单无需滚动；保留 430 DIP 紧凑回归，正文可滚动，操作区固定。
- 初始焦点进入 SSID。空值提交不关闭弹窗，错误就近显示在 SSID 下方；有效编辑清除错误及辅助说明。
- 输入关联可访问标签，密码有帮助说明；校验错误标记 polite live region 并创建 peer 后请求通知。
- 颜色动态引用现有主题资源。FluentWindow 在初始化、移除背景特效时重设 Background，派生窗口在基类完成后恢复表面令牌。
- 保存和取消使用现有 WPF-UI Primary/Secondary 按钮，复用其悬停、按下、禁用与焦点模板，避免手拼强调色和中性按钮模板混用；操作按钮使用最小高度，保留自动测量。

## 验证范围

实际调用模态 `ShowDialog()`，通过控件路由点击验证：初始焦点、滚到底后空值提交仍打开、SSID 焦点和错误视口范围、首次错误 peer、有效编辑清错、保存返回 true、取消返回 false。没有执行 Wi-Fi 配置保存、真实连接或监控。

10 组弹窗用例覆盖中英文、明暗主题资源、动态字典替换、430 DIP 紧凑正文及 14/20 字号设置下布局、固定操作区与内容测量、DWM 圆角偏好。控件库部分控件有独立字体资源，所以 20 字号设置不等同所有控件或系统文本缩放验证。

独立只读审查提出首次错误 peer 缺失问题，已修复，并补充红绿证据；滚到底提交路径也已覆盖。普通 WPF 内容位图不包括系统合成边角，不能用截图证明原生圆角像素。

## 红灯与诊断记录

完整输出保留工作区忽略目录 `.dotnet-home/validation/product-ui/`。

| 日志 | 结果及退出码 |
|---|---|
| `red-add-wifi-flow.log` | CS0104：PasswordBox/TextBox/TextBlock 在 WPF 与 WPF-UI 间引用不明确；补测试类型别名后继续。退出 1 |
| `red-add-wifi-flow-runtime.log` | 正文 ScrollViewer 缺失 `Assert.Single() Failure: The collection was empty`；初始 SSID 焦点失败。退出 1；另有 xUnit2031 警告，已用 Assert.Single 筛选重载修正 |
| `red-add-wifi-error-visibility.log` | `SSID 校验提示被正文裁剪：0,313,424,19 / 294`，移至字段下方后通过。退出 1 |
| `red-add-wifi-theme-binding.log` | 深色期望 #FF272C33、实际 #FF202020，浅色期望 #FFFFFFFF、实际 #FFFAFAFA；初始化背景恢复和动态资源修复。退出 1 |
| `add-wifi-flow-preview.log` | 测试清空全部资源字典导致模板动画 `ArgumentNullException: Value cannot be null. (Parameter 'defaultDestinationValue')`；改为按产品方式替换主题及配色字典，保留控件模板和令牌。退出 1 |
| `red-add-wifi-normal-fit.log` | 普通表单可滚动高度期望 0、实际 56；默认窗口增高后通过。退出 1 |
| `red-add-wifi-live-peer.log` | 首次错误 `Assert.NotNull() Failure: Value is null`；改用 CreatePeerForElement。退出 1 |
| `red-add-wifi-primary-contrast.log`、`add-wifi-primary-final.log`、`add-wifi-final-accent-preview.log` | 原手拼按钮深色黑字/深蓝底；改成熟按钮后，无 Application 的夹具出现浅色白字/透明底；单补颜色键仍失败。三轮均退出 1，随后停止尝试并转独立诊断，没有删除对比度断言 |
| `add-wifi-application-diagnostic.log` | 临时诊断工程相对项目路径多一级，CS0246，退出 1；修正路径后运行 |
| `add-wifi-application-diagnostic-runtime.log` | 真实 Application 初始化主题/强调色后，Dark 黑字/#73C2FF，Light 白字/#006ABB；已检查两张 PNG。退出 0；还原有 NU1900 `获取包漏洞数据时出错: Access to the path is denied.` |
| `add-wifi-restore-http-cache.log` | 首次还原退出 1、日志为空；按单节点重新采集输出 |
| `add-wifi-restore-http-cache-diagnostic.log` | 使用工作区 NuGet HTTP 缓存路径及单节点还原成功，漏洞数据 HTTP 200，0 警告、0 错误，退出 0；没有禁用审计 |

强调色故障不能直接推断为正式进程缺陷：WPF-UI 的共享画刷需要 Application 资源，夹具未运行启动过程。独立诊断 host 创建 Application、加载同套字典并运行库的主题/强调色初始化，证明该环境下实际配对正常；没有构造业务 MainWindow。无 Application 的单元夹具最终显式提供蓝色样本画刷，保留对比度断言。样本颜色通过不证明任意系统强调色均通过。

根因依据：[FluentWindow 初始化](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Controls/FluentWindow/FluentWindow.cs)、[背景恢复实现](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Controls/Window/WindowBackdrop.cs)、[强调色 Application 资源初始化](https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Appearance/ApplicationAccentColorManager.cs)。

## 最终验证

SDK 为 `E:\Environment\dotnet-sdk-8.0\dotnet.exe`。初始化 `scripts/initialize-build.ps1`，TEMP/TMP 使用 `.dotnet-home/product-temp`。公共参数为 `--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 最终结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln --configuration Release` 加公共参数 | Core 206/206、WPF 91/91，无跳过 | 0 |
| `dotnet build NetGuardian.sln --configuration Release` 加公共参数 | 0 警告、0 错误 | 0 |
| `dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --configuration Release --filter FullyQualifiedName~添加无线表单正文` 加公共参数 | 追加正文说明横向边界断言，4/4 通过 | 0 |

最终输出：`add-wifi-flow-release-recheck.log`、`add-wifi-flow-release-build.log`。已检查常规深浅、中英文与紧凑 20 字号预览；真实 Application 诊断输出为 `add-wifi-application-Dark.png` / `add-wifi-application-Light.png`，只是独立 UI 诊断窗口。

前一轮全量 `add-wifi-flow-release-final-tests.log` 曾有 Core 1 项失败（205 通过）、WPF 91 项通过，退出 1：`SettingsReliabilityTests.结构损坏的配置回退最近有效备份` 在 `profiles:null` 样本的 `SettingsStore.SaveCore` 第 82 行 `File.Move` 返回 `System.UnauthorizedAccessException: Access to the path is denied.`。该测试使用工作区 TEMP 下的 GUID 目录；本轮未修改 Core 或该测试。单独复查此类 4/4 通过，退出 0（`add-wifi-settings-reliability-recheck.log`），随后完整复查通过。一次访问错误的外部原因仍未确定，不能据重跑通过声称已修复，也不能将其误判为运行时或框架不可用。

源码提交为 `8819e5da903f58e29446e8855b0ee37ccd54b372`。`dotnet publish src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --self-contained false` 加公共参数及独立 output 成功，退出 0（`add-wifi-flow-publish.log`）。新包在忽略目录 `dist/NetGuardian-UI-Update-dialog-flow`，11 个文件生成清单后独立 SHA-256 复核通过。新更新脚本 `install-ui-update-8819e5d.ps1` 使用新备份目录、保留 UTF-8 BOM，语法解析通过。没有启动管理员更新。安装版仍为 `2772296`，当前 11 个文件与旧安装标识一致，退出 0。

## 未完成验收

真实安装进程、Wi-Fi 凭据写入、硬件键盘 Enter/Escape/Tab、关闭及拖动、讲述人实际通知、系统主题消息和跨屏 DPI 尚未完成。peer 存在和代码请求事件，不等于读屏已播报；资源字典切换不等于系统设置切换。管理员安装仍停留在上次 UAC 被取消，未重复弹出请求。全目标保持未完成。
