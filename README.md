# NetGuardian

NetGuardian 是一个面向远程 Windows 电脑的 Wi-Fi 连通性监控与恢复工具。当前版本是 C# / WPF 桌面应用，默认中文界面，支持切换 English。

## 当前能力

- 现代 WPF 桌面界面，左侧导航、状态卡片、最近事件和网络探针表格
- 默认中文，设置页可切换 English
- Native Wi-Fi API 扫描，失败时回退到 `netsh`
- 可为 Wi-Fi 预设密码，并写入 Windows WLAN Profile，应用配置不保存明文密码
- Wi-Fi 恢复优先级列表支持拖拽排序、上移/下移和删除
- 连通性检测：IPv4、默认网关、DNS、公网 HTTPS
- 检测策略配置：检测间隔、失败阈值、冷却时间、DNS 刷新开关
- 时间设置支持秒、分钟、小时
- 系统托盘常驻，可选择关闭窗口时退出或后台运行
- 项目专属图标，接入窗口、exe 和托盘
- 正式发布输出 `dist\NetGuardian\NetGuardian.exe`

## 环境

.NET SDK 安装在：

```text
E:\Environment\dotnet-sdk-8.0
```

这是通用 .NET SDK 环境，类似 JDK 或 Python 解释器。项目不会修改系统 PATH，脚本会显式调用这个 SDK。

## 启动

推荐先发布正式 exe：

```powershell
.\scripts\publish.ps1
```

然后双击：

```text
dist\NetGuardian\NetGuardian.exe
```

开发期也可以双击：

```text
Start-NetGuardian.vbs
```

它会优先启动已发布 exe；如果还没发布，会自动构建 Debug 版本并启动。

也可以手动构建：

```powershell
.\scripts\build.ps1
```

Debug 构建后的 exe 位于：

```text
src\NetGuardian.App\bin\Debug\net8.0-windows\NetGuardian.exe
```

## 项目结构

```text
src\NetGuardian.App      WPF 桌面 UI
src\NetGuardian.Core     核心模型、检测、Wi-Fi API、配置、语言资源
tests\NetGuardian.Tests  无外部依赖的轻量测试控制台
```

## 设计原则

远程机器不能一断网就重启网卡。NetGuardian 的恢复策略继续遵循：

```text
先诊断
-> 轻恢复
-> 当前 Wi-Fi 软重连
-> 按优先级切换 Wi-Fi
-> 最后才考虑 WLAN 服务/网卡重启
```

重启 WLAN 服务、重启网卡、重置网络栈属于高风险动作，后续服务版本中再以显式开关和冷却策略启用。

