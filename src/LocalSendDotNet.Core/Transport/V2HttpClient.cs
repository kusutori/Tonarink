using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using LocalSendDotNet.Protocol.V2;

namespace LocalSendDotNet;

internal sealed class V2HttpClient(DeviceIdentity identity, LocalSendOptions options)
{
    public async Task<(RegisterResponseDto Info, string Fingerprint, bool Verified)> ProbeAsync(DeviceEndpoint endpoint, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        string? certificateFingerprint = null;
        using var handler = new HttpClientHandler();
        if (endpoint.Protocol == LocalSendProtocol.Https)
        {
            handler.ClientCertificates.Add(identity.Certificate);
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                    throw new PeerIdentityException(CoreText.PeerCertificateMissing);
                using var converted = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
                if (!DeviceIdentityStore.ValidatePeerCertificate(converted, null))
                    throw new PeerIdentityException(CoreText.PeerIdentityInvalid);
                certificateFingerprint = DeviceIdentityStore.Fingerprint(converted);
                return true;
            };
        }
        using var client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri($"{(endpoint.Protocol == LocalSendProtocol.Https ? "https" : "http")}://{FormatHost(endpoint.Address)}:{endpoint.Port}"),
            Timeout = timeout ?? options.RequestTimeout
        };
        RegisterResponseDto info;
        try
        {
            info = await client.GetFromJsonAsync(V2Constants.BasePath + "/info", V2JsonContext.Default.RegisterResponseDto, cancellationToken).ConfigureAwait(false)
                ?? throw new LocalSendException(CoreText.InfoResponseEmpty);
        }
        catch (HttpRequestException exception) when (ContainsPeerIdentityException(exception))
        {
            throw new PeerIdentityException(CoreText.PeerIdentityInvalid, exception);
        }
        var fingerprint = certificateFingerprint ?? info.Fingerprint;
        if (string.IsNullOrWhiteSpace(fingerprint))
            throw new PeerIdentityException(CoreText.PeerFingerprintMissing);
        if (certificateFingerprint is not null && !string.IsNullOrEmpty(info.Fingerprint) &&
            !StringComparer.OrdinalIgnoreCase.Equals(certificateFingerprint, info.Fingerprint))
            throw new PeerIdentityException(CoreText.PeerFingerprintMismatch);
        return (info, fingerprint, certificateFingerprint is not null);
    }

    public async Task<RegisterResponseDto> RegisterAsync(DeviceEndpoint endpoint, string expectedFingerprint, DeviceInfoDto localInfo, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var client = CreateClient(endpoint, expectedFingerprint, timeout);
        using var response = await client.PostAsJsonAsync(V2Constants.BasePath + "/register", localInfo, V2JsonContext.Default.DeviceInfoDto, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(V2JsonContext.Default.RegisterResponseDto, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalSendException(CoreText.RegisterResponseEmpty);
        if (!string.IsNullOrEmpty(result.Fingerprint) && !StringComparer.OrdinalIgnoreCase.Equals(result.Fingerprint, expectedFingerprint))
            throw new PeerIdentityException(CoreText.PeerRegisterFingerprintMismatch);
        return result;
    }

    public async Task<PrepareUploadResponseDto?> PrepareUploadAsync(DeviceEndpoint endpoint, string expectedFingerprint, PrepareUploadRequestDto request, string? pin, CancellationToken cancellationToken)
    {
        using var client = CreateClient(endpoint, expectedFingerprint);
        var path = V2Constants.BasePath + "/prepare-upload" + (pin is null ? string.Empty : $"?pin={Uri.EscapeDataString(pin)}");
        using var response = await client.PostAsJsonAsync(path, request, V2JsonContext.Default.PrepareUploadRequestDto, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new PinRequiredException(pin is not null);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new RateLimitedException();
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new PeerBusyException();
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new TransferDeclinedException();
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(V2JsonContext.Default.PrepareUploadResponseDto, cancellationToken).ConfigureAwait(false)
            ?? throw new LocalSendException(CoreText.PrepareResponseEmpty);
    }

    public async Task UploadAsync(DeviceEndpoint endpoint, string fingerprint, string sessionId, string fileId, string token, Stream content, long length, string contentType, CancellationToken cancellationToken)
    {
        using var client = CreateClient(endpoint, fingerprint, timeout: options.UploadTimeout);
        var path = $"{V2Constants.BasePath}/upload?sessionId={Uri.EscapeDataString(sessionId)}&fileId={Uri.EscapeDataString(fileId)}&token={Uri.EscapeDataString(token)}";
        using var body = new StreamContent(content);
        body.Headers.ContentLength = length;
        body.Headers.ContentType = new(contentType);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task CancelAsync(DeviceEndpoint endpoint, string fingerprint, string sessionId, CancellationToken cancellationToken)
    {
        using var client = CreateClient(endpoint, fingerprint);
        using var response = await client.PostAsync($"{V2Constants.BasePath}/cancel?sessionId={Uri.EscapeDataString(sessionId)}", null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    private HttpClient CreateClient(DeviceEndpoint endpoint, string expectedFingerprint, TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler();
        if (endpoint.Protocol == LocalSendProtocol.Https)
        {
            handler.ClientCertificates.Add(identity.Certificate);
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => ValidateCertificate(certificate, expectedFingerprint)
                ? true
                : throw new PeerIdentityException(CoreText.PeerFingerprintMismatch);
        }
        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"{(endpoint.Protocol == LocalSendProtocol.Https ? "https" : "http")}://{FormatHost(endpoint.Address)}:{endpoint.Port}"),
            Timeout = timeout ?? options.RequestTimeout
        };
    }

    private static string FormatHost(IPAddress address) => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

    private static bool ValidateCertificate(X509Certificate? certificate, string expectedFingerprint)
    {
        if (certificate is null) return false;
        if (certificate is X509Certificate2 certificate2)
            return DeviceIdentityStore.ValidatePeerCertificate(certificate2, expectedFingerprint);
        using var converted = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        return DeviceIdentityStore.ValidatePeerCertificate(converted, expectedFingerprint);
    }

    private static bool ContainsPeerIdentityException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PeerIdentityException)
                return true;
        return false;
    }
}
