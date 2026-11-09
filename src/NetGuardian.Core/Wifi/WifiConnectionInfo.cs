namespace NetGuardian.Core.Wifi;

public sealed record WifiConnectionInfo(bool ReadSucceeded, bool Connected, string? Ssid = null,
    string? AdapterName = null, int? SignalPercent = null, string? Authentication = null,
    string? Encryption = null, string? Ipv4 = null, string? Error = null);
