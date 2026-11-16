using System.Text;

namespace NetGuardian.Core.Wifi;

internal static class NetshOutputDecoder
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string Decode(byte[] bytes, Encoding fallback)
    {
        try { return StrictUtf8.GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { }

        // 中文 netsh 字段使用系统代码页，而 SSID 可能直接输出 UTF-8 字节。
        // 只分开解码 SSID 字段，其他文本（包括配置名和密钥）沿用系统代码页。
        var output = new StringBuilder();
        var start = 0;
        while (start < bytes.Length)
        {
            var newline = Array.IndexOf(bytes, (byte)'\n', start);
            var end = newline < 0 ? bytes.Length : newline + 1;
            var colon = Array.IndexOf(bytes, (byte)':', start, end - start);
            var key = colon < 0 ? "" : fallback.GetString(bytes, start, colon - start).Trim();
            if (IsSsidField(key))
            {
                output.Append(fallback.GetString(bytes, start, colon + 1 - start));
                output.Append(DecodeSsidValue(bytes, colon + 1, end, fallback, IsProfileSsidField(key)));
            }
            else output.Append(fallback.GetString(bytes, start, end - start));
            start = end;
        }
        return output.ToString();
    }

    private static string DecodeSsidValue(byte[] bytes, int start, int end, Encoding fallback, bool profileField)
    {
        var contentStart = start < end && bytes[start] == (byte)' ' ? start + 1 : start;
        var contentEnd = end;
        while (contentEnd > contentStart && bytes[contentEnd - 1] is (byte)'\r' or (byte)'\n') contentEnd--;
        var opening = fallback.GetBytes("“");
        var closing = fallback.GetBytes("”");
        var content = bytes.AsSpan(contentStart, contentEnd - contentStart);
        // 中文配置详情的引号也由系统代码页输出，只有引号内的 SSID 才是 UTF-8。
        if (profileField && content.Length >= opening.Length + closing.Length
            && content.StartsWith(opening) && content.EndsWith(closing))
        {
            var innerStart = contentStart + opening.Length;
            var innerEnd = contentEnd - closing.Length;
            return fallback.GetString(bytes, start, innerStart - start)
                + DecodeBytes(bytes, innerStart, innerEnd - innerStart, fallback)
                + fallback.GetString(bytes, innerEnd, end - innerEnd);
        }
        return DecodeBytes(bytes, start, end - start, fallback);
    }

    private static string DecodeBytes(byte[] bytes, int start, int length, Encoding fallback)
    {
        try { return StrictUtf8.GetString(bytes, start, length); }
        catch (DecoderFallbackException) { return fallback.GetString(bytes, start, length); }
    }

    private static bool IsProfileSsidField(string key) => key.Equals("SSID name", StringComparison.OrdinalIgnoreCase)
        || key == "SSID 名称";

    private static bool IsSsidField(string key) => key.Equals("SSID", StringComparison.OrdinalIgnoreCase)
        || key.Equals("SSID name", StringComparison.OrdinalIgnoreCase) || key == "SSID 名称"
        || (key.StartsWith("SSID ", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(key.AsSpan(5), out _));
}
