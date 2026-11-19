using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace NetGuardian.Core.Wifi;

public sealed partial class NativeWifiScanner : IWifiRadioController
{
    private const uint WlanOpcodeRadioState = 4;
    private const uint RadioOn = 1;
    public string? LastRadioFailure { get; private set; }

    public bool TryEnableSoftwareRadio(string adapterName, CancellationToken cancellationToken = default)
    {
        LastRadioFailure = null;
        cancellationToken.ThrowIfCancellationRequested();
        var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(item =>
            item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && item.Name == adapterName);
        if (adapter is null || !Guid.TryParse(adapter.Id, out var interfaceGuid))
        {
            LastRadioFailure = $"找不到无线接口“{adapterName}”的 GUID，未修改其他接口。";
            return false;
        }

        var openResult = WlanOpenHandle(WlanClientVersion, IntPtr.Zero, out _, out var handle);
        if (openResult != ErrorSuccess)
        {
            LastRadioFailure = $"WlanOpenHandle 错误码 {openResult}";
            return false;
        }
        try
        {
            var interfaces = EnumerateInterfaces(handle, error => LastRadioFailure = $"WlanEnumInterfaces 错误码 {error}");
            if (!interfaces.Any(item => item.InterfaceGuid == interfaceGuid))
            {
                LastRadioFailure ??= $"WLAN 服务未枚举到无线接口“{adapterName}”，未修改其他接口。";
                return false;
            }

            var queryResult = WlanQueryInterface(handle, ref interfaceGuid, WlanOpcodeRadioState, IntPtr.Zero,
                out var dataSize, out var data, out _);
            try
            {
                if (queryResult != ErrorSuccess)
                {
                    LastRadioFailure = $"WlanQueryInterface 无线电查询错误码 {queryResult}";
                    return false;
                }
                var states = ReadRadioStates(data, dataSize);
                LastRadioFailure = EnableSoftwareRadios(states, state =>
                    WlanSetInterface(handle, ref interfaceGuid, WlanOpcodeRadioState,
                        (uint)Marshal.SizeOf<WlanPhyRadioState>(), ref state, IntPtr.Zero), cancellationToken);
                return LastRadioFailure is null;
            }
            catch (InvalidOperationException ex)
            {
                LastRadioFailure = ex.Message;
                return false;
            }
            finally
            {
                if (data != IntPtr.Zero) WlanFreeMemory(data);
            }
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    internal static IReadOnlyList<WlanPhyRadioState> ReadRadioStates(IntPtr data, uint dataSize)
    {
        if (data == IntPtr.Zero || dataSize < sizeof(uint))
            throw new InvalidOperationException("WLAN 无线电查询返回空数据。");
        var count = Marshal.ReadInt32(data);
        var size = Marshal.SizeOf<WlanPhyRadioState>();
        if (count is < 1 or > 64 || dataSize < sizeof(uint) + count * size)
            throw new InvalidOperationException("WLAN 无线电查询返回无效的 PHY 数量或数据长度。");
        var states = new List<WlanPhyRadioState>();
        for (var index = 0; index < count; index++)
            states.Add(Marshal.PtrToStructure<WlanPhyRadioState>(IntPtr.Add(data, sizeof(uint) + index * size)));
        return states;
    }

    internal static string? EnableSoftwareRadios(IReadOnlyList<WlanPhyRadioState> states,
        Func<WlanPhyRadioState, uint> setRadio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (states.Count == 0) return "WLAN 未返回可用的无线电。";
        if (states.Any(state => state.HardwareState != RadioOn))
            return "Wi-Fi 硬件无线电关闭或状态未知，未尝试修改软件无线电。";
        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.SoftwareState == RadioOn) continue;
            var enabled = state;
            enabled.SoftwareState = RadioOn;
            var result = setRadio(enabled);
            if (result != ErrorSuccess) return $"WlanSetInterface 启用 PHY {state.PhyIndex} 失败，错误码 {result}";
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WlanPhyRadioState
    {
        public uint PhyIndex;
        public uint SoftwareState;
        public uint HardwareState;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanSetInterface(IntPtr clientHandle, ref Guid interfaceGuid, uint opcode,
        uint dataSize, ref WlanPhyRadioState data, IntPtr reserved);
}
