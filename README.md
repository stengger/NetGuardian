# NetGuardian

NetGuardian 是一个 Windows Wi-Fi 连通性监控与自动恢复桌面软件 MVP。它面向远程 Windows 电脑只使用 Wi-Fi 上网、偶发断网后无人能现场处理的场景。

当前实现采用 Python 标准库：

- Tkinter 桌面 UI
- 后台监控线程
- `netsh wlan` 扫描、连接、写入 WLAN Profile
- 分层连通性检测：IPv4、默认网关、DNS、公网 HTTPS
- Wi-Fi 优先级列表
- 保守恢复策略：复测、当前 Wi-Fi 软重连、按优先级切换 Wi-Fi

## 运行

```powershell
python run.py
```

## 设计原则

远程机器不能一断网就重启网卡。NetGuardian 的恢复顺序是：

```text
连续失败确认
-> DNS 轻量修复，默认关闭
-> 当前 Wi-Fi 软重连
-> 按优先级尝试其他 Wi-Fi
-> 记录失败并冷却
```

重启 WLAN 服务、重启网卡、重置网络栈属于高风险动作，MVP 中只保留策略开关和扩展位置，不自动执行。

## 密码保存

应用配置文件只保存 SSID、优先级、启用状态和历史统计，不保存 Wi-Fi 密码。

当用户输入密码时，应用会通过 `netsh wlan add profile` 写入 Windows WLAN Profile，由 Windows 保存凭据。临时 XML 文件会在写入后删除。

## 配置文件

默认配置路径：

```text
%APPDATA%\NetGuardian\settings.json
```

## 后续产品化方向

- 拆分 Windows Service 与桌面 UI
- 增加管理员权限安装器
- 支持 Windows Credential Manager / DPAPI
- 增加远程可达性探针，例如 RDP、RustDesk、Tailscale、ZeroTier
- 增加测速、丢包、抖动和历史趋势
- 增加 DNS 设置和网络诊断报告

