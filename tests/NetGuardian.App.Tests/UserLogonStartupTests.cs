using System.IO;
using System.Xml.Linq;
using NetGuardian.App.Startup;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class UserLogonStartupTests
{
    private const string Executable = @"C:\Program Files\NetGuardian\NetGuardian.exe";
    private const string UserId = "S-1-5-21-100-200-300-1001";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void 登录任务只在当前用户交互会话运行且不限电池与运行时长()
    {
        var store = new MemoryTaskStore();
        var startup = new UserLogonStartup(store, Executable, UserId);
        Assert.False(startup.IsEnabled());

        startup.SetEnabled(true);

        Assert.True(startup.IsEnabled());
        var xml = XDocument.Parse(store.Xml!);
        var trigger = Assert.Single(xml.Descendants(Ns + "LogonTrigger"));
        Assert.Equal(UserId, trigger.Element(Ns + "UserId")?.Value);
        var principal = Assert.Single(xml.Descendants(Ns + "Principal"));
        Assert.Equal(UserId, principal.Element(Ns + "UserId")?.Value);
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")?.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")?.Value);
        var action = Assert.Single(xml.Descendants(Ns + "Exec"));
        Assert.Equal(Executable, action.Element(Ns + "Command")?.Value);
        Assert.Equal(@"C:\Program Files\NetGuardian", action.Element(Ns + "WorkingDirectory")?.Value);
        Assert.Null(action.Element(Ns + "Arguments"));
        Assert.Equal("false", xml.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("false", xml.Descendants(Ns + "StopIfGoingOnBatteries").Single().Value);
        Assert.Equal("PT0S", xml.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
    }

    [Fact]
    public void 关闭登录启动删除任务且重复关闭无错误()
    {
        var store = new MemoryTaskStore();
        var startup = new UserLogonStartup(store, Executable, UserId);
        startup.SetEnabled(true);

        startup.SetEnabled(false);
        startup.SetEnabled(false);

        Assert.False(startup.IsEnabled());
        Assert.Null(store.Xml);
    }

    [Theory]
    [InlineData("Command", @"C:\Other\NetGuardian.exe")]
    [InlineData("UserId", "S-1-5-18")]
    [InlineData("LogonType", "ServiceAccount")]
    [InlineData("RunLevel", "LeastPrivilege")]
    public void 不覆盖或删除目标目录与用户不匹配的任务(string element, string value)
    {
        var store = new MemoryTaskStore();
        var startup = new UserLogonStartup(store, Executable, UserId);
        startup.SetEnabled(true);
        var xml = XDocument.Parse(store.Xml!);
        foreach (var node in xml.Descendants(Ns + element)) node.Value = value;
        store.Xml = xml.ToString();
        var previous = store.Xml;

        Assert.False(startup.IsEnabled());
        Assert.Throws<InvalidOperationException>(() => startup.SetEnabled(true));
        Assert.Throws<InvalidOperationException>(() => startup.SetEnabled(false));
        Assert.Equal(previous, store.Xml);
    }

    [Fact]
    public void 系统中禁用任务后开关显示关闭并允许重新启用()
    {
        var store = new MemoryTaskStore();
        var startup = new UserLogonStartup(store, Executable, UserId);
        startup.SetEnabled(true);
        var xml = XDocument.Parse(store.Xml!);
        xml.Root!.Element(Ns + "Settings")!.Element(Ns + "Enabled")!.Value = "false";
        store.Xml = xml.ToString();

        Assert.False(startup.IsEnabled());
        startup.SetEnabled(true);
        Assert.True(startup.IsEnabled());
    }

    [Fact]
    public void 注册失败不能报告已开启()
    {
        var store = new MemoryTaskStore { FailWrites = true };
        var startup = new UserLogonStartup(store, Executable, UserId);

        Assert.Throws<IOException>(() => startup.SetEnabled(true));
        Assert.False(startup.IsEnabled());
    }

    private sealed class MemoryTaskStore : ILogonTaskStore
    {
        public string? Xml { get; set; }
        public bool FailWrites { get; init; }
        public string? Read(string taskName) => Xml;
        public void Write(string taskName, string xml, string userId)
        {
            if (FailWrites) throw new IOException("测试注册失败");
            Xml = xml;
        }
        public void Delete(string taskName) => Xml = null;
    }
}
