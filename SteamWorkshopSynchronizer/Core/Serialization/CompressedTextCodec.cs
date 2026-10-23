using System.IO.Compression;
using System.IO;
using System.Text;

namespace SteamWorkshopSynchronizer.Core.Serialization;

public sealed class CompressedTextCodec
{
    public const string ManifestPrefix = "SWS1:";
    public const int MaxEncodedCharacters = 2 * 1024 * 1024;
    public const int MaxDecodedCharacters = ManifestSerializer.MaxJsonCharacters;

    public string EncodeManifest(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var bytes = Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return ManifestPrefix + ToBase64Url(output.ToArray());
    }

    public string DecodeManifest(string encoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);
        if (encoded.Length > MaxEncodedCharacters)
        {
            throw new InvalidDataException($"压缩文本超过 {MaxEncodedCharacters:N0} 个字符的大小限制");
        }

        if (!encoded.StartsWith(ManifestPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"压缩文本必须以 {ManifestPrefix} 开头");
        }

        byte[] compressed;
        try
        {
            compressed = FromBase64Url(encoded[ManifestPrefix.Length..]);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("压缩文本的 Base64URL 内容无效", exception);
        }

        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[8192];
        var builder = new StringBuilder();
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
            if (builder.Length > MaxDecodedCharacters)
            {
                throw new InvalidDataException($"解压后的清单超过 {MaxDecodedCharacters:N0} 个字符的大小限制");
            }
        }

        return builder.ToString();
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var standard = value.Replace('-', '+').Replace('_', '/');
        standard = standard.PadRight(standard.Length + ((4 - standard.Length % 4) % 4), '=');
        return Convert.FromBase64String(standard);
    }
}
