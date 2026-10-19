using System;
using System.ComponentModel;
using System.IO;

namespace NetGuardian.App.Presentation;

internal static class NetworkConfigurationRefresh
{
    public static bool TryRead(Action read, Action<Exception> onFailure)
    {
        try { read(); return true; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or NotSupportedException)
        {
            onFailure(ex);
            return false;
        }
    }
}
