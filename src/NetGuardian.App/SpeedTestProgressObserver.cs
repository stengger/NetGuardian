using System;
using System.Windows.Threading;
using NetGuardian.Core.Network;

namespace NetGuardian.App;

/// <summary>在应用终态前处理阶段结果，避免异步投递的完成事件被失败状态丢弃。</summary>
internal sealed class SpeedTestProgressObserver(Dispatcher dispatcher, Action<NetworkSpeedTestProgress> update)
    : IProgress<NetworkSpeedTestProgress>
{
    public void Report(NetworkSpeedTestProgress value)
    {
        if (dispatcher.CheckAccess()) update(value);
        else if (!dispatcher.HasShutdownStarted)
            dispatcher.Invoke(() => update(value));
    }
}
