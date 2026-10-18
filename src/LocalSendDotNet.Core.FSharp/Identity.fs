namespace LocalSendDotNet.FSharp

open LocalSendDotNet.Resources

open System
open System.IO
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Threading
open System.Threading.Tasks

exception IdentityLoadException of string * exn option
exception PeerIdentityException of string

type internal DeviceIdentity(certificate: X509Certificate2) =
    member _.Certificate = certificate
    member _.Fingerprint = SHA256.HashData certificate.RawData |> Convert.ToHexString |> Fingerprint.ofCertificate
    interface IDisposable with member _.Dispose() = certificate.Dispose()

[<RequireQualifiedAccess>]
module internal IdentityStore =
    let fingerprint (certificate: X509Certificate2) =
        SHA256.HashData certificate.RawData |> Convert.ToHexString |> Fingerprint.ofCertificate

    let validatePeer expected (certificate: X509Certificate2) =
        let now = DateTime.UtcNow
        if now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime()
           || not (String.Equals(certificate.Subject, certificate.Issuer, StringComparison.Ordinal)) then false
        else
            use chain = new X509Chain()
            chain.ChainPolicy.TrustMode <- X509ChainTrustMode.CustomRootTrust
            chain.ChainPolicy.CustomTrustStore.Add certificate |> ignore
            chain.ChainPolicy.RevocationMode <- X509RevocationMode.NoCheck
            chain.Build certificate && (expected |> Option.forall ((=) (fingerprint certificate)))

    let private acquireLock path (ct: CancellationToken) = task {
        let mutable acquired = None
        while acquired.IsNone do
            ct.ThrowIfCancellationRequested()
            try
                acquired <- Some(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous))
            with :? IOException -> do! Task.Delay(50, ct)
        return acquired.Value
    }

    let private restrict privateKey (path: string) =
        if not (OperatingSystem.IsWindows()) then
            let mode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite
            File.SetUnixFileMode(path, if privateKey then mode else mode ||| UnixFileMode.GroupRead)

    let private cleanup path =
        try File.Delete path
        with
        | :? IOException as error -> Diagnostics.Debug.WriteLine error.Message
        | :? UnauthorizedAccessException as error -> Diagnostics.Debug.WriteLine error.Message

    let loadOrCreate directory (ct: CancellationToken) = task {
        Directory.CreateDirectory directory |> ignore
        let certificatePath = Path.Combine(directory, "identity-certificate.pem")
        let keyPath = Path.Combine(directory, "identity-private-key.pem")
        use! identityLock = acquireLock (Path.Combine(directory, ".identity.lock")) ct
        let exists = File.Exists certificatePath
        if exists <> File.Exists keyPath then
            raise (IdentityLoadException((CoreText.IdentityIncomplete(directory)), None))
        if exists then
            try
                let! certificatePem = File.ReadAllTextAsync(certificatePath, ct)
                let! keyPem = File.ReadAllTextAsync(keyPath, ct)
                use loaded = X509Certificate2.CreateFromPem(certificatePem, keyPem)
                if not loaded.HasPrivateKey then raise (CryptographicException (CoreText.IdentityPrivateKeyMissing))
                return new DeviceIdentity(X509CertificateLoader.LoadPkcs12(loaded.Export X509ContentType.Pkcs12, null))
            with
            | :? CryptographicException as error -> return raise (IdentityLoadException((CoreText.IdentityLoadGeneric), Some error))
            | :? IOException as error -> return raise (IdentityLoadException((CoreText.IdentityReadFailed), Some error))
            | :? UnauthorizedAccessException as error -> return raise (IdentityLoadException((CoreText.IdentityAccessDenied), Some error))
        else
            use rsa = RSA.Create 2048
            let request = CertificateRequest("CN=LocalSend User", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            request.CertificateExtensions.Add(X509BasicConstraintsExtension(false, false, 0, true))
            request.CertificateExtensions.Add(X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature ||| X509KeyUsageFlags.KeyEncipherment, true))
            request.CertificateExtensions.Add(X509SubjectKeyIdentifierExtension(request.PublicKey, false))
            use generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20))
            let suffix = ".tmp-" + Guid.NewGuid().ToString("N")
            let tempCertificate, tempKey = certificatePath + suffix, keyPath + suffix
            try
                do! File.WriteAllTextAsync(tempCertificate, generated.ExportCertificatePem(), ct)
                do! File.WriteAllTextAsync(tempKey, rsa.ExportPkcs8PrivateKeyPem(), ct)
                restrict false tempCertificate
                restrict true tempKey
                File.Move(tempCertificate, certificatePath)
                File.Move(tempKey, keyPath)
            finally
                cleanup tempCertificate
                cleanup tempKey
            return new DeviceIdentity(X509CertificateLoader.LoadPkcs12(generated.Export X509ContentType.Pkcs12, null))
    }
