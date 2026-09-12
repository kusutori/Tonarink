using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;

namespace LocalSendDotNet;

internal sealed class DeviceIdentity : IDisposable
{
    public DeviceIdentity(X509Certificate2 certificate)
    {
        Certificate = certificate;
        Fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }
    public void Dispose() => Certificate.Dispose();
}

internal static class DeviceIdentityStore
{
    private const string CertificateFile = "identity-certificate.pem";
    private const string PrivateKeyFile = "identity-private-key.pem";
    private const string LockFile = ".identity.lock";

    public static async Task<DeviceIdentity> LoadOrCreateAsync(string dataDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dataDirectory);
        var certificatePath = Path.Combine(dataDirectory, CertificateFile);
        var keyPath = Path.Combine(dataDirectory, PrivateKeyFile);
        await using var identityLock = await AcquireLockAsync(Path.Combine(dataDirectory, LockFile), cancellationToken).ConfigureAwait(false);

        var certificateExists = File.Exists(certificatePath);
        var keyExists = File.Exists(keyPath);
        if (certificateExists != keyExists)
            throw new IdentityLoadException($"The LocalSend identity in '{dataDirectory}' is incomplete. Restore or remove both identity PEM files.");

        if (certificateExists)
        {
            try
            {
                var certificatePem = await File.ReadAllTextAsync(certificatePath, cancellationToken).ConfigureAwait(false);
                var keyPem = await File.ReadAllTextAsync(keyPath, cancellationToken).ConfigureAwait(false);
                using var loaded = X509Certificate2.CreateFromPem(certificatePem, keyPem);
                if (!loaded.HasPrivateKey)
                    throw new CryptographicException("The certificate does not have its matching private key.");
                return new DeviceIdentity(X509CertificateLoader.LoadPkcs12(loaded.Export(X509ContentType.Pkcs12), null));
            }
            catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
            {
                throw new IdentityLoadException($"The LocalSend identity in '{dataDirectory}' could not be loaded.", exception);
            }
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=LocalSend User", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));

        var suffix = Guid.NewGuid().ToString("N");
        var temporaryCertificate = certificatePath + ".tmp-" + suffix;
        var temporaryKey = keyPath + ".tmp-" + suffix;
        try
        {
            await File.WriteAllTextAsync(temporaryCertificate, generated.ExportCertificatePem(), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(temporaryKey, rsa.ExportPkcs8PrivateKeyPem(), cancellationToken).ConfigureAwait(false);
            RestrictPermissions(temporaryCertificate, privateKey: false);
            RestrictPermissions(temporaryKey, privateKey: true);
            File.Move(temporaryCertificate, certificatePath);
            File.Move(temporaryKey, keyPath);
        }
        finally
        {
            TryDelete(temporaryCertificate);
            TryDelete(temporaryKey);
        }

        return new DeviceIdentity(X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null));
    }

    public static bool ValidatePeerCertificate(X509Certificate2 certificate, string? expectedFingerprint)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < certificate.NotBefore || now > certificate.NotAfter || !StringComparer.Ordinal.Equals(certificate.Subject, certificate.Issuer))
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(certificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (!chain.Build(certificate))
            return false;

        var actual = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        return expectedFingerprint is null || StringComparer.OrdinalIgnoreCase.Equals(actual, expectedFingerprint);
    }

    public static string Fingerprint(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));

    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous); }
            catch (IOException) { await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false); }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not delete temporary identity file '{path}': {exception.Message}");
        }
    }

    private static void RestrictPermissions(string path, bool privateKey)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(path, privateKey
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite
            : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
    }
}
