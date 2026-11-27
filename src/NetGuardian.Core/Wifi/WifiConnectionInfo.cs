namespace NetGuardian.Core.Wifi;

public sealed record WifiConnectionInfo(bool ReadSucceeded, bool Connected, string? Ssid = null,
    string? AdapterName = null, int? SignalPercent = null, string? Authentication = null,
    string? Encryption = null, string? Ipv4 = null, string? Error = null,
    string? Bssid = null, string? RadioType = null, string? Band = null,
    int? Channel = null, double? ReceiveRateMbps = null, double? TransmitRateMbps = null);
