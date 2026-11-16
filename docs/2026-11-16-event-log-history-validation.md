# 事件日志历史与后台记录加载验证

日期：2026-11-16。

## 问题与实现范围

首页近 7 天统计读取磁盘 EventJournal，事件日志页原来只显示本窗口的内存记录，后台守护产生的断网、恢复及运行明细未进入表格。

- 新增 `PersistedEventLogReader`，只读加载最近 7 天的 `events-yyyyMM.jsonl` 和 `app-yyyyMMdd.log`，并按字节位置增量读取；支持并发写入、未完成行、跨写入 UTF-8、多行详情、跨月和文件重建。磁盘读取在工作线程执行，每 2 秒检查新增内容。
- 恢复事件提供中文摘要，保留原始 JSON；后台运行记录保留原文。检查失败按连续时段显示一次，恢复正常也保留边界，便于核对首页统计。
- 新增后台守护、恢复事件来源，沿用现有搜索、级别、来源和跟随最新。文件读取错误显示在日志范围提示中。
- 以文件位置识别已导入记录，合并本窗口写盘回读，按原始时间排序；最多保留 500 条，断网与恢复事件优先。导入不再次写盘。
- 首页最近事件读取同一集合，后台新增事件到来时刷新近 7 天统计；关闭窗口时停止日志刷新计时器。
- 未修改后台监控、恢复策略或网络操作。

涉及文件：`MainWindow.Log.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`EventLogStore.cs`、新增 `PersistedEventLogReader.cs`、`Localizer.cs`，以及 4 个新增测试文件。

## 验证环境和隔离

SDK：`E:\Environment\dotnet-sdk-8.0\dotnet.exe`（8.0.422），Release 配置。

验证基线：`05d352f94b2fc5110f22cab9a2006f940b67d37b`。工作区另一项更新协调功能正在修改，首次完整定向编译受到其尚未写完的测试影响，因此将基线和本次 10 个源码/测试文件复制到 `.dotnet-home/validation/persisted-logs/source` 隔离验证；没有改动或纳入另一项工作的文件。

## 命令与结果

以下命令从项目根目录执行，`$validation` 指 `.dotnet-home/validation/persisted-logs`。

- `dotnet restore "$validation/source/NetGuardian.sln" --configfile "$validation/source/NuGet.Config" --ignore-failed-sources --verbosity minimal`：退出码 0，5 个项目恢复成功。
- 旧日志范围/来源红测试：`dotnet test tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~PersistedLogScopeTests --verbosity minimal`，退出码 1，两项失败，准确复现只限本窗口和缺少后台来源。
- 空读取实现红测试：筛选 `FullyQualifiedName~PersistedEventLogReaderTests`，退出码 1，7 项失败，均未读取记录。
- 月文件重建边界红测试：筛选 `FullyQualifiedName~PersistedEventLogStoreTests|FullyQualifiedName~PersistedEventLogReaderTests.月文件被截断`，退出码 1，1 项失败、4 项通过；修正读取游标重置时的断网边界状态。
- 隔离定向回归：`dotnet test "$validation/source/tests/NetGuardian.App.Tests/NetGuardian.App.Tests.csproj" --no-restore --configuration Release --filter "FullyQualifiedName~Persisted|FullyQualifiedName~EventLogStoreTests" --verbosity minimal`：退出码 0，18/18 通过，跳过 0。
- 隔离完整回归：`dotnet test "$validation/source/NetGuardian.sln" --no-restore --configuration Release --verbosity minimal --blame-hang-timeout 90s --blame-hang-dump-type none --results-directory "$validation/test-results"`：退出码 0，核心 311/311、桌面 442/442，合计 753/753，跳过 0。
- 完整回归后补充统计对照并完善渲染夹具，筛选 `FullyQualifiedName~PersistedLogViewTests|FullyQualifiedName~PersistedEventLogReaderTests.二十次断网`：退出码 0，2/2 通过。构造 20 次断网、14 次恢复启动、10 次成功、4 次失败，断网/恢复事件在 1000 条正常明细下仍可查看，统计与 EventJournal 一致。
- 真实 WPF 表格验证：历史来源、按原始时间排序、搜索/来源/级别筛选、详情原文绑定、更新详情后的选择保留；1180×760 渲染已人工检查。
- `git diff --check`：退出码 0。

保留既有 WFAC010 高 DPI 警告；未修改 DPI 配置。

完整输出：`.dotnet-home/validation/persisted-logs/` 内的 `red-scope.log`、`red-reader.log`、`red-boundary.log`、`restore.log`、`green-all-log-tests.log`、`full-tests.log`、`green-statistics-and-view.log`。渲染：`preview/历史及后台日志-1180x760.png`。

## 执行中遇到的错误

- 初次沙箱测试退出码 1：`error MSB3883: 意外的异常`，`error : Access to the path 'C:\Users\Qiu\AppData\Local\Temp\MSBuildTemp\tmp1dd339a71b134e61bc97fcf474f24e15.rsp' is denied.`。通过审批后重跑能够编译并得到预期红测试结果。
- 新读取测试首次编译退出码 1：缺少 `System.IO` 引用，CS0103（Path、Directory、File、FileMode）和 CS0246（FileStream）；补充引用后得到预期 7 项失败的红测试，最终通过。
- 另一项工作的 `UpdateCoordinatorProcessTests.cs` 尚未完成时，CS0246：未能找到 `FactAttribute` / `Fact`，退出码 1。未修改该文件，改用上述独立快照验证。

## 构建与本机更新状态

源码提交后，从独立验证快照发布界面文件，并在程序集版本中写入本次提交标识。目标为 `C:\Program Files\NetGuardian` 和项目 `dist\NetGuardian`；只更新界面产品组件及界面使用的 Core 库，保留配置和后台文件。

本记录提交时，发布、本机文件替换与实际启动核验尚待执行，不据测试结果认定本机软件已更新。界面运行时，必须按 AGENTS.md 请用户手动从托盘退出，随后正常提权、备份和校验、替换、重新启动，核对两个目录哈希及后台进程身份。

### 已提交与独立发布

- 源码提交：`12fca94abbc93e86ac1c41e86d43608f74c7a153`，`在事件日志中加载历史断网恢复与后台增量记录`。
- 使用 `git archive 12fca94abbc93e86ac1c41e86d43608f74c7a153` 导出独立提交；发布目录为 `.dotnet-home/validation/persisted-logs/ui-package`，未混入工作区另一项更新协调功能和 1.1.6 版本准备改动。
- `dotnet restore <release-source>/NetGuardian.sln --configfile <release-source>/NuGet.Config --ignore-failed-sources --verbosity minimal`：退出码 0。
- `dotnet publish <release-source>/src/NetGuardian.App/NetGuardian.App.csproj --no-restore --configuration Release --self-contained false --output <ui-package> -p:SourceRevisionId=12fca94abbc93e86ac1c41e86d43608f74c7a153 --verbosity minimal`：退出码 0，保留既有 WFAC010 警告。
- 界面与 Core 程序集 ProductVersion 均核验为 `1.1.5+12fca94abbc93e86ac1c41e86d43608f74c7a153`。发布清单 7 个产品文件 SHA-256 均再次比对通过；`build-manifest.json` 保存在发布目录。
- 部署与启动核查脚本语法检查通过。预检和替换均会拒绝向本机已安装的更高版本降级，保留并核验后台文件及用户配置。
- 本机替换仍待用户从托盘手动退出界面；本节仅记录构建完成，不表示已安装或已启动新版本。正常管理员预检结果单独保存至上述验证目录。
