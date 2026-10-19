using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class NetworkOperationMessageTests
{
    [Fact]
    public void 切语言保留部分改变与读取失败两层信息()
    {
        var message = new NetworkOperationMessage("DnsMayHaveChanged", [5], true);
        var localizer = new Localizer();
        var chinese = message.Format(localizer);
        Assert.Contains("部分改变", chinese);
        Assert.Contains(localizer.T("NetworkReadFailed"), chinese);
        localizer.SetLanguage("en-US");
        var english = message.Format(localizer);
        Assert.Contains("partially changed", english);
        Assert.Contains(localizer.T("NetworkReadFailed"), english);
        Assert.Contains("5", english);
    }
}
