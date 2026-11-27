using System.Text.Json;
using NetGuardian.Core.Models;
using NetGuardian.Core.Portal;
using NetGuardian.Core.Settings;
using Xunit;

namespace NetGuardian.Tests;

public sealed class PortalCredentialStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "NetGuardian-PortalCredTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 加密凭据支持保存读取更新删除与Unicode特殊字符()
    {
        var store = new DpapiPortalCredentialStore(_tempDir, useMachineScope: false);
        var id = Guid.NewGuid().ToString("N");
        var credential = new PortalCredential("2026001@campus", "p@ss&=+%中文😃!#");

        Assert.Equal("PortalCredential(***)", credential.ToString());
        store.Save(id, credential);
        Assert.True(store.Exists(id));

        var rawFileBytes = File.ReadAllText(Path.Combine(store.DirectoryPath, id + ".bin"));
        Assert.DoesNotContain("2026001", rawFileBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("p@ss", rawFileBytes, StringComparison.Ordinal);

        Assert.True(store.TryLoad(id, out var loaded));
        Assert.Equal(credential, loaded);

        var updated = new PortalCredential("2026002", "new-secret");
        store.Save(id, updated);
        Assert.True(store.TryLoad(id, out var reloaded));
        Assert.Equal(updated, reloaded);

        store.Delete(id);
        Assert.False(store.Exists(id));
        Assert.False(store.TryLoad(id, out _));
    }

    [Fact]
    public void 不同标识符不可互换解密且损坏密文安全失败()
    {
        var store = new DpapiPortalCredentialStore(_tempDir, useMachineScope: false);
        var id1 = Guid.NewGuid().ToString("N");
        var id2 = Guid.NewGuid().ToString("N");
        store.Save(id1, new PortalCredential("u1", "p1"));

        var file1 = Path.Combine(store.DirectoryPath, id1 + ".bin");
        var file2 = Path.Combine(store.DirectoryPath, id2 + ".bin");
        File.Copy(file1, file2, overwrite: true);

        Assert.False(store.TryLoad(id2, out _));

        File.WriteAllBytes(file1, [1, 2, 3, 4, 5]);
        Assert.False(store.TryLoad(id1, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("0123456789abcdef0123456789abcde")]
    public void 非法凭据标识符被拒绝(string badId)
    {
        var store = new DpapiPortalCredentialStore(_tempDir, useMachineScope: false);
        Assert.Throws<ArgumentException>(() => store.Save(badId, new PortalCredential("a", "b")));
        Assert.False(store.TryLoad(badId, out _));
        Assert.False(store.Exists(badId));
    }

    [Fact]
    public void 门户配置白名单与HTTPS约束及序列化隔离()
    {
        var id = Guid.NewGuid().ToString("N");
        var config = new PortalAuthConfig
        {
            Enabled = true,
            Ready = true,
            CredentialId = id,
            AllowedOrigin = "https://Portal.Example.Edu:443/",
            AllowedLoginPath = "/eportal/index.jsp",
            AllowedSubmitPath = "/eportal/InterFace.do",
            UsernameSelector = "#username",
            PasswordSelector = "#password",
            SubmitSelector = "#loginBtn"
        };
        config.Normalize();

        Assert.True(config.CanAutoAuthenticate);
        Assert.Equal("https://portal.example.edu", config.AllowedOrigin);
        Assert.True(config.MatchesEntryUri(new Uri("https://portal.example.edu/eportal/index.jsp?wlanuserip=10.0.0.1")));
        Assert.False(config.MatchesEntryUri(new Uri("http://portal.example.edu/eportal/index.jsp")));
        Assert.False(config.MatchesEntryUri(new Uri("https://evil.example.edu/eportal/index.jsp")));
        Assert.True(config.MatchesSubmitUri(new Uri("https://portal.example.edu/eportal/InterFace.do?method=login")));
        Assert.False(config.MatchesSubmitUri(new Uri("https://portal.example.edu/other")));

        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var settingsStore = new SettingsStore(settingsPath);
        settingsStore.Save(new AppSettings
        {
            Profiles =
            [
                new WifiProfile
                {
                    Ssid = "CampusNet",
                    CredentialSaved = true,
                    PortalAuth = config
                }
            ]
        });

        var json = File.ReadAllText(settingsPath);
        Assert.Contains(id, json, StringComparison.Ordinal);
        var loaded = settingsStore.Load();
        Assert.Single(loaded.Profiles);
        Assert.True(loaded.Profiles[0].PortalAuth?.CanAutoAuthenticate);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
