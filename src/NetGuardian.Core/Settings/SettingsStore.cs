using System.Text;
using System.Text.Json;
using NetGuardian.Core.Runtime;

namespace NetGuardian.Core.Settings;

public sealed class SettingsStore
{
    private readonly object _syncRoot = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SettingsStore(string? path = null)
    {
        Path = path ?? GetDefaultPath();
    }

    public string Path { get; }

    // 热加载失败时保留内存中的有效配置，不能被默认空列表覆盖。
    public bool TryLoad(out AppSettings settings)
    {
        lock (_syncRoot) return TryLoad(Path, out settings) || TryLoad(BackupPath, out settings);
    }

    public AppSettings Load()
    {
        lock (_syncRoot)
        {
            if (TryLoad(Path, out var settings))
            {
                return settings;
            }

            if (TryLoad(BackupPath, out settings))
            {
                SaveCore(settings, createBackup: false);
                return settings;
            }

            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_syncRoot)
        {
            SaveCore(settings, createBackup: true);
        }
    }

    private void SaveCore(AppSettings settings, bool createBackup)
    {
        settings.Policy.Normalize();
        settings.LogRetentionDays = Math.Clamp(settings.LogRetentionDays, 1, 90);
        settings.DeviceFailureMinutesBeforeRestart = Math.Clamp(settings.DeviceFailureMinutesBeforeRestart, 10, 24 * 60);
        settings.Profiles = settings.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid))
            .OrderBy(profile => profile.Priority)
            .ToList();

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
            if (createBackup && File.Exists(Path))
            {
                File.Copy(Path, BackupPath, overwrite: true);
            }

            File.Move(tempPath, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private string BackupPath => Path + ".bak";

    private static bool TryLoad(string path, out AppSettings settings)
    {
        settings = new AppSettings();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded?.Policy is null || loaded.Profiles is null || loaded.Profiles.Any(profile => profile is null)) return false;
            settings = loaded;
            settings.ExcludedWifiSsids ??= [];
            settings.Policy.Normalize();
            settings.LogRetentionDays = Math.Clamp(settings.LogRetentionDays, 1, 90);
            settings.DeviceFailureMinutesBeforeRestart = Math.Clamp(settings.DeviceFailureMinutesBeforeRestart, 10, 24 * 60);
            settings.Profiles = settings.Profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid))
                .OrderBy(profile => profile.Priority)
                .ToList();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            settings = new AppSettings();
            return false;
        }
    }

    private static string GetDefaultPath()
    {
        return System.IO.Path.Combine(GuardianPaths.DefaultDirectory, "settings.json");
    }
}
