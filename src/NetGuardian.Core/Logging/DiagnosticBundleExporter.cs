using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NetGuardian.Core.Network;
using NetGuardian.Core.Notifications;
using NetGuardian.Core.Runtime;
using NetGuardian.Core.Settings;

namespace NetGuardian.Core.Logging;

/// <summary>诊断包导出结果。</summary>
public sealed record DiagnosticBundleResult(
    string OutputZipPath,
    int FileCount,
    long TotalBytes);

/// <summary>导出用于排查故障的脱敏诊断压缩包，严格排除门户凭据与敏感密钥。</summary>
public static class DiagnosticBundleExporter
{
    private const int DefaultRecentDays = 7;
    private const long MaxSingleLogFileBytes = 5L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>将脱敏配置、代理/VPN 快照、运行状态和最近 N 天日志/事件/基线打包为 zip 文件。</summary>
    public static DiagnosticBundleResult Export(
        string outputZipPath,
        AppSettings settings,
        string? dataDirectory = null,
        ProxyAndVpnSnapshot? proxySnapshotOverride = null,
        int recentDays = DefaultRecentDays)
    {
        if (string.IsNullOrWhiteSpace(outputZipPath))
        {
            throw new ArgumentException("Output zip path cannot be empty.", nameof(outputZipPath));
        }

        var baseDir = string.IsNullOrWhiteSpace(dataDirectory) ? GuardianPaths.DefaultDirectory : dataDirectory;
        var days = Math.Clamp(recentDays, 1, 30);
        var nowUtc = DateTimeOffset.UtcNow;
        var cutoffLocalDate = DateOnly.FromDateTime(nowUtc.ToLocalTime().DateTime.AddDays(-(days - 1)));
        var cutoffMonth = new DateOnly(cutoffLocalDate.Year, cutoffLocalDate.Month, 1);

        var outputDir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var tempZip = outputZipPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var includedEntries = new List<string>();
        try
        {
            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create, Encoding.UTF8))
            {
                var redactedSettings = BuildRedactedSettingsPayload(settings);
                WriteTextEntry(archive, "settings.redacted.json", JsonSerializer.Serialize(redactedSettings, JsonOptions));
                includedEntries.Add("settings.redacted.json");

                var proxySnapshot = proxySnapshotOverride ?? ProxyAndVpnInspector.Inspect();
                WriteTextEntry(archive, "environment/proxy-vpn.json", JsonSerializer.Serialize(proxySnapshot, JsonOptions));
                includedEntries.Add("environment/proxy-vpn.json");

                CopyOptionalFile(archive, Path.Combine(baseDir, "guardian-status.json"), "runtime/guardian-status.json", includedEntries);
                CopyOptionalFile(archive, Path.Combine(baseDir, "recovery-budget.json"), "runtime/recovery-budget.json", includedEntries);
                CopyOptionalFile(archive, Path.Combine(baseDir, "alerts", "pending-notifications.json"), "alerts/pending-notifications.json", includedEntries);

                var logsDir = Path.Combine(baseDir, "logs");
                if (Directory.Exists(logsDir))
                {
                    foreach (var file in Directory.GetFiles(logsDir).OrderBy(Path.GetFileName, StringComparer.Ordinal))
                    {
                        var fileName = Path.GetFileName(file);
                        if (!ShouldIncludeLogFile(fileName, cutoffLocalDate, cutoffMonth))
                        {
                            continue;
                        }

                        var entryName = $"logs/{fileName}";
                        CopyLogFileWithTailCap(archive, file, entryName);
                        includedEntries.Add(entryName);
                    }
                }

                var manifest = new
                {
                    exportedAtUtc = nowUtc,
                    appVersion = typeof(DiagnosticBundleExporter).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? typeof(DiagnosticBundleExporter).Assembly.GetName().Version?.ToString()
                        ?? "unknown",
                    osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    recentDays = days,
                    excludedSensitiveDirectories = new[] { "portal-credentials" },
                    files = includedEntries
                };
                WriteTextEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
                includedEntries.Add("manifest.json");
            }

            File.Move(tempZip, outputZipPath, overwrite: true);
            var info = new FileInfo(outputZipPath);
            return new DiagnosticBundleResult(outputZipPath, includedEntries.Count, info.Length);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    private static object BuildRedactedSettingsPayload(AppSettings settings)
    {
        return new
        {
            schemaVersion = settings.SchemaVersion,
            language = settings.Language,
            closeBehavior = settings.CloseBehavior,
            theme = settings.Theme,
            startMonitoringOnLaunch = settings.StartMonitoringOnLaunch,
            startMinimizedToTray = settings.StartMinimizedToTray,
            logRetentionDays = settings.LogRetentionDays,
            enableComputerRestart = settings.EnableComputerRestart,
            deviceFailureMinutesBeforeRestart = settings.DeviceFailureMinutesBeforeRestart,
            policy = settings.Policy,
            notifications = new
            {
                settings.Notifications.Enabled,
                channel = settings.Notifications.Channel.ToString(),
                webhookUrl = NotificationSettings.RedactWebhookUrl(settings.Notifications.WebhookUrl),
                settings.Notifications.NotifyOnOffline,
                settings.Notifications.NotifyOnRecovered,
                settings.Notifications.NotifyOnRecoveryFailed,
                settings.Notifications.NotifyOnHighRisk,
                settings.Notifications.TimeoutSeconds,
                settings.Notifications.MaxQueuedItems
            },
            profiles = settings.Profiles.Select(p => new
            {
                p.Ssid,
                p.Priority,
                p.Enabled,
                p.AutoConnect,
                portalAuth = p.PortalAuth is null
                    ? null
                    : new
                    {
                        p.PortalAuth.Enabled,
                        p.PortalAuth.Ready,
                        p.PortalAuth.Paused,
                        mode = p.PortalAuth.Mode.ToString(),
                        allowedOrigin = NotificationSettings.RedactWebhookUrl(p.PortalAuth.AllowedOrigin),
                        allowedLoginPath = p.PortalAuth.AllowedLoginPath,
                        hasCredentialId = !string.IsNullOrWhiteSpace(p.PortalAuth.CredentialId),
                        p.PortalAuth.LastAuthenticatedAt,
                        p.PortalAuth.LastFailureSummary
                    }
            }),
            excludedWifiSsids = settings.ExcludedWifiSsids
        };
    }

    private static bool ShouldIncludeLogFile(string fileName, DateOnly cutoffDate, DateOnly cutoffMonth)
    {
        if (fileName.StartsWith("app-", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            && fileName.Length == "app-yyyyMMdd.log".Length
            && DateOnly.TryParseExact(fileName.AsSpan(4, 8), "yyyyMMdd", out var logDate))
        {
            return logDate >= cutoffDate;
        }

        if (fileName.StartsWith("events-", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            && fileName.Length == "events-yyyyMM.jsonl".Length
            && DateOnly.TryParseExact(fileName.AsSpan(7, 6), "yyyyMM", out var eventMonth))
        {
            return eventMonth >= cutoffMonth;
        }

        if (fileName.StartsWith("quality-baseline-", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            && fileName.Length == "quality-baseline-yyyyMM.jsonl".Length
            && DateOnly.TryParseExact(fileName.AsSpan(17, 6), "yyyyMM", out var baselineMonth))
        {
            return baselineMonth >= cutoffMonth;
        }

        return false;
    }

    private static void CopyOptionalFile(ZipArchive archive, string sourcePath, string entryName, List<string> included)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var dst = entry.Open();
            fs.CopyTo(dst);
            included.Add(entryName);
        }
        catch (IOException)
        {
            // 如果文件正在被并发替换且瞬间锁住，跳过不影响整包导出。
        }
    }

    private static void CopyLogFileWithTailCap(ZipArchive archive, string sourcePath, string entryName)
    {
        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var dst = entry.Open();
        if (fs.Length <= MaxSingleLogFileBytes)
        {
            fs.CopyTo(dst);
            return;
        }

        var marker = Encoding.UTF8.GetBytes($"[NetGuardian] Truncated head; kept last {MaxSingleLogFileBytes} bytes.\n");
        dst.Write(marker, 0, marker.Length);
        fs.Seek(-MaxSingleLogFileBytes, SeekOrigin.End);
        fs.CopyTo(dst);
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
