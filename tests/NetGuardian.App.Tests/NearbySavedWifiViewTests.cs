using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using NetGuardian.Core.Settings;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Models;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class NearbySavedWifiViewTests
{
    [Fact]
    public void 标记与选中提示的序号采用当前附近列表且保留禁用网络的位置()
    {
        var hidden = new WifiProfile { Ssid = "家里", Priority = 1, CredentialSaved = true };
        var first = new WifiProfile { Ssid = "附近甲", Priority = 38, CredentialSaved = true, Enabled = false };
        var second = new WifiProfile { Ssid = "附近乙", Priority = 56, CredentialSaved = true };
        var unsaved = new WifiProfile { Ssid = "未保存", Priority = 58 };
        var profiles = new ObservableCollection<WifiProfile> { hidden, first, second, unsaved };
        var nearby = new NearbySavedWifiView(profiles);
        nearby.Update(["附近甲", "附近乙", "未保存"]);
        Assert.Equal(1, nearby.GetPriority(first));
        Assert.Equal(2, nearby.GetPriority(second));
        Assert.Null(nearby.GetPriority(hidden));
        Assert.Null(nearby.GetPriority(unsaved));
        Assert.Null(nearby.GetPriority(null));
        nearby.Update(["附近乙"]);
        Assert.Equal(1, nearby.GetPriority(second));
        Assert.Null(nearby.GetPriority(first));
        Assert.Equal(new[] { 1, 38, 56, 58 }, profiles.Select(p => p.Priority));
    }
    [Fact]
    public async Task 中文混合编码保存配置匹配附近SSID并显示已有恢复优先级()
    {
        const string ssid = "8b304北_5G_Wi-Fi5";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(936);
        var bytes = oem.GetBytes("SSID 名称 : “").Concat(Encoding.UTF8.GetBytes(ssid))
            .Concat(oem.GetBytes("”\r\n名称 : WLAN\r\n状态 : 已连接\r\nSSID : "))
            .Concat(Encoding.UTF8.GetBytes(ssid)).Concat(new byte[] { 13, 10 }).ToArray();
        var runner = (ISystemCommandRunner)typeof(NetshWifiClient)
            .GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(new NetshWifiClient())!;
        var result = await runner.RunAsync("powershell.exe", ["-NoProfile", "-Command",
            "$bytes = [Convert]::FromBase64String('" + Convert.ToBase64String(bytes)
                + "'); [Console]::OpenStandardOutput().Write($bytes, 0, $bytes.Length)"]);
        Assert.True(result.Ok, result.Output);
        var connection = NetshWifiClient.ParseConnectionInfo(result.Output);
        var profileSsid = NetshWifiClient.ParseProfileSsid(result.Output);
        var existing = new WifiProfile { Ssid = ssid, Priority = 3, CredentialSaved = false };
        var settings = new AppSettings { Profiles = [existing] };
        SavedWifiProfileImporter.Apply(settings, new(true, [new("真实配置北", profileSsid!)]), connection.Ssid);
        var visible = new NearbySavedWifiView(new ObservableCollection<WifiProfile>(settings.Profiles));
        visible.Update([ssid]);
        Assert.Equal(ssid, connection.Ssid);
        Assert.Same(existing, Assert.Single(visible.View.Cast<WifiProfile>()));
        Assert.Equal(3, existing.Priority);
        Assert.True(existing.CredentialSaved);
        Assert.Equal("真实配置北", existing.ConnectionProfileName);
    }

    [Fact]
    public void 只显示附近已保存网络且重新出现时保留原优先级和对象()
    {
        var nearby = new WifiProfile { Ssid = "8b304北", CredentialSaved = true, Priority = 3 };
        var historical = new WifiProfile { Ssid = "家里", CredentialSaved = true, Priority = 1 };
        var unsaved = new WifiProfile { Ssid = "未保存", CredentialSaved = false, Priority = 4 };
        var profiles = new ObservableCollection<WifiProfile> { historical, nearby, unsaved };
        var visible = new NearbySavedWifiView(profiles);
        visible.Update(["8b304北", "未保存", "陌生网络"]);
        Assert.Same(nearby, Assert.Single(visible.View.Cast<WifiProfile>()));
        visible.Update([]);
        Assert.Empty(visible.View.Cast<WifiProfile>());
        visible.Update(["家里", "8b304北"]);
        Assert.Equal(new[] { historical, nearby }, visible.View.Cast<WifiProfile>());
        Assert.Equal(3, nearby.Priority);
        Assert.Equal(3, profiles.Count);
        nearby.CredentialSaved = false;
        visible.View.Refresh();
        Assert.Same(historical, Assert.Single(visible.View.Cast<WifiProfile>()));
    }

    [Fact]
    public void 切换到全部配置后可查看历史和缺少凭据的候选()
    {
        var old = new WifiProfile { Ssid = "旧网络", CredentialSaved = true, Enabled = true, AutoConnect = false };
        var noCredential = new WifiProfile { Ssid = "凭据已删除" };
        var view = new NearbySavedWifiView(new ObservableCollection<WifiProfile> { old, noCredential });
        Assert.Empty(view.View.Cast<WifiProfile>());

        view.SetShowAll(true);
        Assert.Equal(new[] { old, noCredential }, view.View.Cast<WifiProfile>());
        Assert.False(old.ParticipatesInRecovery);
        old.ParticipatesInRecovery = true;
        Assert.True(old.Enabled && old.AutoConnect);
        Assert.True(old.IsRecoveryCandidate);
        Assert.False(noCredential.IsRecoveryCandidate);
        view.SetShowAll(false);
        Assert.Empty(view.View.Cast<WifiProfile>());
    }

    [Fact]
    public void 扫描前不展示历史记录且SSID大小写和空格不能混同()
    {
        var profiles = new ObservableCollection<WifiProfile>
        {
            new() { Ssid = "Home", CredentialSaved = true },
            new() { Ssid = "home", CredentialSaved = true },
            new() { Ssid = " Home ", CredentialSaved = true }
        };
        var visible = new NearbySavedWifiView(profiles);
        Assert.Empty(visible.View.Cast<WifiProfile>());
        visible.Update(["home"]);
        Assert.Equal("home", Assert.Single(visible.View.Cast<WifiProfile>()).Ssid);
    }
}
