# 手动网络操作流程：本机更新与验收

2026-11-09 17:16（北京时间）完成。源码提交为 `4db35beeb10f425fb186578166e56329142da0fc`，其中包含后台交权提交 `ad31766`。本记录与后续规则提交只涉及文档，不改变已安装二进制。

## 构建与发布

使用 `git archive 4db35be` 提取隔离源码，避免混入工作区内容。通过仓库 SDK 8.0.426 执行现有 `scripts/publish.ps1 -OutputDirectory …/package`，完整 Release 回归 Core 278 项、App 164 项，合计 442 项通过，0 失败、0 跳过，发布退出码 0。Debug 完整回归同样为 442 项通过。保留原有两条 WFAC010 高 DPI 清单警告，未在本轮扩大修改范围。

构建标识为 `1.0.0+4db35beeb10f425fb186578166e56329142da0fc`。发布包依赖本机已有的 .NET 8 桌面运行时。

## 更新方式与结果

用户从托盘手动退出旧界面后，管理员预检确认界面不再运行；核实后台进程和两个计划任务都归属于当前安装目录。备份待替换文件、配置、任务 XML 与原启用/运行状态，逐个校验备份哈希。

更新期间临时禁用原主任务和监督任务，通过 `stop.request` 请求后台正常退出，等待已发出的网络操作收尾。没有按名称强杀进程，没有删除或重建计划任务。安装目录与项目发布目录合计替换 38 个差异文件，两处目录各 27 个发布文件的 SHA-256 与隔离发布包一致；未删除包外文件，未写入用户配置。

| 项目 | 验收结果 |
| --- | --- |
| 实际安装目录 | `C:\Program Files\NetGuardian` |
| 项目启动器使用目录 | `C:\Users\Qiu\Documents\Net\dist\NetGuardian` |
| 备份 | `.dotnet-home/validation/manual-network/deployment-backup-20261109-171130/` |
| 构建清单 | 两处目录的 `InstalledBuild.json`、`UIInstalledBuild.json` 均标记 `4db35be` |
| 界面 | 重新从安装目录启动，PID 75540，主窗口 `NetGuardian`，可响应，启动期间没有新增崩溃日志 |
| 后台 | 从安装目录的 `guardian` 子目录启动，PID 69676，协议版本 1，最终心跳 `Healthy`、失败计数 0 |
| 计划任务 | 主任务 `Running`、监督任务 `Ready`，两者均恢复原来的启用状态 |
| 用户配置 | 部署与启动后字节哈希均与备份一致；恢复策略及原 57 条网络的优先级、启用/自动连接、最低信号和备注均保留 |

关键组件校验如下，Core 在界面与后台目录中的哈希一致：

| 文件 | SHA-256 |
| --- | --- |
| `NetGuardian.dll` | `15AA2B7D8C5C02AEBDC852691106B65749C60FD39822D0B2909C75DCBB5C1FB6` |
| `NetGuardian.Core.dll` | `CF02031C917FC19BC6DAA86E583926A06684CA68E5F55079CE9623C1DEED50CD` |
| `guardian/NetGuardian.Guardian.dll` | `DC57A89F248EE97DDB67CD2DF06B495D3E1E813DA282CAE06A85A9BF37514344` |

## 真实后台交权验收

以已安装 Core 编译本机验收程序，在原后台运行时申请 `ManualNetworkAccess`，没有调用连接、DNS、DHCP 或网络重置命令。

- 后台完成收尾后进入 `ManualControl`，手动操作取得独占锁。
- 第二个手动操作收到 `Busy`；其他控制者无法取得同一监控锁。
- 持锁 6 秒跨过请求续期间隔，后台心跳持续更新，后台 PID 始终为 69676，未被监督任务重启。
- 释放后临时请求被撤销，同一个后台恢复为 `Running`，下一次检测安排在约 30 秒后。
- 验收总耗时约 10 秒，退出码 0，配置保持不变。随后启动核验确认后台已恢复 `Healthy`。

## 预检问题与处理

初版临时部署脚本保存为 UTF-8 无 BOM，PowerShell 7 的语法检查通过，但 Windows PowerShell 5 按系统代码页读取中文后发生解析错误。首次管理员预检退出码 1，随后为收集完整错误直接运行同一脚本也退出码 1；这两次都在解析阶段结束，没有替换安装文件。改为 UTF-8 BOM 后，使用实际运行版本 `powershell.exe` 的解析器检查通过，管理员预检和部署均退出码 0。

完整失败命令、原始错误和退出码保存在本机 [deployment-preflight-errors.log](../.dotnet-home/validation/manual-network/deployment-preflight-errors.log)。此问题属于临时脚本编码，不是 SDK 或测试框架故障。原来的管理员进程只读权限问题见[体验报告](2026-11-09-workflow-experience-review.md)。

旧管理员界面没有响应自动化关闭，已停止尝试并由用户手动退出。按用户要求，`AGENTS.md` 记录后续更新前直接请用户从托盘退出，不使用界面自动化代为关闭。

## 证据与边界

完整输出留在本机忽略目录 `.dotnet-home/validation/manual-network/`：`isolated-release-publish.log`、`deployment.log`、`deployment-result.json`、`live-compile.log`、`live-handoff.log`、`live-handoff-exit.json`、`installed-verification.json`。备份含用户设置，不纳入 Git。

本轮已完成部署和不切网的真实后台验收。真实切换到其他 Wi-Fi、校园网网页认证、信号消失以及远程连接中断后的全流程尚未人工验收；没有将模拟测试或本次交权验证视为这些场景已通过。
