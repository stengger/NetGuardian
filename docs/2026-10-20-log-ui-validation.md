# 事件日志筛选与阅读验证

## 修改

- `MainWindow.xaml`、`MainWindow.Log.cs`：将不可检索文本框替换为时间、级别、来源、事件列表，支持组合筛选、搜索、无匹配提示、计数、原文详情与跟随最新开关。界面只展示本窗口最近 500 条，明确导出另含磁盘保留日志及恢复事件，避免误称后台历史已加载。
- `EventLogStore.cs`：有界保留原文，显示摘要折成单行；级别及来源由调用方显式传入，不从消息文字猜测。切换语言只翻译元数据，保留原始日志。
- 各 MainWindow 操作模块：监控、诊断、网络配置分别标明来源；失败结果和异常传入警告或错误级别。新磁盘记录增加级别/来源前缀，不修改旧文件或导出路径。
- `LogReadingBehavior.cs`：用户操作垂直滚动条暂停跟随；向上滚轮、Up/PageUp/Home 同样暂停；程序滚动不误关闭开关。
- `Localizer.cs`：补齐中英文控件标签、空状态、级别、来源及会话范围说明。
- 新增日志存储、滚动行为回归；中英文 1040×660 真实主窗口 XAML 夹具检查筛选工具、100 条记录、展开 13 行原文后仍保留至少 100 DIP 列表区。等待展开过渡完成后截图，确认详情原文首行可见。

## 验证

沿用项目 SDK、初始化脚本及工作区 TEMP。公共参数：`--no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test tests/NetGuardian.App.Tests` 加公共参数 | 49/49，无跳过 | 0 |
| `dotnet test NetGuardian.sln` 加公共参数 | Core 201/201、WPF 49/49，无跳过 | 0 |
| `dotnet build NetGuardian.sln` 加公共参数 | 0 警告、0 错误 | 0 |

完整日志位于忽略目录 `.dotnet-home/validation/product-ui/log-app.log`、`log-all-tests.log`、`log-build.log`。存储测试首次红灯因类型尚未实现报 CS0246/CS0103，退出 1，保留于 `red-log-store.log`；之后实现并通过。提交前检查 `git status`、`git diff`、`git diff --check`。

独立审查发现并修复两项 P2：滚动条阅读旧日志没有停止跟随；DNS 刷新和网站检查明确失败仍标信息级别。复核未发现新增明确 P1/P2。

已人工检查英文稳定布局截图：筛选项与来源完整可读，原文从首行显示。布局数据为夹具模拟，非正式业务进程验收；没有进行真实网络变更。历史文件结构化载入、所有 DPI/主题/键盘/读屏、正式进程及安装发布仍未全部验收，完整产品目标继续有效。
