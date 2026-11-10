using NetGuardian.Core.SystemCommands;
using System.Net.NetworkInformation;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public sealed partial class NetshWifiClient : IWifiClient
{
    private readonly ISystemCommandRunner _commands;
    public string? LastConnectionFailure { get; private set; }

    public NetshWifiClient(ISystemCommandRunner? commands = null)
    {
        _commands = commands ?? CreateCommandRunner();
    }

    private static ProcessCommandRunner CreateCommandRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        // netsh 在不同 Windows 版本上可能输出 UTF-8 或系统 OEM 编码。
        var codePage = OperatingSystem.IsWindows() ? (int)GetOEMCP() : 65001;
        return new ProcessCommandRunner(outputEncoding: Encoding.UTF8,
            fallbackOutputEncoding: Encoding.GetEncoding(codePage));
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "show", "networks", "mode=bssid"], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Wi-Fi 扫描命令失败（退出码 {result.ExitCode}）。{Environment.NewLine}{result.Output}");
        return ParseScanOutput(result.Output);
    }

    public async Task<string?> GetCurrentSsidAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "show", "interfaces"], cancellationToken);
        return ParseCurrentSsid(result.Output);
    }

    public async Task<WifiConnectionInfo> GetConnectionInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "show", "interfaces"], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.ExitCode != 0) return new(false, false, Error: $"Windows 查询失败（退出码 {result.ExitCode}）：{result.Output}");
        var info = ParseConnectionInfo(result.Output);
        if (!info.Connected) return info;
        var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(item =>
            item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && item.Name == info.AdapterName);
        var ipv4 = adapter?.GetIPProperties().UnicastAddresses.FirstOrDefault(item =>
            item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();
        return info with { Ipv4 = ipv4 };
    }

    public static WifiConnectionInfo ParseConnectionInfo(string output)
    {
        var interfaces = new List<Dictionary<string, string>>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(output))
        {
            var parts = line.TrimStart().Split(':', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            if ((key.Equals("Name", StringComparison.OrdinalIgnoreCase) || key == "名称") && fields.Count > 0)
            { interfaces.Add(fields); fields = new(StringComparer.OrdinalIgnoreCase); }
            fields[key] = ReadNameValue(parts[1]);
        }
        interfaces.Add(fields);
        var connections = interfaces.Select(values =>
        {
            string? Read(string english, string chinese) => values.GetValueOrDefault(english) ?? values.GetValueOrDefault(chinese);
            var state = Read("State", "状态")?.Trim();
            var connected = (state?.Equals("connected", StringComparison.OrdinalIgnoreCase) == true || state == "已连接")
                && !string.IsNullOrWhiteSpace(Read("SSID", "SSID 名称"));
            var signal = Regex.Match(Read("Signal", "信号") ?? "", @"\d+");
            return new WifiConnectionInfo(true, connected, connected ? Read("SSID", "SSID 名称") : null,
                Read("Name", "名称")?.Trim(), connected && signal.Success && int.TryParse(signal.Value, out var percent)
                    ? Math.Clamp(percent, 0, 100) : null,
                connected ? Read("Authentication", "身份验证")?.Trim() : null,
                connected ? Read("Cipher", "密码")?.Trim() : null);
        }).ToList();
        return connections.FirstOrDefault(item => item.Connected) ?? connections.FirstOrDefault(item => item.AdapterName is not null)
            ?? new(true, false);
    }

    public async Task<WifiAdapterDiagnostics> GetAdapterDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var adapter = GetPrimaryWifiInterface();
        var result = await RunNetshAsync(["wlan", "show", "interfaces"], cancellationToken);
        var parsed = ParseInterfaceDiagnostics(result.Output);
        var adapterName = parsed.AdapterName ?? adapter?.Name;
        var adapterPresent = adapter is not null || !string.IsNullOrWhiteSpace(parsed.AdapterName);
        var adapterUp = adapter?.OperationalStatus == OperationalStatus.Up
            || parsed.State?.Equals("connected", StringComparison.OrdinalIgnoreCase) == true
            || parsed.State?.Equals("已连接", StringComparison.OrdinalIgnoreCase) == true;

        return new WifiAdapterDiagnostics(
            adapterName,
            adapterPresent,
            adapterUp,
            parsed.HardwareRadioOn,
            parsed.SoftwareRadioOn,
            parsed.State,
            result.ExitCode == 0 ? "" : "Wi-Fi diagnostics require administrator permission.");
    }

    public async Task<IReadOnlyList<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default)
    {
        return (await GetProfileInventoryAsync(cancellationToken)).Names;
    }

    public async Task<WifiProfileInventory> GetProfileInventoryAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "show", "profiles"], cancellationToken);
        return result.ExitCode == 0
            ? new WifiProfileInventory(true, ParseProfileNames(result.Output))
            : new WifiProfileInventory(false, [], result.Output);
    }

    public async Task<SavedWifiProfileInventory> GetSavedProfilesAsync(CancellationToken cancellationToken = default)
    {
        var inventory = await GetProfileInventoryAsync(cancellationToken);
        if (!inventory.Success) return new(false, [], inventory.Error);
        var profiles = new List<SavedWifiProfile>();
        foreach (var name in inventory.Names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (name.StartsWith("NetGuardian-Verify-", StringComparison.OrdinalIgnoreCase)) continue;
            // 只查询元数据，不使用 key=clear，不读取或落盘无线密码。
            var result = await RunNetshAsync(["wlan", "show", "profile", $"name={name}"], cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var ssid = result.ExitCode == 0 ? ParseProfileSsid(result.Output) : null;
            if (ssid is null)
                return new(false, [], $"无法读取无线配置“{name}”（退出码 {result.ExitCode}）。{result.Output}");
            var authentication = SplitLines(result.Output)
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2 && (parts[0].Trim().Equals("Authentication", StringComparison.OrdinalIgnoreCase)
                    || parts[0].Trim() == "身份验证"))
                .Select(parts => parts[1].Trim()).FirstOrDefault() ?? "";
            profiles.Add(new(name, ssid, authentication));
        }
        return new(true, profiles);
    }

    public async Task<bool> TryEnableWifiAdapterAsync(string? adapterName, CancellationToken cancellationToken = default)
    {
        if (adapterName?.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) == true
            || adapterName?.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) == true)
        {
            var enabled = await _commands.RunAsync("pnputil.exe", ["/enable-device", adapterName], cancellationToken);
            return enabled.Ok;
        }
        var name = string.IsNullOrWhiteSpace(adapterName) ? GetPrimaryWifiInterface()?.Name : adapterName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var result = await RunNetshAsync(["interface", "set", "interface", $"name={name}", "admin=enabled"], cancellationToken);
        if (result.ExitCode == 0)
        {
            await RunNetshAsync(["wlan", "set", "autoconfig", "enabled=yes", $"interface={name}"], cancellationToken);
        }

        return result.ExitCode == 0;
    }

    public async Task<string?> GetSavedPasswordAsync(string ssid, CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "show", "profile", $"name={ssid}", "key=clear"], cancellationToken);
        var password = result.ExitCode == 0 && ssid.Equals(ParseProfileSsid(result.Output), StringComparison.Ordinal)
            ? ParseSavedPassword(result.Output) : null;
        if (!string.IsNullOrWhiteSpace(password))
        {
            return password;
        }

        var profilesResult = await RunNetshAsync(["wlan", "show", "profiles"], cancellationToken);
        foreach (var profileName in ParseProfileNames(profilesResult.Output))
        {
            var profileResult = await RunNetshAsync(["wlan", "show", "profile", $"name={profileName}"], cancellationToken);
            var profileSsid = ParseProfileSsid(profileResult.Output);
            if (profileResult.ExitCode != 0 || !ssid.Equals(profileSsid, StringComparison.Ordinal))
            {
                continue;
            }

            var keyResult = await RunNetshAsync(["wlan", "show", "profile", $"name={profileName}", "key=clear"], cancellationToken);
            password = keyResult.ExitCode == 0 && ssid.Equals(ParseProfileSsid(keyResult.Output), StringComparison.Ordinal)
                ? ParseSavedPassword(keyResult.Output) : null;
            if (!string.IsNullOrWhiteSpace(password))
            {
                return password;
            }
        }

        return null;
    }

    public Task<bool> SaveProfileAsync(string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Netsh profile saving is deprecated due to plaintext password risk. Use NativeWifiScanner instead.");
    }

    public Task<bool> SaveProfileAsync(string profileName, string ssid, string? password, string authentication, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Netsh profile saving is deprecated due to plaintext password risk. Use NativeWifiScanner instead.");
    }

    public async Task<bool> ConnectAsync(string ssid, CancellationToken cancellationToken = default)
    {
        return await ConnectProfileAsync(ssid, ssid, cancellationToken);
    }

    public async Task<bool> ConnectProfileAsync(string profileName, string ssid, CancellationToken cancellationToken = default)
    {
        LastConnectionFailure = null;
        // 配置已保存真实 SSID；netsh 的 ssid 文本参数在系统代码页下可能误转换中文。
        var result = await RunNetshAsync(["wlan", "connect", $"name={profileName}"], cancellationToken);
        if (result.ExitCode != 0) LastConnectionFailure = $"netsh 退出码 {result.ExitCode}：{result.Output}";
        return result.ExitCode == 0;
    }

    public async Task<bool> DeleteProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "delete", "profile", $"name={profileName}"], cancellationToken);
        return result.ExitCode == 0;
    }

    public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunNetshAsync(["wlan", "disconnect"], cancellationToken);
        return result.ExitCode == 0;
    }

    public static string? ParseCurrentSsid(string output)
    {
        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.TrimStart();
            if (line.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Regex.IsMatch(line, @"(^|\s)(SSID|SSID 名称)\s*:", RegexOptions.IgnoreCase))
            {
                var value = ReadNameValue(line.Split(':', 2).Last());
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    public static WifiAdapterDiagnostics ParseInterfaceDiagnostics(string output)
    {
        string? adapterName = null;
        string? state = null;
        bool? hardwareRadioOn = null;
        bool? softwareRadioOn = null;

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.Contains(':'))
            {
                var parts = line.Split(':', 2);
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("名称", StringComparison.OrdinalIgnoreCase))
                {
                    adapterName = string.IsNullOrWhiteSpace(value) ? null : value;
                }
                else if (key.Equals("State", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("状态", StringComparison.OrdinalIgnoreCase))
                {
                    state = string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            hardwareRadioOn ??= ParseRadioFlag(line, "Hardware", "硬件");
            softwareRadioOn ??= ParseRadioFlag(line, "Software", "软件");
        }

        return new WifiAdapterDiagnostics(
            adapterName,
            !string.IsNullOrWhiteSpace(adapterName),
            state?.Equals("connected", StringComparison.OrdinalIgnoreCase) == true
                || state?.Equals("已连接", StringComparison.OrdinalIgnoreCase) == true,
            hardwareRadioOn,
            softwareRadioOn,
            state,
            "");
    }

    public static IReadOnlyList<WifiNetwork> ParseScanOutput(string output)
    {
        var networks = new List<WifiNetwork>();
        var current = new MutableNetwork();

        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.TrimStart();
            var ssidMatch = SsidRegex().Match(line);
            if (ssidMatch.Success)
            {
                AddCurrent();
                var ssid = ssidMatch.Groups.Values
                    .Skip(1)
                    .Select(group => group.Value)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
                current = new MutableNetwork { Ssid = ssid };
                continue;
            }

            if (string.IsNullOrWhiteSpace(current.Ssid) || !line.Contains(':'))
            {
                continue;
            }

            var parts = line.Split(':', 2);
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (key.StartsWith("Signal", StringComparison.OrdinalIgnoreCase) || key.StartsWith("信号", StringComparison.OrdinalIgnoreCase))
            {
                var signalMatch = Regex.Match(value, @"\d+");
                if (signalMatch.Success)
                {
                    current.Signal = int.Parse(signalMatch.Value);
                }
            }
            else if (key.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase) || key.StartsWith("身份验证", StringComparison.OrdinalIgnoreCase))
            {
                current.Authentication = value;
            }
            else if (key.StartsWith("Encryption", StringComparison.OrdinalIgnoreCase) || key.StartsWith("加密", StringComparison.OrdinalIgnoreCase))
            {
                current.Encryption = value;
            }
        }

        AddCurrent();
        return networks
            .GroupBy(network => network.Ssid)
            .Select(group => group.OrderByDescending(network => network.SignalPercent).First())
            .OrderByDescending(network => network.SignalPercent)
            .ThenBy(network => network.Ssid)
            .ToList();

        void AddCurrent()
        {
            if (!string.IsNullOrWhiteSpace(current.Ssid))
            {
                networks.Add(new WifiNetwork(
                    current.Ssid,
                    current.Signal,
                    current.Authentication,
                    current.Encryption,
                    !current.Authentication.Contains("Open", StringComparison.OrdinalIgnoreCase)
                        && !current.Authentication.Contains("开放", StringComparison.OrdinalIgnoreCase),
                    true));
            }
        }
    }

    public static string? ParseSavedPassword(string output)
    {
        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.Trim();
            if (!line.Contains(':'))
            {
                continue;
            }

            var parts = line.Split(':', 2);
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (key.Contains("Key Content", StringComparison.OrdinalIgnoreCase)
                || key.Contains("关键内容", StringComparison.OrdinalIgnoreCase)
                || key.Contains("密钥内容", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> ParseProfileNames(string output)
    {
        return SplitLines(output)
            .Select(line => line.TrimStart())
            .Where(line => line.Contains(':'))
            .Select(line => line.Split(':', 2))
            .Where(parts => parts[0].Contains("All User Profile", StringComparison.OrdinalIgnoreCase)
                || parts[0].Contains("User Profile", StringComparison.OrdinalIgnoreCase)
                || parts[0].Contains("所有用户配置文件", StringComparison.OrdinalIgnoreCase)
                || parts[0].Contains("用户配置文件", StringComparison.OrdinalIgnoreCase))
            .Select(parts => ReadNameValue(parts[1]))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static string? ParseProfileSsid(string output)
    {
        foreach (var rawLine in SplitLines(output))
        {
            var line = rawLine.TrimStart();
            if (!line.Contains(':'))
            {
                continue;
            }

            var parts = line.Split(':', 2);
            var key = parts[0].Trim();
            var value = ReadNameValue(parts[1]);
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '“' && value[^1] == '”'))) value = value[1..^1];
            if (key.Equals("SSID name", StringComparison.OrdinalIgnoreCase)
                || key.Equals("SSID 名称", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    // netsh 在冒号后固定添加一个格式空格，其余空格属于网络或配置名称。
    private static string ReadNameValue(string value) => value.StartsWith(' ') ? value[1..] : value;

    private static bool? ParseRadioFlag(string line, string englishKey, string chineseKey)
    {
        if (!line.Contains(englishKey, StringComparison.OrdinalIgnoreCase)
            && !line.Contains(chineseKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (line.Contains("On", StringComparison.OrdinalIgnoreCase)
            || line.Contains("打开", StringComparison.OrdinalIgnoreCase)
            || line.Contains("开启", StringComparison.OrdinalIgnoreCase)
            || line.Contains("启用", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (line.Contains("Off", StringComparison.OrdinalIgnoreCase)
            || line.Contains("关闭", StringComparison.OrdinalIgnoreCase)
            || line.Contains("禁用", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static NetworkInterface? GetPrimaryWifiInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up)
            .ThenBy(item => item.Name)
            .FirstOrDefault();
    }

    public static string BuildWlanProfileXml(string ssid, string? password, string authentication)
    {
        return BuildWlanProfileXml(ssid, ssid, password, authentication);
    }

    public static string BuildWlanProfileXml(string profileName, string ssid, string? password, string authentication)
    {
        var escapedProfileName = SecurityElement.Escape(profileName) ?? profileName;
        var escapedSsid = SecurityElement.Escape(ssid) ?? ssid;
        var isOpen = string.IsNullOrWhiteSpace(password)
            || authentication.Contains("Open", StringComparison.OrdinalIgnoreCase)
            || authentication.Contains("开放", StringComparison.OrdinalIgnoreCase);

        var security = isOpen
            ? """
                    <authEncryption>
                        <authentication>open</authentication>
                        <encryption>none</encryption>
                        <useOneX>false</useOneX>
                    </authEncryption>
              """
            : $"""
                    <authEncryption>
                        <authentication>WPA2PSK</authentication>
                        <encryption>AES</encryption>
                        <useOneX>false</useOneX>
                    </authEncryption>
                    <sharedKey>
                        <keyType>passPhrase</keyType>
                        <protected>false</protected>
                        <keyMaterial>{SecurityElement.Escape(password) ?? password}</keyMaterial>
                    </sharedKey>
              """;

        return $$"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
                <name>{{escapedProfileName}}</name>
                <SSIDConfig>
                    <SSID>
                        <name>{{escapedSsid}}</name>
                    </SSID>
                </SSIDConfig>
                <connectionType>ESS</connectionType>
                <connectionMode>manual</connectionMode>
                <MSM>
                    <security>
            {{security}}
                    </security>
                </MSM>
            </WLANProfile>
            """;
    }

    private async Task<(int ExitCode, string Output)> RunNetshAsync(string[] arguments, CancellationToken cancellationToken)
    {
        // 读取器兼容 UTF-8 与系统 OEM 编码，不修改用户的控制台代码页。
        var result = await _commands.RunAsync("netsh.exe", arguments, cancellationToken);
        return (result.ExitCode, result.Output);
    }

    [GeneratedRegex(@"^SSID\s+\d+\s*: ?(.*)$|^SSID\s*\d*\s*名称\s*: ?(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SsidRegex();

    private sealed class MutableNetwork
    {
        public string Ssid { get; init; } = "";
        public int Signal { get; set; }
        public string Authentication { get; set; } = "";
        public string Encryption { get; set; } = "";
    }
}

public sealed record WifiAdapterDiagnostics(
    string? AdapterName,
    bool AdapterPresent,
    bool AdapterUp,
    bool? HardwareRadioOn,
    bool? SoftwareRadioOn,
    string? State,
    string Detail,
    string? DeviceInstanceId = null,
    int? ProblemCode = null);
