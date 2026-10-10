using System.Security.Cryptography.X509Certificates;

namespace LocalSendDotNet.Core.Tests;

[TestClass]
public sealed class IdentityTests
{
    [TestMethod]
    public async Task IdentityIsStableAcrossReloads()
    {
        var directory = TestDirectory.Create();
        try
        {
            string firstFingerprint;
            await using (var first = new AsyncDisposableIdentity(await DeviceIdentityStore.LoadOrCreateAsync(directory, default)))
                firstFingerprint = first.Identity.Fingerprint;
            await using var second = new AsyncDisposableIdentity(await DeviceIdentityStore.LoadOrCreateAsync(directory, default));
            Assert.AreEqual(firstFingerprint, second.Identity.Fingerprint);
            Assert.AreEqual(64, second.Identity.Fingerprint.Length);
            Assert.IsTrue(second.Identity.Certificate.HasPrivateKey);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task PeerFingerprintMustMatch()
    {
        var directory = TestDirectory.Create();
        try
        {
            await using var wrapper = new AsyncDisposableIdentity(await DeviceIdentityStore.LoadOrCreateAsync(directory, default));
            Assert.IsTrue(DeviceIdentityStore.ValidatePeerCertificate(wrapper.Identity.Certificate, wrapper.Identity.Fingerprint));
            Assert.IsFalse(DeviceIdentityStore.ValidatePeerCertificate(wrapper.Identity.Certificate, new string('0', 64)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task IncompleteIdentityProducesActionableException()
    {
        var directory = TestDirectory.Create();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "identity-certificate.pem"), "incomplete");
            var exception = await Assert.ThrowsExactlyAsync<IdentityLoadException>(() => DeviceIdentityStore.LoadOrCreateAsync(directory, default));
            Assert.Contains("incomplete", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task CorruptIdentityProducesActionableException()
    {
        var directory = TestDirectory.Create();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "identity-certificate.pem"), "not a certificate");
            await File.WriteAllTextAsync(Path.Combine(directory, "identity-private-key.pem"), "not a key");
            await Assert.ThrowsExactlyAsync<IdentityLoadException>(() => DeviceIdentityStore.LoadOrCreateAsync(directory, default));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task ConcurrentFirstLoadPublishesOneStableIdentity()
    {
        var directory = TestDirectory.Create();
        try
        {
            var identities = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => DeviceIdentityStore.LoadOrCreateAsync(directory, default)));
            try { Assert.ContainsSingle(identities.Select(static identity => identity.Fingerprint).Distinct(StringComparer.Ordinal)); }
            finally { foreach (var identity in identities) identity.Dispose(); }
            Assert.IsEmpty(Directory.EnumerateFiles(directory, "*.tmp-*"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class AsyncDisposableIdentity(DeviceIdentity identity) : IAsyncDisposable
    {
        public DeviceIdentity Identity => identity;
        public ValueTask DisposeAsync() { identity.Dispose(); return ValueTask.CompletedTask; }
    }
}

internal static class TestDirectory
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "LocalSendDotNet.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
