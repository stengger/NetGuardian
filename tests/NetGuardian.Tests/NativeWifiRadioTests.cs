using System.Runtime.InteropServices;
using NetGuardian.Core.Wifi;
using Xunit;
using RadioState = NetGuardian.Core.Wifi.NativeWifiScanner.WlanPhyRadioState;

namespace NetGuardian.Tests;

public sealed class NativeWifiRadioTests
{
    [Fact]
    public void 逐个启用关闭的软件无线电并跳过已经开启的PHY()
    {
        var written = new List<RadioState>();
        var failure = NativeWifiScanner.EnableSoftwareRadios(
            [State(3, 2, 1), State(7, 1, 1), State(9, 2, 1)],
            state => { written.Add(state); return 0; }, CancellationToken.None);
        Assert.Null(failure);
        Assert.Equal(new uint[] { 3, 9 }, written.Select(state => state.PhyIndex));
        Assert.All(written, state =>
        {
            Assert.Equal(1u, state.SoftwareState);
            Assert.Equal(1u, state.HardwareState);
        });
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    public void 硬件关闭或状态未知时不写入任何PHY(uint hardwareState)
    {
        var written = new List<RadioState>();
        var failure = NativeWifiScanner.EnableSoftwareRadios(
            [State(0, 2, 1), State(1, 2, hardwareState)],
            state => { written.Add(state); return 0; }, CancellationToken.None);
        Assert.Contains("硬件无线电", failure);
        Assert.Empty(written);
    }

    [Fact]
    public void 已经开启的无线电不执行写操作()
    {
        Assert.Null(NativeWifiScanner.EnableSoftwareRadios([State(0, 1, 1)],
            _ => throw new InvalidOperationException("不应写入"), CancellationToken.None));
    }

    [Fact]
    public void 写入失败保留错误码且不继续其他PHY()
    {
        var written = new List<uint>();
        var failure = NativeWifiScanner.EnableSoftwareRadios([State(4, 2, 1), State(5, 2, 1)],
            state => { written.Add(state.PhyIndex); return 5; }, CancellationToken.None);
        Assert.Contains("PHY 4", failure);
        Assert.Contains("错误码 5", failure);
        Assert.Equal(4u, Assert.Single(written));
    }

    [Fact]
    public void 无PHY时明确失败而不是报告成功()
    {
        Assert.NotNull(NativeWifiScanner.EnableSoftwareRadios([],
            _ => throw new InvalidOperationException("不应写入"), CancellationToken.None));
    }

    [Fact]
    public void 多PHY写入期间取消不再写入后续PHY()
    {
        using var cancellation = new CancellationTokenSource();
        var written = new List<uint>();
        Assert.ThrowsAny<OperationCanceledException>(() => NativeWifiScanner.EnableSoftwareRadios(
            [State(0, 2, 1), State(1, 2, 1)], state =>
            {
                written.Add(state.PhyIndex);
                cancellation.Cancel();
                return 0;
            }, cancellation.Token));
        Assert.Equal(0u, Assert.Single(written));
    }

    [Fact]
    public void 已取消时不写入任何PHY()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => NativeWifiScanner.EnableSoftwareRadios([State(0, 2, 1)],
            _ => throw new InvalidOperationException("不应写入"), cancellation.Token));
    }

    [Fact]
    public void 原生查询按DWORD计数和三个DWORD的PHY布局解析()
    {
        Assert.Equal(12, Marshal.SizeOf<RadioState>());
        var buffer = Marshal.AllocHGlobal(28);
        try
        {
            Marshal.WriteInt32(buffer, 2);
            Marshal.StructureToPtr(State(6, 2, 1), IntPtr.Add(buffer, 4), false);
            Marshal.StructureToPtr(State(8, 1, 2), IntPtr.Add(buffer, 16), false);
            var states = NativeWifiScanner.ReadRadioStates(buffer, 28);
            Assert.Equal(new uint[] { 6, 8 }, states.Select(state => state.PhyIndex));
            Assert.Equal(2u, states[0].SoftwareState);
            Assert.Equal(1u, states[0].HardwareState);
            Assert.Equal(1u, states[1].SoftwareState);
            Assert.Equal(2u, states[1].HardwareState);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [Theory]
    [InlineData(0, 4u)]
    [InlineData(-1, 4u)]
    [InlineData(65, 4u)]
    [InlineData(1, 15u)]
    [InlineData(2, 27u)]
    public void 无效数量或截断数据在读取PHY前拒绝(int count, uint dataSize)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, count);
            Assert.Throws<InvalidOperationException>(() => NativeWifiScanner.ReadRadioStates(buffer, dataSize));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [Fact]
    public void 空指针或不足DWORD的数据被拒绝()
    {
        Assert.Throws<InvalidOperationException>(() => NativeWifiScanner.ReadRadioStates(IntPtr.Zero, 100));
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Assert.Throws<InvalidOperationException>(() => NativeWifiScanner.ReadRadioStates(buffer, 3));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static RadioState State(uint index, uint software, uint hardware) =>
        new() { PhyIndex = index, SoftwareState = software, HardwareState = hardware };
}
