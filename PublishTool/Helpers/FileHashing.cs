using System.Security.Cryptography;

namespace PublishTool.Helpers;

public static class FileHashing
{
    private const int BufferSize = 1024 * 1024;

    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
