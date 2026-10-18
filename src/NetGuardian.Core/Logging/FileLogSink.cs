using System.Text;
using System.Threading.Channels;

namespace NetGuardian.Core.Logging;

/// <summary>Receives log lines. Implementations decide where they go.</summary>
public interface ILogSink
{
    void Write(string message);
}

/// <summary>
/// Daily-rolling file log. Lines are queued to a background writer so callers never block on I/O.
/// Files: {directory}/app-yyyyMMdd.log — files older than <see cref="RetentionDays"/> are deleted.
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    public const int DefaultRetentionDays = 14;

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true
    });
    private readonly Task _writerTask;
    private string? _lastCleanupDate;

    public FileLogSink(string? directory = null, int retentionDays = DefaultRetentionDays, TimeProvider? timeProvider = null)
    {
        _directory = directory ?? GetDefaultDirectory();
        RetentionDays = Math.Clamp(retentionDays, 1, 90);
        _time = timeProvider ?? TimeProvider.System;
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public int RetentionDays { get; }
    public string Directory => _directory;

    public void Write(string message)
    {
        var line = $"{_time.GetLocalNow():yyyy-MM-dd HH:mm:ss zzz}  {message}";
        _queue.Writer.TryWrite(line);
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var line in _queue.Reader.ReadAllAsync())
        {
            try
            {
                var now = _time.GetLocalNow();
                var date = now.ToString("yyyyMMdd");
                System.IO.Directory.CreateDirectory(_directory);
                if (_lastCleanupDate != date)
                {
                    _lastCleanupDate = date;
                    CleanupOldFiles(now);
                }

                var path = Path.Combine(_directory, $"app-{date}.log");
                await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down. Drop the line.
            }
        }
    }

    private void CleanupOldFiles(DateTimeOffset now)
    {
        try
        {
            var cutoff = now.AddDays(-RetentionDays);
            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "app-*.log"))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (stem.Length == "app-yyyyMMdd".Length
                    && DateTimeOffset.TryParseExact(stem["app-".Length..], "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.AssumeLocal, out var fileDate)
                    && fileDate < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
    }

    private static string GetDefaultDirectory()
    {
        return Path.Combine(NetGuardian.Core.Runtime.GuardianPaths.DefaultDirectory, "logs");
    }
}
