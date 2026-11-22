using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetGuardian.Core.Updates;

// 地址、公钥、允许的跳转源均随受信任的客户端构建交付。
internal sealed record UpdateEndpoint(Uri Manifest, Uri PackageRoot, params Uri[] RedirectOrigins);
internal sealed record UpdateTrust(byte[] PublicKey, UpdateEndpoint Primary, UpdateEndpoint Mirror);
internal static class OfficialUpdateFeed
{
    // 维护者核实正式设施后在此装配。空值明确禁用分发，不读取外部配置。
    internal static UpdateTrust? Trust => null;
}

internal sealed record SignedManifest(long Sequence, DateTimeOffset Expires, Version Version, string File,
    long Length, string Hash, string Notes, string Identity)
{
    internal static SignedManifest Verify(byte[] envelope, byte[] trustedKey)
    {
        try
        {
            using var outer = JsonDocument.Parse(envelope);
            var header = Object(outer.RootElement, "payload", "signature");
            var payload = header.GetProperty("payload").GetBytesFromBase64();
            var signature = header.GetProperty("signature").GetBytesFromBase64();
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(trustedKey, out var consumed);
            if (consumed != trustedKey.Length || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                !key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw Security("清单签名无效。");
            using var document = JsonDocument.Parse(payload);
            var item = Object(document.RootElement, "schema", "channel", "sequence", "expiresUtc", "version", "arch", "file", "length", "sha256", "notes");
            string Text(string name) => item.GetProperty(name).GetString() ?? throw Security("清单字段不能为空。");
            var serial = item.GetProperty("sequence").GetInt64();
            var length = item.GetProperty("length").GetInt64();
            var versionText = Text("version");
            var hash = Text("sha256");
            var expires = item.GetProperty("expiresUtc");
            if (item.GetProperty("schema").GetInt32() != 1 || Text("channel") != "stable" || Text("arch") != "win-x64" ||
                serial <= 0 || length is <= 0 or > 1073741824 || !Version.TryParse(versionText, out var version) ||
                version.Build < 0 || version.Revision >= 0 || version.ToString() != versionText ||
                Text("file") != $"NetGuardian-{version}-win-x64-Setup.exe" || !Regex.IsMatch(hash, @"\A[0-9a-fA-F]{64}\z") ||
                !Regex.IsMatch(Text("expiresUtc"), @"(?:Z|\+00:00)\z") || !expires.TryGetDateTimeOffset(out var until))
                throw Security("签名清单的渠道、序号、有效期或安装包字段无效。");
            return new(serial, until, version, Text("file"), length, hash.ToLowerInvariant(), Text("notes"), Convert.ToHexString(SHA256.HashData(payload)));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or CryptographicException or OverflowException)
        { throw new UpdateException(UpdateError.VerificationFailed, "签名清单格式或签名无效。", ex); }
    }

    private static JsonElement Object(JsonElement value, params string[] names)
    {
        var remaining = names.ToHashSet(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!remaining.Remove(field.Name)) throw Security("清单字段重复或未知。");
        if (remaining.Count != 0) throw Security("清单字段不完整。");
        return value;
    }

    internal UpdateRelease At(UpdateEndpoint source) => new(Version, Notes, source.Manifest,
        new(File, new Uri(source.PackageRoot, $"v{Version}/{File}"), Length, "sha256:" + Hash), null);

    internal static UpdateException Security(string message) => new(UpdateError.VerificationFailed, message);
}
