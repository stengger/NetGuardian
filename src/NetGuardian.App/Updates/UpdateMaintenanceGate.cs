using System;
using System.Threading;

namespace NetGuardian.App.Updates;

// 与独立协调脚本共用；持有期间不允许新的界面开始读取或操作安装文件。
internal sealed class UpdateMaintenanceGate : IDisposable
{
    private readonly Mutex _mutex;
    private UpdateMaintenanceGate(Mutex mutex) => _mutex = mutex;

    public static UpdateMaintenanceGate? TryEnter(string name = @"Global\NetGuardian.Update")
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, name);
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return new UpdateMaintenanceGate(mutex);
            mutex.Dispose();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            mutex?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
