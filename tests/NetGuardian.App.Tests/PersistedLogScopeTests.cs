using NetGuardian.App.Presentation;
using NetGuardian.Core.Localization;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class PersistedLogScopeTests
{
    [Fact]
    public void 日志范围说明包含历史与后台而不是仅本窗口()
    {
        var text = new Localizer().T("LogSessionScope");
        Assert.DoesNotContain("本窗口", text);
        Assert.Contains("7 天", text);
        Assert.Contains("后台", text);
    }

    [Fact]
    public void 来源能够区分后台守护和恢复事件()
    {
        Assert.Contains("Background", Enum.GetNames<UiLogSource>());
        Assert.Contains("Recovery", Enum.GetNames<UiLogSource>());
    }
}
