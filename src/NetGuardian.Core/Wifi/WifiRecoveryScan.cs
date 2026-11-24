using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public enum WifiScanStatus { Completed, Failed, TimedOut }

public sealed record WifiRecoveryScan(WifiScanStatus Status, IReadOnlyList<WifiNetwork> Networks);
