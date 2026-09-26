using Nmf.Client;

namespace Nmf.Client.Tests;

public class ContentLocatorTests
{
    [Fact]
    public void FindContentRoot_WalksUpToContentCore()
    {
        var root = Directory.CreateTempSubdirectory("nmf-locator-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content", "core"));
            var nested = Directory.CreateDirectory(Path.Combine(root, "src", "Nmf.Game", "bin")).FullName;
            Assert.Equal(Path.Combine(root, "content"), ContentLocator.FindContentRoot(nested));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindContentRoot_NoStartingPlace_ReturnsNull()
    {
        // An exported game has no res:// folder on disk: the path comes back empty.
        Assert.Null(ContentLocator.FindContentRoot(""));
        Assert.Null(ContentLocator.FindContentRoot(Path.Combine(Path.GetTempPath(), "nmf-no-such-dir", "deeper")));
    }

    [Fact]
    public void FindContentRoot_NoContent_ReturnsNull()
    {
        var root = Directory.CreateTempSubdirectory("nmf-locator-").FullName;
        try
        {
            Assert.Null(ContentLocator.FindContentRoot(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
