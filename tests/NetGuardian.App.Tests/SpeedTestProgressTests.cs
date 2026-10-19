using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using NetGuardian.Core.Network;
using Xunit;

namespace NetGuardian.App.Tests;

public sealed class SpeedTestProgressTests
{
    [Fact]
    public async Task 同步上传失败不能抢先吞掉已经完成的下载结果()
    {
        var previous = SynchronizationContext.Current;
        var queued = new QueueContext();
        SynchronizationContext.SetSynchronizationContext(queued);
        try
        {
            var session = new SpeedTestSession();
            var id = session.Begin();
            var updates = 0;
            var progress = new SpeedTestProgressObserver(Dispatcher.CurrentDispatcher, value =>
            {
                session.Apply(id, value);
                updates++;
            });
            using var client = new HttpClient(new FailingUploadHandler());
            try
            {
                await new NetworkSpeedTestService(client).RunAsync(progress);
                Assert.Fail("上传应该失败。");
            }
            catch (HttpRequestException exception)
            {
                session.Fail(id, exception.Message);
            }
            Assert.Equal(SpeedTestStatus.Partial, session.Status);
            Assert.Equal(SpeedMetricState.Complete, session.Download.State);
            Assert.True(session.Download.Value > 0);
            Assert.Equal(SpeedMetricState.Complete, session.Latency.State);
            Assert.Equal(SpeedMetricState.Failed, session.Upload.State);
            Assert.True(updates >= 5);
            Assert.Empty(queued.Callbacks);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class QueueContext : SynchronizationContext
    {
        public Queue<(SendOrPostCallback Callback, object? State)> Callbacks { get; } = new();
        public override void Post(SendOrPostCallback callback, object? state) => Callbacks.Enqueue((callback, state));
    }

    private sealed class FailingUploadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post) throw new HttpRequestException("测试用上传失败");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(request.Method == HttpMethod.Get ? new byte[5_000_000] : [])
            });
        }
    }
}
