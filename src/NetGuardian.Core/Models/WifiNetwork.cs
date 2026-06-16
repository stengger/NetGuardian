namespace NetGuardian.Core.Models;

public sealed record WifiNetwork(
    string Ssid,
    int SignalPercent,
    string Authentication,
    string Encryption,
    bool IsSecure,
    bool IsConnectable);

