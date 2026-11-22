using System.Text.Json;

namespace NetGuardian.Core.Updates;

internal sealed record AcceptedRelease(long Sequence, string Identity, Version Version, string PackageHash, long Length, DateTimeOffset Checked, UpdateRoute Route, UpdateRoute? DownloadRoute = null);

// 同一把跨进程文件锁保护读、比较和原子替换。调用方保证根目录和新文件的 ACL。
internal sealed class UpdateSequenceStore(Func<string> prepareDirectory, Action<FileInfo>? validate = null,
    Func<FileInfo, FileStream>? create = null)
{
    private FileStream Create(FileInfo file) => create?.Invoke(file) ?? new(file.FullName, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

    private T Locked<T>(Func<FileInfo, AcceptedRelease?, T> action)
    {
        var root = prepareDirectory();
        var gate = new FileInfo(Path.Combine(root, "signed-release.lock"));
        FileStream handle;
        if (gate.Exists)
        {
            validate?.Invoke(gate);
            handle = new(gate.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        else handle = Create(gate);
        using (handle)
        {
            var state = new FileInfo(Path.Combine(root, "signed-release.json"));
            AcceptedRelease? previous = null;
            if (state.Exists)
            {
                validate?.Invoke(state);
                if (state.Length is 0 or > 16384) throw SignedManifest.Security("本机发行序号记录损坏。");
                using var input = state.OpenRead();
                try { previous = JsonSerializer.Deserialize<AcceptedRelease>(input); }
                catch (JsonException) { throw SignedManifest.Security("本机发行序号记录无法识别。"); }
                if (previous is not { Sequence: > 0, Identity.Length: 64, PackageHash.Length: 64, Length: > 0, Version: not null } || previous.Checked == default)
                    throw SignedManifest.Security("本机发行序号记录无效。");
            }
            return action(state, previous);
        }
    }

    internal AcceptedRelease? Read() => Locked((_, previous) => previous);

    internal AcceptedRelease Verify(SignedManifest manifest, DateTimeOffset now, UpdateRoute route, bool accept, bool downloaded = false)
    {
        return Locked((file, previous) =>
        {
            if (previous is not null && (manifest.Sequence < previous.Sequence || manifest.Version < previous.Version ||
                manifest.Sequence == previous.Sequence && manifest.Identity != previous.Identity))
                throw SignedManifest.Security("发行序号回退、版本降级或同一序号的清单已被替换。");
            if (previous?.Version == manifest.Version && (previous.PackageHash != manifest.Hash || previous.Length != manifest.Length))
                throw SignedManifest.Security("同一版本的安装包长度或摘要已被替换。");
            if (manifest.Expires <= now || previous is not null && manifest.Expires <= previous.Checked)
                throw new UpdateException(UpdateError.Expired, "稳定版清单已过期，请重新检查更新。");
            if (!accept && previous?.Identity != manifest.Identity) throw SignedManifest.Security("本机发行记录已变化，请重新检查更新。");
            if (!accept && !downloaded) return previous!;
            var next = downloaded ? previous! with { DownloadRoute = route } : new AcceptedRelease(manifest.Sequence,
                manifest.Identity, manifest.Version, manifest.Hash, manifest.Length,
                previous is not null && previous.Checked > now ? previous.Checked : now, route,
                previous?.Identity == manifest.Identity ? previous.DownloadRoute : null);
            var temporary = new FileInfo(file.FullName + "." + Guid.NewGuid().ToString("N"));
            try
            {
                using (var output = Create(temporary))
                {
                    JsonSerializer.Serialize(output, next);
                    output.Flush(true);
                }
                File.Move(temporary.FullName, file.FullName, true);
            }
            finally { temporary.Delete(); }
            return next;
        });
    }
}
