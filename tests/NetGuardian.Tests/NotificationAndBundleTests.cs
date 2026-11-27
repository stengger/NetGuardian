using System.IO.Compression;
using System.Text.Json;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Models;
using NetGuardian.Core.Network;
using NetGuardian.Core.Notifications;
using NetGuardian.Core.Settings;
using Xunit;

namespace NetGuardian.Tests;

public sealed class NotificationAndBundleTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ng_notify_bundle_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void BuildPayloadJson_FormatsEachChannelDistinctly()
    {
        var msg = new AlertNotificationMessage(
            "id-1",
            AlertEventKind.OfflineDetected,
            "断网告警",
            "网络不可达",
            "Campus-WiFi",
            DateTimeOffset.UtcNow);

        var generic = AlertNotificationService.BuildPayloadJson(NotificationChannelType.GenericWebhook, msg);
        var wecom = AlertNotificationService.BuildPayloadJson(NotificationChannelType.WeCom, msg);
        var feishu = AlertNotificationService.BuildPayloadJson(NotificationChannelType.Feishu, msg);
        var dingtalk = AlertNotificationService.BuildPayloadJson(NotificationChannelType.DingTalk, msg);

        Assert.Contains("\"app\":\"NetGuardian\"", generic);
        Assert.Contains("\"msgtype\":\"markdown\"", wecom);
        Assert.Contains("\"msg_type\":\"text\"", feishu);
        Assert.Contains("\"msgtype\":\"text\"", dingtalk);
    }

    [Fact]
    public async Task NotifyAsync_QueuesFailedAlertAndFlushesWhenOnline()
    {
        var shouldSucceed = false;
        var sentCount = 0;
        var svc = new AlertNotificationService(
            dataDirectory: _tempDir,
            transport: (_, _, _, _) =>
            {
                if (!shouldSucceed)
                {
                    return Task.FromResult<(bool, string?)>((false, "offline timeout"));
                }

                sentCount++;
                return Task.FromResult<(bool, string?)>((true, null));
            });

        var settings = new NotificationSettings
        {
            Enabled = true,
            Channel = NotificationChannelType.WeCom,
            WebhookUrl = "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=secret-demo-key-12345"
        };

        var msg = new AlertNotificationMessage(
            "m1",
            AlertEventKind.OfflineDetected,
            "掉线",
            "无法访问互联网",
            "Lab-5G",
            DateTimeOffset.UtcNow);

        var firstResult = await svc.NotifyAsync(settings, msg);
        Assert.False(firstResult.Succeeded);
        Assert.True(firstResult.QueuedForRetry);
        Assert.DoesNotContain("secret-demo-key-12345", firstResult.ErrorMessage ?? string.Empty);
        Assert.Single(svc.GetPendingSnapshot());

        shouldSucceed = true;
        var flushed = await svc.FlushPendingAsync(settings);
        Assert.Equal(1, flushed);
        Assert.Equal(1, sentCount);
        Assert.Empty(svc.GetPendingSnapshot());
    }

    [Fact]
    public void RedactWebhookUrl_MasksQueryKeysAndPathSecrets()
    {
        var ding = NotificationSettings.RedactWebhookUrl(
            "https://oapi.dingtalk.com/robot/send?access_token=abcdef1234567890");
        Assert.DoesNotContain("abcdef1234567890", ding);
        Assert.Contains("access_token=ab***90", ding);

        var feishu = NotificationSettings.RedactWebhookUrl(
            "https://open.feishu.cn/open-apis/bot/v2/hook/9f8e7d6c5b4a3210");
        Assert.DoesNotContain("9f8e7d6c5b4a3210", feishu);
        Assert.Contains("9f***10", feishu);
    }

    [Fact]
    public void DiagnosticBundleExporter_RedactsSecretsAndExcludesPortalCredentials()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "logs"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "portal-credentials"));

        var today = DateTimeOffset.Now;
        File.WriteAllText(Path.Combine(_tempDir, "logs", $"app-{today:yyyyMMdd}.log"), "2026-11-27 info line");
        File.WriteAllText(Path.Combine(_tempDir, "logs", $"app-{today.AddDays(-20):yyyyMMdd}.log"), "old expired line");
        File.WriteAllText(Path.Combine(_tempDir, "portal-credentials", "Campus.dat"), "TOP_SECRET_CREDENTIAL");
        File.WriteAllText(Path.Combine(_tempDir, "guardian-status.json"), "{\"State\":\"Online\"}");

        var settings = new AppSettings
        {
            Notifications = new NotificationSettings
            {
                Enabled = true,
                Channel = NotificationChannelType.Feishu,
                WebhookUrl = "https://open.feishu.cn/open-apis/bot/v2/hook/my-super-secret-hook-token"
            },
            Profiles =
            [
                new WifiProfile
                {
                    Ssid = "Campus",
                    Priority = 0,
                    PortalAuth = new PortalAuthConfig
                    {
                        Enabled = true,
                        Ready = true,
                        Mode = PortalAuthMode.CredentialsForm,
                        AllowedOrigin = "https://portal.campus.edu",
                        AllowedLoginPath = "/login",
                        CredentialId = "secret999"
                    }
                }
            ]
        };

        var zipPath = Path.Combine(_tempDir, "bundle.zip");
        var proxySnapshot = new ProxyAndVpnSnapshot(
            false, null, null, null, null, null, [], [], DateTimeOffset.UtcNow);

        var result = DiagnosticBundleExporter.Export(zipPath, settings, _tempDir, proxySnapshot, recentDays: 7);
        Assert.True(File.Exists(result.OutputZipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("settings.redacted.json", names);
        Assert.Contains("environment/proxy-vpn.json", names);
        Assert.Contains($"logs/app-{today:yyyyMMdd}.log", names);
        Assert.DoesNotContain($"logs/app-{today.AddDays(-20):yyyyMMdd}.log", names);
        Assert.DoesNotContain(names, n => n.Contains("portal-credentials", StringComparison.OrdinalIgnoreCase));

        using var sr = new StreamReader(archive.GetEntry("settings.redacted.json")!.Open());
        var settingsJson = sr.ReadToEnd();
        Assert.DoesNotContain("my-super-secret-hook-token", settingsJson);
        Assert.DoesNotContain("secret999", settingsJson);
    }
}
