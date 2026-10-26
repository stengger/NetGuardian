# Windows Setup 安装包验证

## 交付范围

- `installer/NetGuardian.iss`：稳定 AppId、管理员安装、目录选择、开始菜单、可选桌面快捷方式、系统卸载登记。安装或升级先检查进程和后台任务；不强制终止应用；不自动启用后台。
- `scripts/installer-maintenance.ps1`：核对程序路径及根任务文件夹内的后台归属；拒绝仍注册的后台/监督任务（包括 Ready）；卸载复用安全停止流程，失败时向安装器返回非零并阻止删除。SYSTEM 后台限定 Program Files 子目录。
- `scripts/package-support.ps1`、`publish.ps1`：支持指定 RID 和自包含发布；发布目录附带 README、启动入口及必要脚本，中文脚本写成 UTF-8 BOM。
- `scripts/build-installer.ps1`：先执行脚本回归及 Release 完整测试，使用全新暂存目录发布 win-x64 自包含版本，再编译 Setup 和生成 SHA-256 文件。
- `register-netguardian-task.ps1`：在受支持的自选安装目录内注册后台，不将程序复制到另一个目录；打包新增维护脚本。
- `unregister-netguardian-task.ps1`：查询、停止及注销均限定根任务文件夹，避免触及其他文件夹的同名任务。
- 两组 PowerShell 测试覆盖上述边界；README 记录安装、升级、卸载、构建和 Release 附件方法。

## 工具和产物

编译器为 Inno Setup 7.1.0 x64。通过官网下载链接获取，Authenticode 状态 Valid，发布者 Pyrsys B.V.；使用其官方 `/PORTABLE=1 /CURRENTUSER /NOICONS` 模式解包到工作区 `.dotnet-home/installer-tools/inno`，没有将编译器安装到系统程序目录。

最终文件：

```text
dist/releases/NetGuardian-1.0.0-win-x64-Setup.exe
dist/releases/NetGuardian-1.0.0-win-x64-Setup.exe.sha256
```

安装包约 73.79 MiB，SHA-256 为 `f54a0c05352d72f8c5e23a397c91ecc965ee274566df325590e2ce6b74415454`。产物位于 Git 忽略目录，不提交二进制。安装包尚未做代码签名，也未上传 GitHub Release。

## 验证命令及结果

```powershell
$env:NETGUARDIAN_DOTNET = '<工作区>/.dotnet-home/sdk/dotnet.exe'
./scripts/build-installer.ps1 -Version 1.0.0 -CompilerPath '<工作区>/.dotnet-home/installer-tools/inno/ISCC.exe'
./tests/Test-InstallerScripts.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-InstallerScripts.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-GuardianScripts.ps1
git diff --check
```

- 首次完整打包：Release 核心 241/241、桌面 155/155，通过且无跳过；管理界面和 Guardian 的自包含发布通过，ISCC 编译成功，退出 0。保留现有 WFAC010 警告。
- 独立审查发现 Ready 监督任务可能在更新中途拉起后台，以及实际注销缺少 TaskPath；对应回归先失败、修复后通过。未删除或跳过测试。
- 最终支持脚本和 README 更新后，重新执行两组脚本回归，并重新拷入已验证的自包含暂存目录，再调用相同 ISCC 命令编译最终产物，退出 0；生产 .NET 源码没有变化，未重复无关的完整 UI 测试。
- 两组脚本在 PowerShell 7 和 Windows PowerShell 5.1 下均通过，退出 0；计划任务、进程和注销入口用替身验证，未停止真实后台。另对既有实际注销脚本的 TaskPath 参数做约束回归。
- 暂存中的最终 README、维护/注册/注销脚本与源码 SHA-256 一致。
- 程序 runtimeconfig 的 includedFrameworks 确认包含 .NET 8.0.32；发布目录包含 coreclr.dll 和管理界面的 PresentationFramework.dll。
- 在 DOTNET_ROOT 指向不存在目录、DOTNET_MULTILEVEL_LOOKUP=0 时，发布版 Guardian `--validate --settings <隔离测试配置>` 返回 0；该命令只校验静态测试配置，不扫描、连接或恢复网络。
- 安装包 SHA-256 与独立校验文件一致；差异空白检查退出 0。

完整日志保留在工作区忽略目录：`installer-build.log`、`installer-final-compile.log`、`installer-script-tests.log`、`installer-script-tests-ps51.log`、`installer-guardian-script-tests.log`。

## 失败及处理

- 最初沙箱内 RID 还原失败，退出 1：`NU1301: 无法加载源 https://api.nuget.org/v3/index.json 的服务索引。` 正常网络权限下相同还原成功；不是 SDK 或运行时缺失。
- 首个新增测试在维护脚本尚未创建时返回命令不存在，退出 1。实现后通过。
- Ready 任务回归初次抛出“预期拒绝操作：停止后台”，退出 1；原预检只拒绝 Running，修改为拒绝尚未移除的任务后通过。
- 安全注销范围回归初次抛出“安全卸载必须限定根任务文件夹，不能命中其他文件夹的同名任务。”，退出 1；所有实际注销调用增加 TaskPath 后通过。
- Program Files 路径门禁回归先因函数尚未存在而失败，退出 1；实现后合法目录通过、用户目录及相似前缀目录被拒绝。

## 验收边界

已验证真实编译、自包含运行、包内文件及安装维护脚本的行为；没有对本机正式程序做安装、升级或卸载，没有启动界面、切换网络、启用后台或重启电脑。Windows 设置卸载登记、快捷方式和完整安装/升级/卸载 UI 闭环仍需在独立测试机做验收，不能将编译通过称为该实机闭环已经完成。
