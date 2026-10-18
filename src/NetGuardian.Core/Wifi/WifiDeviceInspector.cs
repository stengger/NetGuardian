using System.Text.Json;
using NetGuardian.Core.SystemCommands;

namespace NetGuardian.Core.Wifi;

public sealed record WifiDeviceStatus(string InstanceId, string Name, int ProblemCode);

/// <summary>只在 WLAN 接口缺失时读取 PnP，避免每次正常检测启动 PowerShell。</summary>
public sealed class WifiDeviceInspector(ISystemCommandRunner commands)
{
    public async Task<WifiDeviceStatus?> FindFaultAsync(CancellationToken cancellationToken = default)
    {
        const string script = "$ErrorActionPreference='Stop'; Get-CimInstance Win32_PnPEntity -Filter \"PNPClass='Net'\" | Where-Object { $_.Name -match 'Wi-Fi|Wireless|802\\.11|MT792[0-9]' -and $_.ConfigManagerErrorCode -ne 0 } | Select-Object -First 1 @{n='InstanceId';e={$_.PNPDeviceID}},Name,@{n='ProblemCode';e={$_.ConfigManagerErrorCode}} | ConvertTo-Json -Compress";
        var result = await commands.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], cancellationToken);
        return result.Ok ? Parse(result.Output) : null;
    }

    public static WifiDeviceStatus? Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<WifiDeviceStatus>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return value is { ProblemCode: > 0 } && !string.IsNullOrWhiteSpace(value.InstanceId) && !string.IsNullOrWhiteSpace(value.Name) ? value : null;
        }
        catch (JsonException) { return null; }
    }
}
