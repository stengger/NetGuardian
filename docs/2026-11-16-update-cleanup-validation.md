# 1.1.6 更新安装包清理与协助进程诊断

日期：2026-11-16。

## 图片中的故障

本机界面为 1.1.4，失败作业的目标为 1.1.5。管理员只读诊断确认三次作业只有 request.json、复制的协调脚本和 cancel.request，没有 ready.json、result.json、备份或安装日志，更新在准备阶段中止。对应请求的受保护目录权限链核验通过。不能将退出码 1 解释成下载校验失败、安装器失败或具体权限故障。

对已取消作业执行带错误捕获的复查，返回“更新请求已取消，未开始安装”。这证明取消阻断有效，但不证明原始故障已经消失。旧版本启动器没有保存脚本的标准错误，无法还原最初退出的具体原因；本次没有宣称已修复该未知根因。

## 变更与边界

- 安装器退出码、安装后的程序集版本、后台任务恢复均通过后，清理本次安装包。
- 清理前重新核对 downloads 目录范围、目标版本文件名、权限链、重解析点和 SHA256；不批量清理旧包，不清理作业日志或回滚备份。
- 安装失败或取消保留安装包。文件被占用、摘要变化等清理失败记录 CleanupWarning，不回滚已核验的安装。InstallerRemoved 单独记录清理结果。
- 界面隐藏启动 Windows PowerShell 5.1，异步读取标准错误；无结构化结果且协助进程退出时，保存 coordinator-stderr.log，并在界面提示原始中文错误及日志位置。
- 固定启动命令通过环境变量传入脚本和请求路径，支持空格、中文、单引号路径；路径不会参与 PowerShell 表达式拼接。使用文本错误输出，避免内部 CLIXML 进入界面。
- 版本更新为 1.1.6。首次隔离验证排除日志界面相关的未提交改动。其后日志功能由另一项工作提交为 12fca94，最终构建将该已独立验证的提交与本次更新文件一起做整体验证，避免覆盖已完成的功能。

## 已执行验证

- PowerShell 7：`& .\tests\Test-UpdateScripts.ps1`，退出码 0。覆盖成功清理、安装失败保留、文件占用不回滚、变化摘要拒绝删除、目录越界和其他版本拒绝删除，以及原互斥、取消、安全停止和回滚场景。
- Windows PowerShell 5.1：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .dotnet-home\validation\update-cleanup\source\tests\Test-UpdateScripts.ps1`，退出码 0。
- 桌面定向测试：`dotnet test tests\NetGuardian.App.Tests\NetGuardian.App.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~UpdateCoordinatorProcessTests --verbosity minimal`，退出码 0，2/2 通过。实际启动 Windows PowerShell，验证中文错误和错误日志、路径字符及隐藏启动设置。
- 清理红测试退出码 1，错误为“安装和版本、任务核验通过后必须删除本次安装包。”；加入生产逻辑后通过。
- 错误输出原型测试两次退出码 1：`Assert.DoesNotContain() Failure: Sub-string found`，`Found: "CLIXML"`；改为固定文本命令并将终止错误直接写入标准错误后通过，没有删除或跳过断言。
- 首次构建受到沙箱临时目录权限限制，退出码 1：`error MSB3883: 意外的异常`，`Access to the path 'C:\Users\Qiu\AppData\Local\Temp\MSBuildTemp\tmp2cb61f55d1c74da6a677c2b95593aeca.rsp' is denied.`。将 TEMP/TMP 指向项目临时目录并经审批在沙箱外运行后完成构建。

完整回归、发布与本机部署记录在后续验证完成后追加。原始诊断、测试和构建日志位于 Git 忽略的 `.dotnet-home/validation/update-cleanup/`。保留项目既有 WFAC010 高 DPI 警告，不改动 DPI 配置。
## 完整回归

隔离源码命令：`.dotnet-home\sdk\dotnet.exe test .dotnet-home\validation\update-cleanup\source\NetGuardian.sln --no-restore --configuration Release --verbosity minimal --blame-hang-timeout 90s --blame-hang-dump-type none --results-directory .dotnet-home\validation\update-cleanup\test-results`。

退出码 0：核心 311/311、桌面 428/428，共 739/739，失败 0，跳过 0。桌面套件耗时 4 分 17 秒。`git diff --check` 退出码 0。隔离源码的六个变更文件与工作区待提交版本逐一比对 SHA256，防止验证和提交内容不一致。

本机发布包与部署尚待完成；更新前必须由用户手动从托盘退出界面。
## 与已提交日志功能整体验证

另一项工作在首轮回归期间提交日志功能 12fca94，随后仅追加发布记录 69e8922。将这些已提交功能文件加入隔离源码后，以相同完整回归命令重新验证，退出码 0：核心 311/311、桌面 445/445，合计 756/756，失败 0，跳过 0；桌面套件耗时 3 分 58 秒。完整日志为 `.dotnet-home/validation/update-cleanup/integrated-full-tests.log`。最终构建保留该功能。
## 独立发布与本机状态

- 源码提交：`c705b654aecd9e7da14457880530c18b048a3cae`，`更新成功后清理安装包并保存协助进程原始错误`。
- 使用 `git archive c705b654aecd9e7da14457880530c18b048a3cae` 导出独立发布源码，未从共享工作区直接打包。
- `dotnet restore .dotnet-home\validation\update-cleanup\release-source\NetGuardian.sln --configfile NuGet.Config` 退出码 0。
- `.dotnet-home\validation\update-cleanup\build-local-package.ps1 -SourceCommit c705b654aecd9e7da14457880530c18b048a3cae` 退出码 0；实际执行 `dotnet publish <release-source>/src/NetGuardian.App/NetGuardian.App.csproj --configuration Release --self-contained false --no-restore --output <ui-package>`，保留既有 WFAC010 警告。
- 发布目录：`.dotnet-home/validation/update-cleanup/ui-package`；界面和 Core 程序集 ProductVersion 均为 `1.1.6+c705b654aecd9e7da14457880530c18b048a3cae`。8 个待部署组件（含更新协调脚本）的 SHA256 已逐一核对通过。发布清单为 `build-manifest.json`。
- 此前管理员诊断核实界面及守护来自 `C:\Program Files\NetGuardian`。后续只读安装预检的 Windows 管理员确认被取消，未产生 Inspect-deployment.json，因此未执行替换、备份或重启，不能认定本机已更新。
- 本机更新仍待用户手动从托盘退出，并正常通过 Windows 管理员确认；之后需备份并更新 `C:\Program Files\NetGuardian` 与项目 `dist\NetGuardian` 的必要界面组件及更新脚本，保留配置和后台进程，核对哈希、启动路径及后台身份。

预检启动失败的完整错误：退出码 1，`This command cannot be run due to the error: An error occurred trying to start process 'C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe' with working directory 'C:\Users\Qiu\Documents\Net'. 操作已被用户取消。`；随后无进程对象导致 `You cannot call a method on a null-valued expression.`，无报告文件导致 `Cannot find path '.dotnet-home\validation\update-cleanup\Inspect-deployment.json' because it does not exist.`。这些属于管理员启动被取消后的派生错误，不是 .NET 工具故障；没有反复发起提权。
## 本机更新完成（2026-11-16 22:41，北京时间）

用户确认已从托盘退出后，按正常 Windows 管理员确认执行本机更新。

- Inspect、Preflight、Baseline、Apply 与文件 Verify 均退出码 0。两处安装原来均为 `1.1.4+6230e3531bcda3d5f746613898c348efe16ba57d`，均为框架依赖安装；依赖兼容预检通过。
- 两个更新目标：`C:\Program Files\NetGuardian`、`C:\Users\Qiu\Documents\Net\dist\NetGuardian`。按发布清单检查每处 8 个组件（共 16 项）；14 个文件需要替换，其余哈希本来一致。必要界面组件和更新协调脚本已更新为本次独立发布包。
- 替换前备份并逐项检查原始文件及用户配置。备份目录：`.dotnet-home/validation/update-cleanup/ui-backup-20261116-223720/`；原始/目标 SHA256 清单位于 `changes.json`。更新期间配置、两个 guardian 目录的文件哈希及后台进程身份均核验不变。
- 最终重新执行 `deploy-ui.ps1 -Mode Verify`，退出码 0，两处安装每项 SHA256 与发布清单一致。
- 最终执行 `verify-startup.ps1 -Mode Verify`，退出码 0。界面 PID 16612，实际路径为 `C:\Program Files\NetGuardian\NetGuardian.exe`，所属目录的 ProductVersion 为 `1.1.6+c705b654aecd9e7da14457880530c18b048a3cae`。
- 守护 PID 77480、实际路径及创建时间与更新前一致，未重启守护、未调用切网操作。守护状态 Healthy，最终核验时心跳年龄 3.96 秒；NetGuardian 与 NetGuardian-Watchdog 两个任务保持启用，和更新前一致。
- 最终包装执行结果 `local-update-result.json` 为 Success=true，两个核验子步骤均 ExitCode=0；详细证据为 `Inspect-deployment.json`、`Preflight-deployment.json`、`Baseline-startup.json`、`Apply-deployment.json`、`Verify-deployment.json`、`Verify-startup.json`。

本机辅助执行过程中的纠正：首轮文件检查通过，但启动核验退出码 1，完整错误为 `System.Management.Automation.RuntimeException: 更新界面未从核实目录正常启动。`。辅助进程曾使用 Start-Process -Wait 等待子进程树，调整等待方式；此时原辅助进程已经结束，没有结束界面或守护。一次后续包装读取到 ExitCode=null，返回 `System.Management.Automation.RuntimeException: 核验步骤失败：deploy-ui-Verify，退出码：`，而对应文件核验报告为 Success=true。随后改用隐藏辅助脚本直接调用原生 powershell.exe，并读取 LASTEXITCODE；最终两个真实核验均为 0。没有重新替换文件、强制结束软件或删除失败记录。

结论边界：本机 1.1.6 安装和进程路径核验完成；真实下一次在线升级尚未执行，不能将本次本机文件更新等同于原始准备失败根因已修复或在线安装包清理已端到端验收。之前失败的 1.1.5 安装包继续保留，符合失败时保留的策略。