using System.Net;
using System.Net.NetworkInformation;
using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class NetworkAdapterPresentationTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 链路启用不冒充互联网可达且类型不泄漏枚举名(string language)
    {
        var localizer = new Localizer();
        localizer.SetLanguage(language);
        var status = NetworkAdapterPresentation.Status(OperationalStatus.Up, localizer);
        Assert.NotEqual("LinkUp", status);
        Assert.Equal(localizer.T("LinkUp"), status);
        Assert.NotEqual(localizer.T("Healthy"), status);
        Assert.NotEqual("Wireless80211", NetworkAdapterPresentation.Type(NetworkInterfaceType.Wireless80211, localizer));
        Assert.NotEqual("AdapterTypeWifi", NetworkAdapterPresentation.Type(NetworkInterfaceType.Wireless80211, localizer));
        Assert.Equal(localizer.T("AdapterTypeOther"), NetworkAdapterPresentation.Type((NetworkInterfaceType)9999, localizer));
        Assert.Equal(localizer.T("LinkUnknown"), NetworkAdapterPresentation.Status((OperationalStatus)9999, localizer));
    }

    [Fact]
    public void 两页使用同一DNS格式且保留IPv6与未知空值()
    {
        Assert.Equal("223.5.5.5, 2001:4860:4860::8888", NetworkAdapterPresentation.Addresses([
            IPAddress.Parse("223.5.5.5"), IPAddress.Parse("2001:4860:4860::8888"), IPAddress.Parse("223.5.5.5")]));
        Assert.Equal("—", NetworkAdapterPresentation.Addresses([]));
    }
}
