using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetGuardian.App.Updates;

internal static class UpdateExitCoordinator
{
    public static async Task WaitAsync(Func<CancellationToken, Task> exit,
        Func<CancellationToken, Task> verifyJob, CancellationToken token, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var exitToken = cancellation.Token;
        Task? pending = null;
        try
        {
            await verifyJob(exitToken).WaitAsync(exitToken);
            pending = exit(exitToken);
            while (!pending.IsCompleted)
            {
                exitToken.ThrowIfCancellationRequested();
                await verifyJob(exitToken).WaitAsync(exitToken);
                await Task.WhenAny(pending, Task.Delay(100, exitToken));
            }
            await pending;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException("等待界面退出超时，已撤销本次安装。请等待网络操作收尾后重试，或正常退出后手动运行安装包。");
        }
        finally
        {
            cancellation.Cancel();
            // 无论失败来自脚本还是时限，都先结束可取消的退出任务再恢复界面。
            if (pending is not null)
            {
                try { await pending.WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (Exception) { /* 原始退出或脚本错误由上面的等待传播。 */ }
            }
        }
    }
}
