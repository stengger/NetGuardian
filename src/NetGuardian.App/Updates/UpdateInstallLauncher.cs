using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetGuardian.Core.Updates;

namespace NetGuardian.App.Updates;

internal sealed class UpdateInstallLauncher(UpdateStorage storage)
{
    internal static bool IsInstalledDirectory(string directory)
    {
        var prefix = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(directory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public async Task PrepareAsync(DownloadedUpdate update, CancellationToken token)
    {
        var directory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!IsInstalledDirectory(directory)) throw new InvalidOperationException("当前运行目录不支持原地安装，请从已安装版本打开。");
        UpdateStorage.AssertProtected(new DirectoryInfo(directory));
        var downloads = storage.PrepareDownloads();
        if (!Path.GetFullPath(update.InstallerPath).StartsWith(downloads + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("安装包不在受保护下载目录中。");
        UpdateStorage.AssertProtected(new FileInfo(update.InstallerPath));
        var job = storage.CreateJob();
        var source = Path.Combine(directory, "scripts", "update-netguardian.ps1");
        UpdateStorage.AssertProtected(new FileInfo(source));
        var script = Path.Combine(job, "update-netguardian.ps1");
        File.Copy(source, script);
        UpdateStorage.ProtectFile(script);
        using var parent = Process.GetCurrentProcess();
        var request = new
        {
            InstallDirectory = directory, update.InstallerPath, update.Sha256, Version = update.Version.ToString(),
            ParentProcessId = parent.Id, ParentStartedAt = parent.StartTime.ToUniversalTime().ToString("O"), JobDirectory = job
        };
        var requestFile = Path.Combine(job, "request.json");
        await File.WriteAllTextAsync(requestFile, JsonSerializer.Serialize(request), new UTF8Encoding(true), token);
        UpdateStorage.ProtectFile(requestFile);
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        using var process = Process.Start(new ProcessStartInfo(shell)
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RequestFile \"{requestFile}\"",
            UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = job
        }) ?? throw new IOException("无法启动更新协调进程。");
        var prepared = false;
        try
        {
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                var resultFile = Path.Combine(job, "result.json");
                if (File.Exists(resultFile))
                {
                    using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile, token));
                    throw new IOException(result.RootElement.GetProperty("Message").GetString() ?? "更新准备失败。");
                }
                var readyFile = Path.Combine(job, "ready.json");
                if (File.Exists(readyFile))
                {
                    using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(readyFile, token));
                    if (ready.RootElement.GetProperty("Ready").GetBoolean() && ready.RootElement.GetProperty("Version").GetString() == update.Version.ToString())
                    {
                        prepared = true;
                        return;
                    }
                    throw new IOException("更新准备信号无效。");
                }
                if (process.HasExited) throw new IOException($"更新协调进程提前退出（{process.ExitCode}）。");
                await Task.Delay(200, token);
            }
            throw new TimeoutException("更新准备超时，界面将保持打开。");
        }
        finally
        {
            // 超时也必须撤销握手，避免稍后普通退出界面触发遗留的安装请求。
            if (!prepared)
            {
                var cancel = Path.Combine(job, "cancel.request");
                await File.WriteAllTextAsync(cancel, "", CancellationToken.None);
                UpdateStorage.ProtectFile(cancel);
            }
        }
    }
}
