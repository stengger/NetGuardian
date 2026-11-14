using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace NetGuardian.App.Presentation;

// 界面偏好独立于网络配置；异常或旧值均回退到可阅读的默认布局。
internal sealed record WifiLayoutPreferences(double HorizontalRatio = .5, double VerticalRatio = .5,
    bool NearbyExpanded = true, bool PriorityExpanded = true)
{
    internal static double Normalize(double value) => double.IsFinite(value) ? Math.Clamp(value, .25, .75) : .5;
    private const string Key = @"Software\NetGuardian\UiLayout";

    internal static WifiLayoutPreferences Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            int Value(string name, int fallback) => key?.GetValue(name) is int value ? value : fallback;
            return new(Normalize(Value("WifiHorizontalPermille", 500) / 1000d),
                Normalize(Value("WifiVerticalPermille", 500) / 1000d),
                Value("WifiNearbyExpanded", 1) != 0, Value("WifiPriorityExpanded", 1) != 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        { return new(); }
    }

    internal void Save()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue("WifiHorizontalPermille", (int)Math.Round(Normalize(HorizontalRatio) * 1000));
            key.SetValue("WifiVerticalPermille", (int)Math.Round(Normalize(VerticalRatio) * 1000));
            key.SetValue("WifiNearbyExpanded", NearbyExpanded ? 1 : 0);
            key.SetValue("WifiPriorityExpanded", PriorityExpanded ? 1 : 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
    }
}
