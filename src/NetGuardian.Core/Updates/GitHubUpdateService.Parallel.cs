using System.Net;
using System.Runtime.ExceptionServices;

namespace NetGuardian.Core.Updates;

public sealed partial class GitHubUpdateService
{
    private const int RangeCount = 4;
    private const long ParallelThreshold = 8L * 1024 * 1024;

    private sealed class RangeUnsupportedException : Exception { }

    private async Task DownloadRangesAsync(UpdateAsset asset, FileStream prefix, RangeCache ranges, int first,
        HttpResponseMessage probe, IProgress<UpdateProgress>? progress, CancellationToken token)
    {
        using var peers = CancellationTokenSource.CreateLinkedTokenSource(token);
        var counter = new TransferProgress(progress, asset.Size, ranges.SavedBytes(prefix.Length));
        Exception? stopReason = null;

        async Task TransferAsync(int index)
        {
            try
            {
                peers.Token.ThrowIfCancellationRequested();
                long end = ranges.Start(index + 1) - 1;
                if (index == first)
                {
                    await AppendBodyAsync(probe, prefix, end + 1 - prefix.Length, counter, peers.Token).ConfigureAwait(false);
                    return;
                }
                await using var file = OpenPartial(ranges.Tail(index));
                long start = ranges.Start(index);
                if (file.Length > end + 1 - start) file.SetLength(0);
                long offset = start + file.Length;
                if (offset == end + 1) return;
                using var response = await SendAsync(asset.Url, peers.Token, offset, end).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable)
                    throw new RangeUnsupportedException();
                CheckRange(response, offset, end, asset.Size);
                await AppendBodyAsync(response, file, end + 1 - offset, counter, peers.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not HttpRequestException network || !CanRetry(network))
            {
                // 暂时断流时让其余分段写完。限流、取消或无效响应则停止整组请求，并保留最初的原因。
                if (Interlocked.CompareExchange(ref stopReason, ex, null) is null) await peers.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }

        try { await Task.WhenAll(Enumerable.Range(first, RangeCount - first).Select(TransferAsync)).ConfigureAwait(false); }
        catch when (stopReason is not null)
        {
            token.ThrowIfCancellationRequested();
            ExceptionDispatchInfo.Capture(stopReason).Throw();
        }
    }

    // 主分片始终保存从零开始的连续数据，兼容旧版缓存。其余三个文件绑定同一摘要，各自从固定边界开始。
    private sealed class RangeCache(GitHubUpdateService service, FileInfo prefix, long size)
    {
        public bool Enabled => size >= ParallelThreshold;
        public long Start(int index) => size * index / RangeCount;
        public int SegmentAt(long offset)
        {
            int index = 0;
            while (index + 1 < RangeCount && offset >= Start(index + 1)) index++;
            return index;
        }
        public FileInfo Tail(int index) => new(prefix.FullName + "." + index);

        private long SavedTail(FileInfo file, int index)
        {
            if (!service.InspectFile(file)) return 0;
            long length = file.Length;
            return length <= Start(index + 1) - Start(index) ? length : 0;
        }

        public long SavedBytes(long prefixLength)
        {
            long saved = prefixLength;
            if (Enabled)
            {
                for (int i = 1; i < RangeCount; i++)
                {
                    long start = Start(i);
                    long end = start + SavedTail(Tail(i), i);
                    saved += Math.Max(0, end - Math.Max(start, prefixLength));
                }
            }
            return saved;
        }

        public async Task MergeAvailableAsync(FileStream output, CancellationToken token)
        {
            if (!Enabled) return;
            output.Position = output.Length;
            for (int i = 1; i < RangeCount && Start(i) <= output.Length; i++)
            {
                var file = Tail(i);
                long overlap = output.Length - Start(i);
                if (SavedTail(file, i) <= overlap) continue;
                // 合并中断后，从主分片已经覆盖的位置继续。直到整包校验通过才删除尾部分片。
                await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                input.Position = overlap;
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }
        }

        public void DeleteTails()
        {
            if (Enabled)
                for (int i = 1; i < RangeCount; i++) Tail(i).Delete();
        }
    }
}
