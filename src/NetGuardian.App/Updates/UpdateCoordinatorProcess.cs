using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NetGuardian.App.Updates;

internal static class UpdateCoordinatorProcess
{
    internal static ProcessStartInfo CreateStartInfo(string script, string requestFile, string job)
    {
        // 固定引导命令只读取环境中的路径，避免路径被当作 PowerShell 表达式解析。
        const string bootstrap = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); try { & $env:NETGUARDIAN_UPDATE_SCRIPT -RequestFile $env:NETGUARDIAN_UPDATE_REQUEST } catch { [Console]::Error.WriteLine(($_ | Out-String)); exit 1 }";
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true, StandardErrorEncoding = new UTF8Encoding(false), WorkingDirectory = job
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-OutputFormat", "Text", "-Command", bootstrap })
            info.ArgumentList.Add(argument);
        info.Environment["NETGUARDIAN_UPDATE_SCRIPT"] = script;
        info.Environment["NETGUARDIAN_UPDATE_REQUEST"] = requestFile;
        return info;
    }

    internal static async Task<IOException> ReadFailureAsync(Process process, Task<string> errorOutput, string job)
    {
        await process.WaitForExitAsync();
        var detail = (await errorOutput).Trim();
        var logFile = Path.Combine(job, "coordinator-stderr.log");
        if (detail.Length == 0) detail = "协助进程未返回详细错误。";
        await File.WriteAllTextAsync(logFile, detail, new UTF8Encoding(true));
        return new IOException($"更新协调进程提前退出（{process.ExitCode}）：{detail}{Environment.NewLine}错误记录：{logFile}");
    }
}