using System.Text;
using System.Text.Json;

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
        settings.Profiles = settings.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid))
            .OrderBy(profile => profile.Priority)
            .ToList();

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path + ".tmp";
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
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Policy.Normalize();
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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appData, "NetGuardian", "settings.json");
    }
}
