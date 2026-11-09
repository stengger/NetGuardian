using System.Runtime.InteropServices;
using System.Text;

namespace NetGuardian.Core.Wifi;

public sealed partial class NativeWifiScanner
{
    public bool ConnectionFailureIsFinal { get; private set; }
    public string? LastConnectionFailure { get; private set; }

    public bool TryConnectProfile(string profileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastNativeError = null;
        LastConnectionFailure = null;
        ConnectionFailureIsFinal = false;
        var result = WlanOpenHandle(WlanClientVersion, IntPtr.Zero, out _, out var handle);
        if (result != ErrorSuccess) { LastNativeError = result; return false; }
        try
        {
            foreach (var wlanInterface in EnumerateInterfaces(handle, error => LastNativeError = error))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var interfaceGuid = wlanInterface.InterfaceGuid;
                var attempt = new WifiConnectionAttempt(interfaceGuid, profileName);
                WlanNotificationCallback callback = (ref WlanNotificationData notification, IntPtr _) =>
                {
                    if ((notification.NotificationSource & WlanNotificationSourceAcm) == 0
                        || notification.NotificationCode is not (10 or 11)
                        || notification.InterfaceGuid != interfaceGuid || notification.DataPtr == IntPtr.Zero
                        || notification.DataSize < Marshal.SizeOf<WlanConnectionNotificationHeader>()) return;
                    var data = Marshal.PtrToStructure<WlanConnectionNotificationHeader>(notification.DataPtr);
                    attempt.Observe(notification.NotificationSource, notification.NotificationCode,
                        notification.InterfaceGuid, data.ProfileName, data.ReasonCode);
                };
                var registrationResult = WlanRegisterNotification(handle, WlanNotificationSourceAcm, false,
                    callback, IntPtr.Zero, IntPtr.Zero, out _);
                var registered = registrationResult == ErrorSuccess;
                if (!registered)
                {
                    ConnectionFailureIsFinal = true;
                    LastConnectionFailure = $"Windows 无法监听无线连接结果（错误码 {registrationResult}），未发出连接请求。";
                    return false;
                }
                async Task StopAndWaitAsync()
                {
                    // 等待超时与结果通知可能同时发生；先核实已结束的请求，避免断开已恢复的原网络。
                    if (attempt.Completion.IsCompleted && QueryInterfaceState(handle, interfaceGuid) is 0 or 1 or 2 or 4) return;
                    var disconnectGuid = interfaceGuid;
                    var disconnectResult = WlanDisconnect(handle, ref disconnectGuid, IntPtr.Zero);
                    while (true)
                    {
                        var state = QueryInterfaceState(handle, interfaceGuid);
                        if (state is 0 or 4
                            || (disconnectResult != ErrorSuccess && attempt.Completion.IsCompleted && state is 1 or 2)) return;
                        // 未知或仍在连接的状态不能证明异步请求结束，继续持锁等待系统确认。
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                }
                try
                {
                    var parameters = new WlanConnectionParameters
                    {
                        ConnectionMode = 0,
                        ProfileName = profileName,
                        BssType = 1
                    };
                    cancellationToken.ThrowIfCancellationRequested();
                    result = WlanConnect(handle, ref interfaceGuid, ref parameters, IntPtr.Zero);
                    if (result != ErrorSuccess) { LastNativeError = result; continue; }
                    LastNativeError = null;
                    ConnectionFailureIsFinal = true;
                    var outcome = attempt.WaitAsync(TimeSpan.FromSeconds(20), StopAndWaitAsync, cancellationToken).GetAwaiter().GetResult();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (outcome?.Connected == true) return true;
                    if (outcome is null)
                    {
                        LastConnectionFailure = "Windows 无线连接超时，已停止本次连接请求。";
                        return false;
                    }
                    if (QueryInterfaceState(handle, interfaceGuid) is not (0 or 1 or 2 or 4))
                        StopAndWaitAsync().GetAwaiter().GetResult();
                    LastConnectionFailure = DescribeConnectionFailure(outcome.ReasonCode);
                    return false;
                }
                finally
                {
                    if (registered) WlanRegisterNotification(handle, 0, false, null, IntPtr.Zero, IntPtr.Zero, out _);
                    GC.KeepAlive(callback);
                }
            }
            return false;
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }

    public static string DescribeConnectionFailure(uint reasonCode)
    {
        if (reasonCode == 0) return "Windows 未能完成本次无线连接。";
        var text = new StringBuilder(1024);
        return WlanReasonCodeToString(reasonCode, (uint)text.Capacity, text, IntPtr.Zero) == ErrorSuccess
            && !string.IsNullOrWhiteSpace(text.ToString())
            ? text.ToString().Trim() : $"Windows 无线连接失败，原因码：{reasonCode}。";
    }

    private static uint? QueryInterfaceState(IntPtr handle, Guid interfaceGuid)
    {
        var result = WlanQueryInterface(handle, ref interfaceGuid, 6, IntPtr.Zero, out var size, out var data, out _);
        try
        {
            if (result == 1168) return 0;
            return result == ErrorSuccess && data != IntPtr.Zero && size >= 4 ? (uint)Marshal.ReadInt32(data) : null;
        }
        finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanDisconnect(IntPtr handle, ref Guid interfaceGuid, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr handle, ref Guid interfaceGuid, uint opcode, IntPtr reserved,
        out uint dataSize, out IntPtr data, out uint opcodeType);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanReasonCodeToString(uint reasonCode, uint bufferSize, StringBuilder buffer, IntPtr reserved);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionNotificationHeader
    {
        public uint ConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public Dot11Ssid Ssid;
        public uint BssType;
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        public uint ReasonCode;
        public uint Flags;
    }
}
