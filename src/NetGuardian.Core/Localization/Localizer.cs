namespace NetGuardian.Core.Localization;

public sealed class Localizer
{
    private readonly Dictionary<string, Dictionary<string, string>> _resources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = new()
        {
            ["AppTitle"] = "NetGuardian",
            ["Status"] = "状态",
            ["Wifi"] = "Wi-Fi",
            ["Policy"] = "策略",
            ["Tools"] = "工具",
            ["Log"] = "日志",
            ["Settings"] = "设置",
            ["CheckNow"] = "立即检测",
            ["StartMonitor"] = "开始监控",
            ["Stop"] = "停止",
            ["Scan"] = "扫描",
            ["Scanning"] = "正在扫描 Wi-Fi...",
            ["ScanDone"] = "扫描完成，发现 {0} 个网络",
            ["NearbyNetworks"] = "周围网络",
            ["PriorityList"] = "恢复优先级",
            ["Priority"] = "优先级",
            ["Language"] = "语言",
            ["Chinese"] = "中文",
            ["English"] = "English",
            ["CurrentNetwork"] = "当前网络",
            ["HealthState"] = "网络状态",
            ["LastCheck"] = "最近检测",
            ["Summary"] = "摘要",
            ["Signal"] = "信号",
            ["Security"] = "安全",
            ["Authentication"] = "认证",
            ["Encryption"] = "加密",
            ["Ssid"] = "SSID",
            ["AddSelected"] = "添加选中",
            ["AddManual"] = "手动添加",
            ["MoveUp"] = "上移",
            ["MoveDown"] = "下移",
            ["Connect"] = "连接",
            ["Enabled"] = "启用",
            ["Failures"] = "失败次数",
            ["Save"] = "保存",
            ["Interval"] = "检测间隔（秒）",
            ["FailuresBeforeRecovery"] = "恢复前连续失败次数",
            ["Cooldown"] = "恢复冷却（秒）",
            ["AutoSwitch"] = "允许自动切换 Wi-Fi",
            ["SoftReconnect"] = "允许当前 Wi-Fi 软重连",
            ["DnsFlush"] = "恢复前刷新 DNS",
            ["HighRiskReserved"] = "重启 WLAN 服务和网卡属于高风险动作，后续服务版本再启用。",
            ["NotChecked"] = "尚未检测",
            ["Healthy"] = "正常",
            ["Disconnected"] = "未连接",
            ["GatewayIssue"] = "网关异常",
            ["DnsIssue"] = "DNS 异常",
            ["InternetIssue"] = "公网异常",
            ["Recovering"] = "恢复中",
            ["Failed"] = "失败"
        },
        ["en-US"] = new()
        {
            ["AppTitle"] = "NetGuardian",
            ["Status"] = "Status",
            ["Wifi"] = "Wi-Fi",
            ["Policy"] = "Policy",
            ["Tools"] = "Tools",
            ["Log"] = "Log",
            ["Settings"] = "Settings",
            ["CheckNow"] = "Check now",
            ["StartMonitor"] = "Start monitor",
            ["Stop"] = "Stop",
            ["Scan"] = "Scan",
            ["Scanning"] = "Scanning Wi-Fi...",
            ["ScanDone"] = "Scan complete, {0} networks found",
            ["NearbyNetworks"] = "Nearby networks",
            ["PriorityList"] = "Recovery priority",
            ["Priority"] = "Priority",
            ["Language"] = "Language",
            ["Chinese"] = "中文",
            ["English"] = "English",
            ["CurrentNetwork"] = "Current network",
            ["HealthState"] = "Health state",
            ["LastCheck"] = "Last check",
            ["Summary"] = "Summary",
            ["Signal"] = "Signal",
            ["Security"] = "Security",
            ["Authentication"] = "Authentication",
            ["Encryption"] = "Encryption",
            ["Ssid"] = "SSID",
            ["AddSelected"] = "Add selected",
            ["AddManual"] = "Add manually",
            ["MoveUp"] = "Move up",
            ["MoveDown"] = "Move down",
            ["Connect"] = "Connect",
            ["Enabled"] = "Enabled",
            ["Failures"] = "Failures",
            ["Save"] = "Save",
            ["Interval"] = "Check interval seconds",
            ["FailuresBeforeRecovery"] = "Failures before recovery",
            ["Cooldown"] = "Recovery cooldown seconds",
            ["AutoSwitch"] = "Allow automatic Wi-Fi switching",
            ["SoftReconnect"] = "Allow current Wi-Fi soft reconnect",
            ["DnsFlush"] = "Flush DNS before recovery",
            ["HighRiskReserved"] = "WLAN service and adapter restart are high-risk actions reserved for the service build.",
            ["NotChecked"] = "Not checked",
            ["Healthy"] = "Healthy",
            ["Disconnected"] = "Disconnected",
            ["GatewayIssue"] = "Gateway issue",
            ["DnsIssue"] = "DNS issue",
            ["InternetIssue"] = "Internet issue",
            ["Recovering"] = "Recovering",
            ["Failed"] = "Failed"
        }
    };

    public string Language { get; private set; } = "zh-CN";

    public void SetLanguage(string language)
    {
        Language = _resources.ContainsKey(language) ? language : "zh-CN";
    }

    public string T(string key)
    {
        if (_resources.TryGetValue(Language, out var lang) && lang.TryGetValue(key, out var value))
        {
            return value;
        }

        return _resources["zh-CN"].TryGetValue(key, out var fallback) ? fallback : key;
    }
}
