using System.IO.Compression;
using System.Text;

namespace MarketMakerEtl.Core.Data;

internal static class GzipJson
{
    public static byte[]? Compress(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    public static string? Decompress(byte[]? gzipBytes)
    {
        if (gzipBytes is null || gzipBytes.Length == 0)
        {
            return null;
        }

        using var input = new MemoryStream(gzipBytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
