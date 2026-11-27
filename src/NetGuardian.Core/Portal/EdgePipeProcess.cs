using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;

namespace NetGuardian.Core.Portal;

/// <summary>
/// 通过 Win32 继承匿名管道（FD 3/4）与无头 Edge 建立不暴露本机调试端口的私有 CDP 通道，
/// 并借助 Job Object 确保任意异常或取消时整棵浏览器进程树及临时配置目录被彻底清理。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class EdgePipeProcess : IAsyncDisposable, IDisposable
{
    private const uint HandleFlagInherit = 0x00000001;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const byte CrtFileOpen = 0x01;
    private const byte CrtFilePipe = 0x08;

    private readonly string _profileDirectory;
    private readonly IntPtr _jobHandle;
    private readonly IntPtr _processHandle;
    private readonly int _processId;
    private readonly FileStream _pipeWriter;
    private readonly FileStream _pipeReader;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly CancellationTokenSource _readerCts = new();
    private readonly Task _readLoop;
    private long _nextId;
    private int _disposed;

    public event Func<string, JsonObject, Task>? NotificationReceived;

    private EdgePipeProcess(
        string profileDirectory,
        IntPtr jobHandle,
        IntPtr processHandle,
        int processId,
        FileStream pipeWriter,
        FileStream pipeReader)
    {
        _profileDirectory = profileDirectory;
        _jobHandle = jobHandle;
        _processHandle = processHandle;
        _processId = processId;
        _pipeWriter = pipeWriter;
        _pipeReader = pipeReader;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public int ProcessId => _processId;

    public static EdgePipeProcess Start(string edgePath, string profileDirectory)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Edge 私有管道仅支持 Windows。");

        Directory.CreateDirectory(profileDirectory);

        SafeFileHandle? parentWrite = null;
        SafeFileHandle? childRead = null;
        SafeFileHandle? childWrite = null;
        SafeFileHandle? parentRead = null;
        var reserved2Buffer = IntPtr.Zero;
        var jobHandle = IntPtr.Zero;
        var processHandle = IntPtr.Zero;
        var threadHandle = IntPtr.Zero;

        try
        {
            var sa = new SecurityAttributes
            {
                nLength = Marshal.SizeOf<SecurityAttributes>(),
                bInheritHandle = true,
                lpSecurityDescriptor = IntPtr.Zero
            };

            if (!NativeMethods.CreatePipe(out childRead, out parentWrite, ref sa, 0)
                || !NativeMethods.CreatePipe(out parentRead, out childWrite, ref sa, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "创建 Edge 控制管道失败。");
            }

            if (!NativeMethods.SetHandleInformation(parentWrite, HandleFlagInherit, 0)
                || !NativeMethods.SetHandleInformation(parentRead, HandleFlagInherit, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "配置管道继承标志失败。");
            }

            // 构造 MSVC CRT 5 槽位继承信息：fd 3 读取父端指令，fd 4 写入回执。
            const int fdCount = 5;
            var reservedSize = 4 + fdCount + fdCount * IntPtr.Size;
            reserved2Buffer = Marshal.AllocHGlobal(reservedSize);
            for (var i = 0; i < reservedSize; i++) Marshal.WriteByte(reserved2Buffer, i, 0);
            Marshal.WriteInt32(reserved2Buffer, 0, fdCount);
            Marshal.WriteByte(reserved2Buffer, 4 + 3, CrtFileOpen | CrtFilePipe);
            Marshal.WriteByte(reserved2Buffer, 4 + 4, CrtFileOpen | CrtFilePipe);
            Marshal.WriteIntPtr(reserved2Buffer, 4 + fdCount + 3 * IntPtr.Size, childRead.DangerousGetHandle());
            Marshal.WriteIntPtr(reserved2Buffer, 4 + fdCount + 4 * IntPtr.Size, childWrite.DangerousGetHandle());

            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                cbReserved2 = (ushort)reservedSize,
                lpReserved2 = reserved2Buffer
            };

            var commandLine = BuildCommandLine(edgePath, profileDirectory);

            jobHandle = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
            if (jobHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "创建浏览器作业对象失败。");

            var limits = new JobObjectExtendedLimitInformationNative();
            limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
            if (!NativeMethods.SetInformationJobObject(
                    jobHandle,
                    JobObjectExtendedLimitInformation,
                    ref limits,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformationNative>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "配置浏览器作业清理规则失败。");
            }

            if (!NativeMethods.CreateProcessW(
                    edgePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    bInheritHandles: true,
                    CreateNoWindow | CreateSuspended | CreateNewProcessGroup | CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    Path.GetDirectoryName(edgePath),
                    ref startupInfo,
                    out var processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "启动受限浏览器进程失败。");
            }

            processHandle = processInfo.hProcess;
            threadHandle = processInfo.hThread;

            if (!NativeMethods.AssignProcessToJobObject(jobHandle, processHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "将浏览器纳入作业对象失败。");

            if (NativeMethods.ResumeThread(threadHandle) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "恢复浏览器主线程失败。");

            // 子进程已继承对应句柄，立即释放父端副本并移交管道读写流。
            childRead.Dispose();
            childRead = null;
            childWrite.Dispose();
            childWrite = null;

            var writer = new FileStream(parentWrite, FileAccess.Write, 4096, isAsync: false);
            parentWrite = null;
            var reader = new FileStream(parentRead, FileAccess.Read, 4096, isAsync: false);
            parentRead = null;

            return new EdgePipeProcess(profileDirectory, jobHandle, processHandle, processInfo.dwProcessId, writer, reader);
        }
        catch
        {
            if (processHandle != IntPtr.Zero)
            {
                try { NativeMethods.TerminateProcess(processHandle, 1); } catch { }
                NativeMethods.CloseHandle(processHandle);
            }
            if (jobHandle != IntPtr.Zero) NativeMethods.CloseHandle(jobHandle);
            throw;
        }
        finally
        {
            if (threadHandle != IntPtr.Zero) NativeMethods.CloseHandle(threadHandle);
            if (reserved2Buffer != IntPtr.Zero) Marshal.FreeHGlobal(reserved2Buffer);
            childRead?.Dispose();
            childWrite?.Dispose();
            parentWrite?.Dispose();
            parentRead?.Dispose();
        }
    }

    public async Task<JsonObject> CallAsync(
        string method,
        object? parameters = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        using var reg = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(id, out var waiter))
                waiter.TrySetCanceled(cancellationToken);
        });

        try
        {
            var payload = new JsonObject
            {
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters is null ? new JsonObject() : JsonSerializer.SerializeToNode(parameters)
            };
            if (!string.IsNullOrEmpty(sessionId))
                payload["sessionId"] = sessionId;

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            var frame = new byte[jsonBytes.Length + 1];
            Buffer.BlockCopy(jsonBytes, 0, frame, 0, jsonBytes.Length);
            frame[^1] = 0;

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _pipeWriter.WriteAsync(frame, cancellationToken);
                await _pipeWriter.FlushAsync(cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            return await tcs.Task.WaitAsync(cancellationToken);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[8192];
        using var messageBuffer = new MemoryStream();
        try
        {
            while (!_readerCts.IsCancellationRequested)
            {
                var read = await _pipeReader.ReadAsync(buffer, _readerCts.Token);
                if (read == 0) break;

                var start = 0;
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != 0) continue;
                    if (i > start) messageBuffer.Write(buffer, start, i - start);
                    var frame = messageBuffer.ToArray();
                    messageBuffer.SetLength(0);
                    start = i + 1;
                    if (frame.Length > 0) HandleIncomingMessage(frame);
                }

                if (start < read)
                    messageBuffer.Write(buffer, start, read - start);
            }
        }
        catch (Exception) when (_readerCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            FailAllPending(ex);
            return;
        }

        FailAllPending(new EndOfStreamException("Edge CDP 管道已关闭。"));
    }

    private void HandleIncomingMessage(byte[] frame)
    {
        JsonObject? node;
        try
        {
            node = JsonNode.Parse(frame) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (node is null) return;

        if (node.TryGetPropertyValue("id", out var idNode) && idNode is not null && idNode.GetValue<long>() is var id)
        {
            if (_pending.TryRemove(id, out var tcs))
            {
                if (node.TryGetPropertyValue("error", out var err) && err is JsonObject errObj)
                {
                    var message = errObj["message"]?.GetValue<string>() ?? "CDP 调用失败。";
                    tcs.TrySetException(new InvalidOperationException(message));
                }
                else
                {
                    var result = node["result"] as JsonObject ?? new JsonObject();
                    tcs.TrySetResult(result);
                }
            }
            return;
        }

        if (node.TryGetPropertyValue("method", out var methodNode)
            && methodNode?.GetValue<string>() is { } methodName
            && NotificationReceived is { } handler)
        {
            var paramObj = node["params"] as JsonObject ?? new JsonObject();
            _ = Task.Run(async () =>
            {
                try { await handler(methodName, paramObj); }
                catch { }
            });
        }
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var id in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(id, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    private static string BuildCommandLine(string edgePath, string profileDirectory)
    {
        var args = new[]
        {
            edgePath,
            "--headless=new",
            "--no-first-run",
            "--disable-background-networking",
            "--disable-extensions",
            "--no-default-browser-check",
            "--remote-debugging-pipe",
            "--disable-quic",
            "--disable-gpu",
            "--proxy-server=http://127.0.0.1:1",
            "--proxy-bypass-list=<-loopback>",
            $"--user-data-dir={profileDirectory}",
            "about:blank"
        };
        return string.Join(" ", args.Select(QuoteArg));
    }

    private static string QuoteArg(string arg) =>
        arg.Contains(' ') || arg.Contains('\t') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _readerCts.Cancel();
        try { _pipeWriter.Dispose(); } catch { }
        try { _pipeReader.Dispose(); } catch { }

        if (_jobHandle != IntPtr.Zero) NativeMethods.CloseHandle(_jobHandle);
        if (_processHandle != IntPtr.Zero)
        {
            try
            {
                using var proc = Process.GetProcessById(_processId);
                await proc.WaitForExitAsync(new CancellationTokenSource(1500).Token);
            }
            catch { }
            NativeMethods.CloseHandle(_processHandle);
        }

        try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
        _readerCts.Dispose();
        _writeLock.Dispose();
        FailAllPending(new ObjectDisposedException(nameof(EdgePipeProcess)));
        CleanupProfileDirectory(_profileDirectory);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static void CleanupProfileDirectory(string directory)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(80);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformationNative
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreatePipe(
            out SafeFileHandle hReadPipe,
            out SafeFileHandle hWritePipe,
            ref SecurityAttributes lpPipeAttributes,
            uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetHandleInformation(
            SafeFileHandle hObject,
            uint dwMask,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessW(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref StartupInfo lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(
            IntPtr hJob,
            uint jobObjectInfoClass,
            ref JobObjectExtendedLimitInformationNative lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
    }
}
