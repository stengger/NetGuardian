using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace NetGuardian.App.Updates;

internal static class UpdateProxyPolicy
{
    internal static bool AllowMirrorDirect()
    {
        try
        {
            // 机器级策略只能限制已内置的线路；未知值或读取失败时服从系统代理。
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var policy = machine.OpenSubKey(@"SOFTWARE\Policies\NetGuardian\Updates");
            var value = policy?.GetValue("ProxyOnly");
            return value is null or 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }
}
