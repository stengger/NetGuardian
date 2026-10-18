using System.Diagnostics;
using System.Text;

namespace NetGuardian.Core.SystemCommands;

public sealed record SystemCommandResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Runs an external system command (e.g. ipconfig) and captures its output.
/// </summary>
public interface ISystemCommandRunner
{
    Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

public sealed class ProcessCommandRunner : ISystemCommandRunner
{
    private readonly TimeSpan _timeout;
    private readonly Encoding? _outputEncoding;

    public ProcessCommandRunner(TimeSpan? timeout = null, Encoding? outputEncoding = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _outputEncoding = outputEncoding;
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<SystemCommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = _outputEncoding,
            StandardErrorEncoding = _outputEncoding,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return new SystemCommandResult(process.ExitCode, (await outputTask + await errorTask).Trim());
        }
        catch (OperationCanceledException)
        {
            // 取消等待不能留下仍会修改网络的子进程。
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            cancellationToken.ThrowIfCancellationRequested();
            return new SystemCommandResult(124, $"系统命令超时（{_timeout.TotalSeconds:g} 秒）：{fileName}");
        }
    }
}
