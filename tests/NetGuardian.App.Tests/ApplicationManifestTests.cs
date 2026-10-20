using System.Xml.Linq;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class ApplicationManifestTests
{
    [Fact]
    public void 应用清单声明逐屏高分辨率感知及兼容回退()
    {
        using var stream = typeof(ApplicationManifestTests).Assembly.GetManifestResourceStream("app.manifest")!;
        var document = XDocument.Load(stream);
        XNamespace assembly = "urn:schemas-microsoft-com:asm.v3";
        XNamespace legacy = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
        XNamespace modern = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
        var settings = document.Root!.Element(assembly + "application")?.Element(assembly + "windowsSettings");
        Assert.NotNull(settings);
        Assert.Equal("PerMonitorV2, PerMonitor", settings.Element(modern + "dpiAwareness")?.Value);
        Assert.Equal("true/pm", settings.Element(legacy + "dpiAware")?.Value);
    }
}
