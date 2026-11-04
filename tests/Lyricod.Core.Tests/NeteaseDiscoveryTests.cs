namespace Lyricod.Core.Tests;

public class NeteaseDiscoveryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TriesRegistrationsThenTheRunningProcessUntilOneIsReadable(int valid)
    {
        var tried = new List<int>();
        var candidates = Enumerable.Range(0, 4).Select<int, Func<string?>>(i => () =>
        {
            tried.Add(i);
            return i.ToString();
        });
        var found = NeteaseSync.FindInstallation(candidates, folder =>
        {
            int i = int.Parse(folder);
            if (i == valid) return folder;
            if (i % 2 == 0) return null; // missing executable
            throw new IOException("unreadable executable");
        });
        Assert.Equal(valid.ToString(), found);
        Assert.Equal(Enumerable.Range(0, valid + 1), tried);
    }

    [Fact]
    public void AnUnreadableRegistryKeyDoesNotHideTheNextInstallation() =>
        Assert.Equal("user", NeteaseSync.FindInstallation<string>(
            [() => throw new UnauthorizedAccessException(), () => null, () => "user"], folder => folder));

    [Fact]
    public void NoReadableCandidateReturnsNull() =>
        Assert.Null(NeteaseSync.FindInstallation<string>([() => "old", () => null], _ => null));
}
