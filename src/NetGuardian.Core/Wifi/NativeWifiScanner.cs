using System.Runtime.InteropServices;
using System.Text;
using NetGuardian.Core.Models;

namespace NetGuardian.Core.Wifi;

public sealed class NativeWifiScanner
{
    private const uint WlanClientVersion = 2;
    private const uint WlanAvailableNetworkIncludeAllAdhocProfiles = 0x00000001;
    private const uint WlanAvailableNetworkIncludeAllManualHiddenProfiles = 0x00000002;
    private const uint ErrorSuccess = 0;

    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var openResult = WlanOpenHandle(WlanClientVersion, IntPtr.Zero, out _, out var handle);
        if (openResult != ErrorSuccess)
        {
            return [];
        }

        try
        {
            var interfaces = EnumerateInterfaces(handle);
            foreach (var wlanInterface in interfaces)
            {
                var interfaceGuid = wlanInterface.InterfaceGuid;
                WlanScan(handle, ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            var networks = new List<WifiNetwork>();
            foreach (var wlanInterface in interfaces)
            {
                networks.AddRange(GetAvailableNetworks(handle, wlanInterface.InterfaceGuid));
            }

            return networks
                .Where(network => !string.IsNullOrWhiteSpace(network.Ssid))
                .GroupBy(network => network.Ssid)
                .Select(group => group.OrderByDescending(network => network.SignalPercent).First())
                .OrderByDescending(network => network.SignalPercent)
                .ThenBy(network => network.Ssid)
                .ToList();
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    private static IReadOnlyList<WlanInterfaceInfo> EnumerateInterfaces(IntPtr handle)
    {
        var result = WlanEnumInterfaces(handle, IntPtr.Zero, out var listPointer);
        if (result != ErrorSuccess || listPointer == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var numberOfItems = Marshal.ReadInt32(listPointer);
            var current = IntPtr.Add(listPointer, 8);
            var itemSize = Marshal.SizeOf<WlanInterfaceInfo>();
            var interfaces = new List<WlanInterfaceInfo>();
            for (var index = 0; index < numberOfItems; index++)
            {
                var item = Marshal.PtrToStructure<WlanInterfaceInfo>(current);
                interfaces.Add(item);
                current = IntPtr.Add(current, itemSize);
            }

            return interfaces;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    private static IReadOnlyList<WifiNetwork> GetAvailableNetworks(IntPtr handle, Guid interfaceGuid)
    {
        var flags = WlanAvailableNetworkIncludeAllAdhocProfiles | WlanAvailableNetworkIncludeAllManualHiddenProfiles;
        var result = WlanGetAvailableNetworkList(handle, ref interfaceGuid, flags, IntPtr.Zero, out var listPointer);
        if (result != ErrorSuccess || listPointer == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var numberOfItems = Marshal.ReadInt32(listPointer);
            var current = IntPtr.Add(listPointer, 8);
            var itemSize = Marshal.SizeOf<WlanAvailableNetwork>();
            var networks = new List<WifiNetwork>();

            for (var index = 0; index < numberOfItems; index++)
            {
                var item = Marshal.PtrToStructure<WlanAvailableNetwork>(current);
                var ssid = DecodeSsid(item.Dot11Ssid);
                networks.Add(new WifiNetwork(
                    ssid,
                    (int)item.WlanSignalQuality,
                    FormatAuth(item.Dot11DefaultAuthAlgorithm),
                    FormatCipher(item.Dot11DefaultCipherAlgorithm),
                    item.SecurityEnabled,
                    item.NetworkConnectable));
                current = IntPtr.Add(current, itemSize);
            }

            return networks;
        }
        finally
        {
            WlanFreeMemory(listPointer);
        }
    }

    internal static string DecodeSsid(Dot11Ssid ssid)
    {
        var length = Math.Min((int)ssid.SsidLength, ssid.SsidBytes.Length);
        return Encoding.UTF8.GetString(ssid.SsidBytes, 0, length);
    }

    private static string FormatAuth(Dot11AuthAlgorithm value)
    {
        return value switch
        {
            Dot11AuthAlgorithm.Open => "Open",
            Dot11AuthAlgorithm.Wpa => "WPA",
            Dot11AuthAlgorithm.WpaPsk => "WPA-PSK",
            Dot11AuthAlgorithm.WpaNone => "WPA-None",
            Dot11AuthAlgorithm.Rsna => "WPA2",
            Dot11AuthAlgorithm.RsnaPsk => "WPA2-PSK",
            Dot11AuthAlgorithm.Wpa3 => "WPA3",
            Dot11AuthAlgorithm.Wpa3Sae => "WPA3-SAE",
            _ => value.ToString()
        };
    }

    private static string FormatCipher(Dot11CipherAlgorithm value)
    {
        return value switch
        {
            Dot11CipherAlgorithm.None => "None",
            Dot11CipherAlgorithm.Wep40 => "WEP-40",
            Dot11CipherAlgorithm.Tkip => "TKIP",
            Dot11CipherAlgorithm.Ccmp => "AES/CCMP",
            Dot11CipherAlgorithm.Wep104 => "WEP-104",
            Dot11CipherAlgorithm.Gcmp => "GCMP",
            Dot11CipherAlgorithm.Gcmp256 => "GCMP-256",
            Dot11CipherAlgorithm.Ccmp256 => "CCMP-256",
            _ => value.ToString()
        };
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(
        uint clientVersion,
        IntPtr reserved,
        out uint negotiatedVersion,
        out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(
        IntPtr clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanScan(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        IntPtr dot11Ssid,
        IntPtr ieData,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        uint flags,
        IntPtr reserved,
        out IntPtr availableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string InterfaceDescription;

        public uint InterfaceState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Dot11Ssid
    {
        public uint SsidLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] SsidBytes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanAvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public Dot11Ssid Dot11Ssid;
        public uint Dot11BssType;
        public uint NumberOfBssids;

        [MarshalAs(UnmanagedType.Bool)]
        public bool NetworkConnectable;

        public uint NotConnectableReason;
        public uint NumberOfPhyTypes;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] Dot11PhyTypes;

        [MarshalAs(UnmanagedType.Bool)]
        public bool MorePhyTypes;

        public uint WlanSignalQuality;

        [MarshalAs(UnmanagedType.Bool)]
        public bool SecurityEnabled;

        public Dot11AuthAlgorithm Dot11DefaultAuthAlgorithm;
        public Dot11CipherAlgorithm Dot11DefaultCipherAlgorithm;
        public uint Flags;
        public uint Reserved;
    }

    private enum Dot11AuthAlgorithm : uint
    {
        Open = 1,
        SharedKey = 2,
        Wpa = 3,
        WpaPsk = 4,
        WpaNone = 5,
        Rsna = 6,
        RsnaPsk = 7,
        Wpa3 = 8,
        Wpa3Sae = 9
    }

    private enum Dot11CipherAlgorithm : uint
    {
        None = 0x00,
        Wep40 = 0x01,
        Tkip = 0x02,
        Ccmp = 0x04,
        Wep104 = 0x05,
        Gcmp = 0x08,
        Gcmp256 = 0x09,
        Ccmp256 = 0x0A,
        Wep = 0x101
    }
}
