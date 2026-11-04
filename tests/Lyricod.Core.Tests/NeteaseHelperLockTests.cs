using System.Security.Cryptography;

namespace Lyricod.Core.Tests;

public class NeteaseHelperLockTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task VerificationTransfersOnlyTheMatchingOpenFile(bool wrongSize, bool wrongHash)
    {
        string folder = Directory.CreateTempSubdirectory("Lyricod-helper-lock-test-").FullName;
        string path = Path.Combine(folder, "helper.exe");
        byte[] bytes = [1, 2, 3, 4];
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            string hash = Convert.ToHexString(SHA256.HashData(wrongHash ? [4, 3, 2, 1] : bytes));
            using (var verified = await NeteaseSync.OpenVerifiedHelperAsync(path, wrongSize ? 5 : 4, hash))
            {
                if (wrongSize || wrongHash) Assert.Null(verified);
                else
                {
                    Assert.NotNull(verified);
                    Assert.True(verified.CanRead);
                    Assert.False(verified.CanWrite);
                    Assert.Equal(Path.GetFullPath(path), verified.Name);
                    using var reader = File.OpenRead(path);
                    Assert.Equal(bytes.Length, reader.Length);
                    if (OperatingSystem.IsWindows())
                    {
                        Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
                        Assert.Throws<IOException>(() => File.Delete(path));
                        Assert.Throws<IOException>(() => File.Move(path, path + ".renamed"));
                        Assert.Throws<IOException>(() => Directory.Move(folder, folder + "-renamed"));
                    }
                }
            }
            // Both rejected files and the caller's released handle allow a new exclusive open.
            using var reopened = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
