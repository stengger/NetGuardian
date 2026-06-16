using System.Text.Json;

namespace NetGuardian.Core.Settings;

public sealed class SettingsStore
{
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
        if (!File.Exists(Path))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(Path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.Policy.Normalize();
        settings.Profiles = settings.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Ssid))
            .OrderBy(profile => profile.Priority)
            .ToList();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.Policy.Normalize();
        settings.Profiles = settings.Profiles
            .OrderBy(profile => profile.Priority)
            .ToList();

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, Path, overwrite: true);
    }

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appData, "NetGuardian", "settings.json");
    }
}

