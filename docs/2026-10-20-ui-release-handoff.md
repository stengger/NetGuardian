# 产品界面更新包与实际验收交接

## 当前发布包

- 界面源码提交：`2772296710cf170e20093f0fadd2196ec7235006`。
- 发布目录：`C:\Users\Qiu\Documents\Net\dist\NetGuardian-UI-Update-2772296`。
- `UIInstalledBuild.json` 记录提交、生成时间和 11 个界面文件 SHA-256；另有同提交的 guardian 子目录，但此次只准备替换界面根目录文件。
- Release `dotnet test NetGuardian.sln --configuration Release --no-restore --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false`：Core 206/206、WPF 76/76，无跳过，退出 0。
- App 与 Guardian 分别 `dotnet publish ... --configuration Release --self-contained false --no-restore --output ...` 加单节点/禁复用参数均退出 0。日志位于忽略目录 `release-tests.log`、`publish-app.log`、`publish-guardian.log`。
- 实际运行时检查确认系统安装 Microsoft.WindowsDesktop.App 8.0.31，符合发布包 net8.0-windows 要求。

## 替换现状与步骤

更新脚本为 `C:\Users\Qiu\Documents\Net\.dotnet-home\validation\product-ui\install-ui-update.ps1`。脚本及构建产物均在忽略目录，不提交到 Git。语法检查和 `-WhatIf` 退出 0；独立审查发现的回滚中断问题已修复并复核。脚本校验发布包，取得全局界面互斥，探测写权限与锁，备份已有文件，再复制并逐文件校验；回滚逐项执行并汇总恢复错误。

自动替换尝试退出 1，发生在任何备份或写入之前：

```text
Exception: install-ui-update.ps1:23
界面仍在运行或另一更新正在执行；没有替换文件。请从托盘退出后重试。
```

当前终端未列出对应进程，CIM 查询返回“无法从客户端中访问 CIM 资源”；不能据此绕过全局单实例检查。备份目录尚未建立，尚未替换安装文件。

1. 从 NetGuardian 系统托盘菜单选择“退出”；关闭窗口可能只隐藏到托盘。
2. 在管理员 PowerShell 中执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Qiu\Documents\Net\.dotnet-home\validation\product-ui\install-ui-update.ps1"
```

3. 必须看到“界面更新成功，已验证 11 个文件”；安装目录中的 `UIInstalledBuild.json` 应记录上述提交。
4. 再启动 `C:\Program Files\NetGuardian\NetGuardian.exe`。

若仍报告互斥被占用，请先退出其他 Windows 登录会话中的界面或另一更新进程。不要强制绕过校验。若失败消息包含恢复失败文件，先按其备份路径修复再启动。

脚本不修改机器配置、后台目录、任务或启动设置。机器配置读取实际返回 Access denied，因此不能声称对配置做过前后哈希验证。后台 EXE/Core 文件已记录更新前哈希用于后续比对。

## 尚须实际验收

当前自动化截图来自真实主窗口 XAML 的测试窗口与模拟状态，不是安装业务进程。原生计算机控制不可用，以下项目不能以夹具测试代替：

- 安装进程的测速摘要、说明展开/滚动、重新测速/取消/失败；最小窗口内三项结果持续可见，HTTP 响应耗时与 Ping 区分明确。
- 页头/托盘在前台、后台接管、手动检查完成和退出阶段一致；旧心跳不能覆盖新报告。
- 真实 Wi-Fi 扫描、取消、位置权限错误、WLAN 服务不可用；DNS/DHCP 确认目标、部分失败与刷新失败。
- 100%/125%/150%/200% DPI、跨屏移动、明暗主题实时切换、高对比模式、完整键盘与讲述人。
- 实际流量采样的性能和读取异常；虚拟接口重复计数应按范围说明理解，不能视为互联网测速。

源码阶段问题已逐单元修复和验证，但完整商业桌面软件目标尚未完成。安装与实际验收结果应继续追加，不能仅凭测试通过关闭整个目标。
