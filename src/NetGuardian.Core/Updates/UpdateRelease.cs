namespace NetGuardian.Core.Updates;

public sealed record UpdateAsset(string Name, Uri Url, long Size, string? Digest);
public sealed record UpdateRelease(Version Version, string Notes, Uri ReleaseUrl, UpdateAsset Installer, UpdateAsset Checksum);
public sealed record DownloadedUpdate(Version Version, string InstallerPath, string Sha256);
public sealed record UpdateProgress(long Received, long Total)
{
    public double Percentage => Total > 0 ? Math.Clamp(100d * Received / Total, 0, 100) : 0;
}

public enum UpdateError { Network, RateLimited, InvalidRelease, VerificationFailed }

public sealed class UpdateException(UpdateError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public UpdateError Error { get; } = error;
}

public interface IUpdateService
{
    Task<UpdateRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken);
    Task<DownloadedUpdate> DownloadAsync(UpdateRelease release, string directory,
        IProgress<UpdateProgress>? progress, CancellationToken cancellationToken);
}
