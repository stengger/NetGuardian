using System.Security.Cryptography;
using NetGuardian.Core.Updates;
using Xunit;

namespace NetGuardian.Tests;

public sealed class OfficialUpdateFeedTests
{
    [Fact]
    public void 正式客户端默认自动换源并通过代理读取同一份清单和版本安装包()
    {
        var trust = Assert.IsType<UpdateTrust>(OfficialUpdateFeed.Trust);
        const string releases = "https://github.com/stengger/NetGuardian/releases/";
        const string proxy = "https://gh-proxy.org/";
        Assert.Equal(releases + "latest/download/stable.json", trust.Primary.Manifest.AbsoluteUri);
        Assert.Equal(proxy + trust.Primary.Manifest.AbsoluteUri, trust.Mirror.Manifest.AbsoluteUri);
        const string asset = "v1.2.3/NetGuardian-1.2.3-win-x64-Setup.exe";
        Assert.Equal(releases + "download/" + asset, new Uri(trust.Primary.PackageRoot, asset).AbsoluteUri);
        Assert.Equal(proxy + new Uri(trust.Primary.PackageRoot, asset).AbsoluteUri, new Uri(trust.Mirror.PackageRoot, asset).AbsoluteUri);
        Assert.Empty(trust.Mirror.RedirectOrigins);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(trust.PublicKey, out var consumed);
        Assert.Equal(trust.PublicKey.Length, consumed);
        Assert.Equal(ECCurve.NamedCurves.nistP256.Oid.Value, key.ExportParameters(false).Curve.Oid.Value);
        using var client = UpdateClient.CreateOfficial(() => throw new InvalidOperationException(), _ => { }, _ => throw new InvalidOperationException(), () => false);
        Assert.Equal(UpdateSourceMode.Auto, Assert.IsType<SignedUpdateService>(client.Service).Mode);
    }
}
