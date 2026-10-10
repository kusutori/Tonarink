namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class SafeFileTargetTests
{
    [TestMethod]
    [DataRow("../secret.txt")]
    [DataRow("folder/../../secret.txt")]
    [DataRow("C:\\secret.txt")]
    [DataRow("/etc/passwd")]
    public void RejectsUnsafePaths(string value)
    {
        var root = TestDirectory.Create();
        try { Assert.ThrowsExactly<LocalSendException>(() => SafeFileTarget.ResolveUnique(root, value)); }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void CreatesUniqueNameForCollision()
    {
        var root = TestDirectory.Create();
        try
        {
            File.WriteAllText(Path.Combine(root, "hello.txt"), "existing");
            Assert.EndsWith("hello (1).txt", SafeFileTarget.ResolveUnique(root, "hello.txt"), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void ReservesUniqueNamesBeforeFilesExist()
    {
        var root = TestDirectory.Create();
        try
        {
            var reserved = new HashSet<string>();
            var first = SafeFileTarget.ResolveUnique(root, "hello.txt", reserved);
            var second = SafeFileTarget.ResolveUnique(root, "hello.txt", reserved);
            Assert.AreNotEqual(first, second);
            Assert.EndsWith("hello (1).txt", second, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void RejectsLinkedDirectoryThatEscapesRoot()
    {
        var root = TestDirectory.Create();
        var outside = TestDirectory.Create();
        try
        {
            var link = Path.Combine(root, "link");
            try { Directory.CreateSymbolicLink(link, outside); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                Assert.Inconclusive($"Symbolic link creation is unavailable: {exception.Message}");
            }
            Assert.ThrowsExactly<LocalSendException>(() => SafeFileTarget.ResolveUnique(root, "link/escaped.txt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }
}
