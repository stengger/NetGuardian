# 自适应监控安装与资源实测

## 更新目标和发布准备

- 功能提交：`8b19a4afe6929585c8e73d4ec1337e61ebff5069`。
- 发布包：`C:\Users\Qiu\Documents\Net\dist\NetGuardian-Adaptive-8b19a4a`，包含界面与 guardian 子目录，`InstalledBuild.json` 记录提交及各文件 SHA-256。
- 安装目标：`C:\Program Files\NetGuardian`；保留机器配置、日志和现有任务定义。
- 更新前发现：实际界面运行于工作区 `dist\NetGuardian-UI-Update-57ba7d7`，对应功能提交的前一提交；安装目录界面标识仍为 `8034277`。实际 Guardian 从安装目录运行。
- 用户已从托盘退出旧界面。

## 验证命令

使用 `E:\Environment\dotnet-sdk-8.0\dotnet.exe`，通过现有 `scripts/initialize-build.ps1` 设置环境。

| 命令 | 结果 | 退出码 |
|---|---|---|
| `dotnet test NetGuardian.sln --configuration Release --no-restore --verbosity minimal -p:UseSharedCompilation=false -m:1` | Core222/222、WPF149/149，无跳过 | 0 |
| `dotnet build NetGuardian.sln --configuration Release --no-restore --verbosity minimal -p:UseSharedCompilation=false -m:1` | 0错误，2个NU1900警告 | 0 |
| `dotnet publish src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --self-contained false --no-build --no-restore --output dist/NetGuardian-Adaptive-8b19a4a` | 界面发布完成 | 0 |
| `dotnet publish src/NetGuardian.Guardian/NetGuardian.Guardian.csproj --configuration Release --self-contained false --no-build --no-restore --output dist/NetGuardian-Adaptive-8b19a4a/guardian` | 后台发布完成 | 0 |
| `update.ps1 -WhatIf` 与 PowerShell AST 语法检查 | 验证包提交、路径白名单及哈希；未执行安装副作用 | 0 |
| `test-update-helpers.ps1` | 从实际更新脚本提取停止/启动函数，在临时目录和任务替身验证：清理本次停止请求、保留外部请求、有停止请求时拒绝启动 | 0 |

脚本及日志位于忽略目录 `.dotnet-home\validation\adaptive-deploy`。Release测试完整输出为 `.dotnet-home\adaptive-release-tests.log`；构建、发布日志为 `release-build.log`、`publish-app.log` 和 `publish-guardian.log`。保留 NU1900 漏洞元数据源不可达及测试编译中的 WFAC010 DPI 配置警告，没有禁用检查。

更新脚本经独立只读复核，已修正停止请求残留、写入句柄妨碍守护删除请求，以及回退失败错误恢复监督等边界。脚本只在完整新包及新守护验证成功，或完整回退及旧守护验证成功后恢复监督；失败时不强制终止正在恢复的守护。

## 更新前实测

本地时间 2026-10-25 11:38:13 至 11:40:13，约120秒，每秒采样。原配置正常周期30秒，未改为90秒。

| 进程 | CPU累计秒数 | 平均整机CPU占比（16逻辑处理器） | 平均工作集 MiB | 平均私有内存 MiB |
|---|---:|---:|---:|---:|
| 界面 | 1.15625 | 0.0606% | 221.19 | 168.52 |
| Guardian | 0.140625 | 0.0074% | 35.98 | 31.62 |

数据保存在 `before.json`。首次采样在实际界面路径校验时主动退出，没有修改程序或网络；完整错误保留在 `measure-before.log`、`measure-before-attempt1.json`：`运行目标路径不匹配：C:\Users\Qiu\Documents\Net\dist\NetGuardian-UI-Update-57ba7d7\NetGuardian.exe`。按实际目标修正后采样成功。

基线脚本保存的报告包括采样开始前最后一条报告，最终比较按 `CheckedAt >= StartedAt` 且 `CheckedAt <= EndedAt` 重新过滤，不直接使用原始数组长度作为检查次数。配置哈希在部署前再次与基线核对。

## 实测口径和边界

- CPU只统计界面和Guardian两个主进程，不包括短暂的netsh/PowerShell子进程、WLAN系统服务；未测量网络字节数。
- 工作集及私有内存受进程运行时长、缓存及预热影响，重启后的短窗口不能证明长期内存收益。
- 检查次数来自每秒读取心跳的观测，不是完整网络抓包；采样期间不主动注入断网。
- 前后保持相同配置与30秒检测周期，避免把周期拉长的效果误算成检测实现本身的收益。
- 原有 `docs/2026-10-18-validation.md` 改动不纳入本次提交。

## 当前进度

发布包、更新前采样、脚本预演已完成。真实更新启动时，Windows UAC 返回取消，更新脚本没有启动，未生成更新结果或备份目录；没有执行后台停止或安装文件替换。没有自动重试管理员启动。

启动命令为 `Start-Process 'C:\Program Files\PowerShell\7\pwsh.exe' -Verb RunAs -WindowStyle Hidden ...run-update.ps1`，完整错误为：

```text
Start-Process:
Line |
   2 |  $process=Start-Process -FilePath 'C:\Program Files\PowerShell\7\pwsh. …
     |           ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     | 由于以下错误，无法运行此命令: An error occurred trying to start process 'C:\Program Files\PowerShell\7\pwsh.exe' with working directory 'C:\Users\Qiu\Documents\Net'. 操作已被用户取消。
```

外层PowerShell命令退出0并输出 `UpdateProcessId=null`；这只是非终止错误后的外层状态，不代表更新成功。实际更新脚本没有退出码，因为没有启动。

取消后通过只读CIM查询确认 Guardian 仍为 PID51500、启动时间2026-10-25 11:12:03（与基线一致），旧界面已经退出；安装目录界面标识仍为8034277。没有生成 `backup-8b19a4a` 或 `update-result.json`。

安装与更新后120秒采样仍未完成，当前只能报告更新前数据，不能报告资源节省比例。后续需用户确认重新启动管理员更新，完成后逐文件比对哈希、核对守护/监督状态及机器配置哈希，再采样并生成 `comparison.json`。
