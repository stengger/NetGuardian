using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetGuardian.App.Updates;

internal sealed class ApplicationExitCoordinator
{
    public bool IsExiting { get; private set; }

    public static async Task WaitForIdleAsync(Func<bool> isBusy, Action release, CancellationToken token = default)
    {
        try
        {
            while (isBusy()) await Task.Delay(100, token);
            token.ThrowIfCancellationRequested();
            release();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 更新退出被撤销后仍需安全释放控制权；此收尾没有关窗回调。
            _ = WaitForIdleAsync(isBusy, release);
            throw;
        }
    }

    public async Task ExitAsync(Func<CancellationToken, Task> prepare, Action close, CancellationToken token)
    {
        if (IsExiting) throw new InvalidOperationException("界面正在退出，请等待收尾完成。");
        IsExiting = true;
        try
        {
            token.ThrowIfCancellationRequested();
            await prepare(token).WaitAsync(token);
            // 取消只结束本次退出等待，绝不能让迟到的收尾任务再关闭窗口。
            token.ThrowIfCancellationRequested();
            close();
        }
        finally { IsExiting = false; }
    }
}
