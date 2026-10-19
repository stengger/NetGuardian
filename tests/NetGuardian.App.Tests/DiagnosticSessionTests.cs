using NetGuardian.App.Presentation;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class DiagnosticSessionTests
{
    [Fact]
    public async Task 防止重复运行且取消后不接收迟到成功()
    {
        var state = new DiagnosticSession<string>();
        var completion = new TaskCompletionSource<string>();
        var calls = 0;
        var running = state.RunAsync(_ => { calls++; return completion.Task; }, () => { });
        await state.RunAsync(_ => { calls++; return Task.FromResult("重复"); }, () => { });
        state.Cancel();
        completion.SetResult("迟到");
        await running;
        Assert.Equal(1, calls);
        Assert.Equal(DiagnosticPhase.Cancelled, state.Phase);
        Assert.Null(state.Result);
        Assert.False(state.IsBusy);
    }

    [Fact]
    public async Task 重试清除旧结果和错误且取消异常不误报失败()
    {
        var state = new DiagnosticSession<string>();
        await state.RunAsync(_ => Task.FromResult("旧结果"), () => { });
        await state.RunAsync(_ => throw new InvalidOperationException("失败"), () =>
        {
            if (state.IsBusy) Assert.Null(state.Result);
        });
        Assert.Equal("失败", state.Error);
        await state.RunAsync(token => { state.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(""); }, () => { });
        Assert.Equal(DiagnosticPhase.Cancelled, state.Phase);
        Assert.Null(state.Error);
        await state.RunAsync(_ => Task.FromResult("新结果"), () => { });
        Assert.Equal("新结果", state.Result);
        Assert.Equal(DiagnosticPhase.Complete, state.Phase);
    }
}
