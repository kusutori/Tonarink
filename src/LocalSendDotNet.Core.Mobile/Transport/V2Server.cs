using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LocalSendDotNet.Protocol.V2;
using Microsoft.Extensions.Logging;

namespace LocalSendDotNet;

internal sealed record PrepareOutcome(HttpStatusCode StatusCode, PrepareUploadResponseDto? Response = null, string? Message = null);

internal sealed class V2Server(
    LocalSendOptions options,
    DeviceIdentity identity,
    Func<DeviceInfoDto> localInfo,
    Func<DeviceInfoDto, IPAddress, string?, Task> onRegister,
    Func<PrepareUploadRequestDto, IPAddress, string?, bool, CancellationToken, Task<PrepareOutcome>> onPrepare,
    Func<string, string, string, IPAddress, Stream, long?, CancellationToken, Task<HttpStatusCode>> onUpload,
    Func<string, IPAddress, CancellationToken, Task<bool>> onCancel,
    ILogger logger,
    WebShareService webShare) : IAsyncDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;
    private readonly ConcurrentDictionary<IPAddress, (int Count, DateTimeOffset LockedUntil)> _pinAttempts = new();
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private CancellationTokenSource? _lifetime;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private long _connectionId;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var listener = new TcpListener(IPAddress.Any, options.Port);
        try
        {
            listener.Start();
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            throw new PortUnavailableException(options.Port, exception);
        }

        _listener = listener;
        _lifetime = new CancellationTokenSource();
        _acceptLoop = AcceptLoopAsync(listener, _lifetime.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var id = Interlocked.Increment(ref _connectionId);
                var task = HandleClientAsync(client, cancellationToken);
                _connections[id] = task;
                _ = task.ContinueWith(static (completedTask, state) =>
                {
                    var (connections, connectionId) = ((ConcurrentDictionary<long, Task>, long))state!;
                    connections.TryRemove(connectionId, out _);
                }, (_connections, id), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            var remote = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address ?? IPAddress.None;
            if (remote.IsIPv4MappedToIPv6)
                remote = remote.MapToIPv4();
            Stream transport = client.GetStream();
            SslStream? ssl = null;
            X509Certificate2? clientCertificate = null;
            try
            {
                if (options.EnableHttps)
                {
                    ssl = new SslStream(transport, leaveInnerStreamOpen: false, static (_, _, _, _) => true);
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = identity.Certificate,
                        ClientCertificateRequired = true,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    }, cancellationToken).ConfigureAwait(false);
                    transport = ssl;
                    if (ssl.RemoteCertificate is { } certificate)
                        clientCertificate = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
                }

                var request = await PortableRequest.ReadAsync(transport, remote, clientCertificate, cancellationToken).ConfigureAwait(false);
                if (request is null)
                    return;
                await DispatchAsync(request, transport).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                logger.LogDebug(exception, "Rejected malformed HTTP request from {Address}", remote);
                await PortableResponse.WriteStatusAsync(transport, HttpStatusCode.BadRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException or SocketException or OperationCanceledException)
            {
                logger.LogDebug(exception, "Portable HTTP connection from {Address} ended", remote);
            }
            finally
            {
                clientCertificate?.Dispose();
                if (ssl is not null)
                    await ssl.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private Task DispatchAsync(PortableRequest request, Stream response) => (request.Method, request.Path) switch
    {
        ("POST", V2Constants.BasePath + "/register") => RegisterAsync(request, response),
        ("GET", V2Constants.BasePath + "/info") => InfoAsync(request, response),
        ("POST", V2Constants.BasePath + "/prepare-upload") => PrepareUploadAsync(request, response),
        ("POST", V2Constants.BasePath + "/upload") => UploadAsync(request, response),
        ("POST", V2Constants.BasePath + "/cancel") => CancelAsync(request, response),
        ("GET", "/") => WebIndexAsync(request, response),
        ("POST", V2Constants.BasePath + "/prepare-download") => PrepareDownloadAsync(request, response),
        ("GET", V2Constants.BasePath + "/download") => DownloadAsync(request, response),
        ("POST", V2Constants.BasePath + "/prepare-web-upload") => PrepareWebUploadAsync(request, response),
        _ => PortableResponse.WriteStatusAsync(response, HttpStatusCode.NotFound, request.CancellationToken),
    };

    private async Task RegisterAsync(PortableRequest request, Stream response)
    {
        var payload = await ReadJsonAsync(request, response, 64 * 1024, V2JsonContext.Default.DeviceInfoDto).ConfigureAwait(false);
        if (payload is null)
            return;
        var fingerprint = request.ClientCertificate is null ? null : DeviceIdentityStore.Fingerprint(request.ClientCertificate);
        if (options.EnableHttps && (request.ClientCertificate is null || !DeviceIdentityStore.ValidatePeerCertificate(request.ClientCertificate, payload.Fingerprint)))
        {
            logger.LogWarning("Ignoring spoofed register fingerprint from {Address}", request.RemoteAddress);
            await WriteErrorAsync(response, request, HttpStatusCode.Forbidden, "Client fingerprint mismatch").ConfigureAwait(false);
            return;
        }
        await onRegister(payload, request.RemoteAddress, fingerprint).ConfigureAwait(false);
        await WriteJsonAsync(response, request, HttpStatusCode.OK, ToInfoResponse()).ConfigureAwait(false);
    }

    private Task InfoAsync(PortableRequest request, Stream response) =>
        WriteJsonAsync(response, request, HttpStatusCode.OK, ToInfoResponse());

    private RegisterResponseDto ToInfoResponse()
    {
        var info = localInfo();
        return new RegisterResponseDto
        {
            Alias = info.Alias,
            Version = info.Version,
            DeviceModel = info.DeviceModel,
            DeviceType = info.DeviceType,
            Fingerprint = info.Fingerprint,
            Download = false,
        };
    }

    private async Task PrepareUploadAsync(PortableRequest request, Stream response)
    {
        var payload = await ReadJsonAsync(request, response, options.MaxPrepareRequestBytes, V2JsonContext.Default.PrepareUploadRequestDto).ConfigureAwait(false);
        if (payload is null)
            return;
        if (payload.Files.Count == 0)
        {
            await WriteErrorAsync(response, request, HttpStatusCode.BadRequest, "No files provided").ConfigureAwait(false);
            return;
        }
        if (payload.Info.Port is < 1 or > ushort.MaxValue || string.IsNullOrWhiteSpace(payload.Info.Alias) ||
            !IsFingerprint(payload.Info.Fingerprint) ||
            (!StringComparer.OrdinalIgnoreCase.Equals(payload.Info.Protocol, "http") && !StringComparer.OrdinalIgnoreCase.Equals(payload.Info.Protocol, "https")))
        {
            await WriteErrorAsync(response, request, HttpStatusCode.BadRequest, "Invalid sender metadata").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Any(static pair => pair.Value.Size < 0 || string.IsNullOrWhiteSpace(pair.Value.FileName) ||
            string.IsNullOrWhiteSpace(pair.Value.FileType) || !StringComparer.Ordinal.Equals(pair.Key, pair.Value.Id)))
        {
            await WriteErrorAsync(response, request, HttpStatusCode.BadRequest, "Invalid file metadata").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Count > options.MaxIncomingItemsPerTransfer || ExceedsTransferLimit(payload.Files.Values, options.MaxIncomingTransferBytes))
        {
            await WriteErrorAsync(response, request, HttpStatusCode.RequestEntityTooLarge, "Incoming transfer exceeds configured limits").ConfigureAwait(false);
            return;
        }
        var fingerprint = request.ClientCertificate is null ? null : DeviceIdentityStore.Fingerprint(request.ClientCertificate);
        if (options.EnableHttps && (request.ClientCertificate is null || !DeviceIdentityStore.ValidatePeerCertificate(request.ClientCertificate, payload.Info.Fingerprint)))
        {
            await WriteErrorAsync(response, request, HttpStatusCode.Forbidden, "Client fingerprint mismatch").ConfigureAwait(false);
            return;
        }
        var pinStatus = CheckPin(request);
        if (pinStatus is { } rejected)
        {
            await PortableResponse.WriteStatusAsync(response, rejected, request.CancellationToken).ConfigureAwait(false);
            return;
        }

        var outcome = await onPrepare(payload, request.RemoteAddress, fingerprint, false, request.CancellationToken).ConfigureAwait(false);
        if (outcome.Response is not null)
            await WriteJsonAsync(response, request, outcome.StatusCode, outcome.Response).ConfigureAwait(false);
        else if (outcome.Message is not null)
            await WriteErrorAsync(response, request, outcome.StatusCode, outcome.Message).ConfigureAwait(false);
        else
            await PortableResponse.WriteStatusAsync(response, outcome.StatusCode, request.CancellationToken).ConfigureAwait(false);
    }

    private async Task UploadAsync(PortableRequest request, Stream response)
    {
        if (!request.Query.TryGetValue("sessionId", out var sessionId) || !request.Query.TryGetValue("fileId", out var fileId) || !request.Query.TryGetValue("token", out var token))
        {
            await WriteErrorAsync(response, request, HttpStatusCode.BadRequest, "Missing upload query parameters").ConfigureAwait(false);
            return;
        }
        var status = await onUpload(sessionId, fileId, token, request.RemoteAddress, request.Body, request.ContentLength, request.CancellationToken).ConfigureAwait(false);
        await PortableResponse.WriteStatusAsync(response, status, request.CancellationToken).ConfigureAwait(false);
    }

    private async Task CancelAsync(PortableRequest request, Stream response)
    {
        if (!request.Query.TryGetValue("sessionId", out var sessionId))
        {
            await WriteErrorAsync(response, request, HttpStatusCode.BadRequest, "Missing sessionId").ConfigureAwait(false);
            return;
        }
        var found = await onCancel(sessionId, request.RemoteAddress, request.CancellationToken).ConfigureAwait(false);
        await PortableResponse.WriteStatusAsync(response, found ? HttpStatusCode.OK : HttpStatusCode.NotFound, request.CancellationToken).ConfigureAwait(false);
    }

    private async Task WebIndexAsync(PortableRequest request, Stream response)
    {
        var state = webShare.Snapshot();
        if (!state.Active)
        {
            await PortableResponse.WriteStatusAsync(response, HttpStatusCode.NotFound, request.CancellationToken).ConfigureAwait(false);
            return;
        }
        var body = Encoding.UTF8.GetBytes(WebShareHtml.Render(localInfo().Alias, state.Pin is not null, state.Mode));
        await PortableResponse.WriteAsync(response, HttpStatusCode.OK, "text/html; charset=utf-8", body, null, request.CancellationToken).ConfigureAwait(false);
    }

    private async Task PrepareDownloadAsync(PortableRequest request, Stream response)
    {
        try
        {
            var result = await webShare.PrepareAsync(request.RemoteAddress, request.Header("User-Agent"), request.QueryValue("pin"), request.CancellationToken).ConfigureAwait(false);
            if (result is null)
                await PortableResponse.WriteStatusAsync(response, HttpStatusCode.Forbidden, request.CancellationToken).ConfigureAwait(false);
            else
                await WriteJsonAsync(response, request, HttpStatusCode.OK, result).ConfigureAwait(false);
        }
        catch (WebSharePinException) { await PortableResponse.WriteStatusAsync(response, HttpStatusCode.Unauthorized, request.CancellationToken).ConfigureAwait(false); }
        catch (WebSharePinRateLimitedException) { await PortableResponse.WriteStatusAsync(response, HttpStatusCode.TooManyRequests, request.CancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { await PortableResponse.WriteStatusAsync(response, HttpStatusCode.RequestTimeout, request.CancellationToken).ConfigureAwait(false); }
    }

    private async Task PrepareWebUploadAsync(PortableRequest request, Stream response)
    {
        var payload = await ReadJsonAsync(request, response, options.MaxPrepareRequestBytes, V2JsonContext.Default.WebUploadPrepareRequestDto).ConfigureAwait(false);
        if (payload is null)
            return;
        if (payload.Files.Length == 0 || payload.Files.GroupBy(static file => file.Id, StringComparer.Ordinal).Any(static group => group.Count() != 1) ||
            payload.Files.Any(static file => file.Size < 0 || string.IsNullOrWhiteSpace(file.Id) || string.IsNullOrWhiteSpace(file.FileName) || string.IsNullOrWhiteSpace(file.FileType)))
        {
            await WriteErrorAsync(response, request, HttpStatusCode.BadRequest, "Invalid file metadata").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Length > options.MaxIncomingItemsPerTransfer || ExceedsTransferLimit(payload.Files, options.MaxIncomingTransferBytes))
        {
            await WriteErrorAsync(response, request, HttpStatusCode.RequestEntityTooLarge, "Incoming transfer exceeds configured limits").ConfigureAwait(false);
            return;
        }

        try
        {
            if (!webShare.TryAuthorizeReceive(request.RemoteAddress, request.QueryValue("pin"), out var autoAccept))
            {
                await PortableResponse.WriteStatusAsync(response, HttpStatusCode.NotFound, request.CancellationToken).ConfigureAwait(false);
                return;
            }
            var userAgent = request.Header("User-Agent") ?? string.Empty;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.RemoteAddress}|{userAgent}")));
            var preparedRequest = new PrepareUploadRequestDto
            {
                Info = new DeviceInfoDto
                {
                    Alias = "Web browser",
                    Version = V2Constants.Version,
                    DeviceModel = WebShareUserAgent.Describe(userAgent),
                    DeviceType = "web",
                    Fingerprint = fingerprint,
                    Port = 1,
                    Protocol = "http",
                    Download = false
                },
                Files = payload.Files.ToDictionary(static file => file.Id, StringComparer.Ordinal)
            };
            var outcome = await onPrepare(preparedRequest, request.RemoteAddress, fingerprint, autoAccept, request.CancellationToken).ConfigureAwait(false);
            if (outcome.Response is not null)
                await WriteJsonAsync(response, request, outcome.StatusCode, outcome.Response).ConfigureAwait(false);
            else if (outcome.Message is not null)
                await WriteErrorAsync(response, request, outcome.StatusCode, outcome.Message).ConfigureAwait(false);
            else
                await PortableResponse.WriteStatusAsync(response, outcome.StatusCode, request.CancellationToken).ConfigureAwait(false);
        }
        catch (WebSharePinException) { await PortableResponse.WriteStatusAsync(response, HttpStatusCode.Unauthorized, request.CancellationToken).ConfigureAwait(false); }
        catch (WebSharePinRateLimitedException) { await PortableResponse.WriteStatusAsync(response, HttpStatusCode.TooManyRequests, request.CancellationToken).ConfigureAwait(false); }
    }

    private async Task DownloadAsync(PortableRequest request, Stream response)
    {
        var sessionId = request.QueryValue("sessionId");
        var fileId = request.QueryValue("fileId");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(fileId))
        {
            await PortableResponse.WriteStatusAsync(response, HttpStatusCode.BadRequest, request.CancellationToken).ConfigureAwait(false);
            return;
        }
        try
        {
            var offered = await webShare.OpenFileAsync(sessionId, fileId, request.RemoteAddress, request.QueryValue("pin"), request.CancellationToken).ConfigureAwait(false);
            if (offered is null)
            {
                await PortableResponse.WriteStatusAsync(response, HttpStatusCode.NotFound, request.CancellationToken).ConfigureAwait(false);
                return;
            }
            var (file, item) = offered.Value;
            var encoded = Uri.EscapeDataString(file.FileName);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Disposition"] = $"attachment; filename=\"{file.FileName.Replace("\"", "%22", StringComparison.Ordinal)}\"; filename*=UTF-8''{encoded}",
            };
            await PortableResponse.WriteHeadersAsync(response, HttpStatusCode.OK,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, file.Size, headers, request.CancellationToken).ConfigureAwait(false);
            await using var source = await item.OpenReadAsync(request.CancellationToken).ConfigureAwait(false);
            await source.CopyToAsync(response, request.CancellationToken).ConfigureAwait(false);
        }
        catch (WebSharePinException) { await PortableResponse.WriteStatusAsync(response, HttpStatusCode.Unauthorized, request.CancellationToken).ConfigureAwait(false); }
        catch (WebSharePinRateLimitedException) { await PortableResponse.WriteStatusAsync(response, HttpStatusCode.TooManyRequests, request.CancellationToken).ConfigureAwait(false); }
    }

    private HttpStatusCode? CheckPin(PortableRequest request)
    {
        if (options.ReceivePin is null)
            return null;
        var now = DateTimeOffset.UtcNow;
        var attempts = _pinAttempts.GetOrAdd(request.RemoteAddress, static _ => (0, DateTimeOffset.MinValue));
        if (attempts.Count >= 3 && attempts.LockedUntil > now)
            return HttpStatusCode.TooManyRequests;
        if (attempts.Count >= 3)
            attempts = (0, DateTimeOffset.MinValue);
        var supplied = request.QueryValue("pin");
        if (StringComparer.Ordinal.Equals(supplied, options.ReceivePin))
        {
            _pinAttempts.TryRemove(request.RemoteAddress, out _);
            return null;
        }
        if (supplied.Length > 0)
        {
            var count = attempts.Count + 1;
            _pinAttempts[request.RemoteAddress] = (count, count >= 3 ? now + options.PinLockoutDuration : DateTimeOffset.MinValue);
        }
        return HttpStatusCode.Unauthorized;
    }

    private static async Task<T?> ReadJsonAsync<T>(PortableRequest request, Stream response, long limit, JsonTypeInfo<T> typeInfo)
    {
        if (request.ContentLength is > 0 && request.ContentLength > limit)
        {
            await WriteErrorAsync(response, request, HttpStatusCode.RequestEntityTooLarge, "JSON request exceeds the configured limit").ConfigureAwait(false);
            return default;
        }
        try
        {
            return await JsonSerializer.DeserializeAsync(request.Body, typeInfo, request.CancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await WriteErrorAsync(response, request, HttpStatusCode.BadRequest, "Invalid JSON request").ConfigureAwait(false);
            return default;
        }
    }

    private static Task WriteJsonAsync<T>(Stream response, PortableRequest request, HttpStatusCode status, T value)
    {
        var typeInfo = (JsonTypeInfo<T>)V2JsonContext.Default.GetTypeInfo(typeof(T))!;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        return PortableResponse.WriteAsync(response, status, "application/json; charset=utf-8", bytes, null, request.CancellationToken);
    }

    private static Task WriteErrorAsync(Stream response, PortableRequest request, HttpStatusCode status, string message) =>
        WriteJsonAsync(response, request, status, new ErrorResponseDto { Message = message });

    private static bool ExceedsTransferLimit(IEnumerable<FileDto> files, long limit)
    {
        long total = 0;
        foreach (var file in files)
        {
            if (file.Size > limit - total)
                return true;
            total += file.Size;
        }
        return false;
    }

    private static bool IsFingerprint(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    public async ValueTask DisposeAsync()
    {
        var lifetime = _lifetime;
        var listener = _listener;
        _lifetime = null;
        _listener = null;
        if (lifetime is null)
            return;
        await lifetime.CancelAsync().ConfigureAwait(false);
        listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        try { await Task.WhenAll(_connections.Values).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException) { }
        lifetime.Dispose();
        _acceptLoop = null;
    }

    private sealed class PortableRequest
    {
        private PortableRequest(string method, string path, IReadOnlyDictionary<string, string> query,
            IReadOnlyDictionary<string, string> headers, Stream body, long? contentLength, IPAddress remoteAddress,
            X509Certificate2? clientCertificate, CancellationToken cancellationToken)
        {
            Method = method;
            Path = path;
            Query = query;
            Headers = headers;
            Body = body;
            ContentLength = contentLength;
            RemoteAddress = remoteAddress;
            ClientCertificate = clientCertificate;
            CancellationToken = cancellationToken;
        }

        public string Method { get; }
        public string Path { get; }
        public IReadOnlyDictionary<string, string> Query { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public Stream Body { get; }
        public long? ContentLength { get; }
        public IPAddress RemoteAddress { get; }
        public X509Certificate2? ClientCertificate { get; }
        public CancellationToken CancellationToken { get; }

        public string Header(string name) => Headers.TryGetValue(name, out var value) ? value : string.Empty;
        public string QueryValue(string name) => Query.TryGetValue(name, out var value) ? value : string.Empty;

        public static async Task<PortableRequest?> ReadAsync(Stream stream, IPAddress remoteAddress,
            X509Certificate2? clientCertificate, CancellationToken cancellationToken)
        {
            var rented = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                using var buffer = new MemoryStream();
                var headerEnd = -1;
                while (headerEnd < 0)
                {
                    var read = await stream.ReadAsync(rented, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        return null;
                    buffer.Write(rented, 0, read);
                    if (buffer.Length > MaxHeaderBytes)
                        throw new InvalidDataException("HTTP header is too large.");
                    headerEnd = FindHeaderEnd(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
                }

                var all = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
                var headerText = Encoding.ASCII.GetString(all[..headerEnd]);
                var lines = headerText.Split("\r\n", StringSplitOptions.None);
                var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid HTTP request line.");
                var method = requestLine[0].ToUpperInvariant();
                var target = requestLine[1];
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1))
                {
                    if (line.Length == 0)
                        continue;
                    var separator = line.IndexOf(':');
                    if (separator <= 0)
                        throw new InvalidDataException("Invalid HTTP header.");
                    headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }
                long? contentLength = null;
                if (headers.TryGetValue("Content-Length", out var lengthText))
                {
                    if (!long.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length < 0)
                        throw new InvalidDataException("Invalid Content-Length.");
                    contentLength = length;
                }
                var chunked = headers.TryGetValue("Transfer-Encoding", out var encoding)
                    && encoding.Split(',').Any(static value => string.Equals(value.Trim(), "chunked", StringComparison.OrdinalIgnoreCase));

                var uri = new Uri("http://localhost" + target, UriKind.Absolute);
                var query = ParseQuery(uri.Query);
                var bodyOffset = headerEnd + 4;
                var prefix = all[bodyOffset..].ToArray();
                Stream body = new PrefixReadStream(prefix, stream);
                if (chunked)
                    body = new ChunkedReadStream(body);
                else if (contentLength is { } bodyLength)
                    body = new LimitedReadStream(body, bodyLength);
                return new PortableRequest(method, uri.AbsolutePath, query, headers, body, contentLength,
                    remoteAddress, clientCertificate, cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static int FindHeaderEnd(ReadOnlySpan<byte> bytes)
        {
            for (var index = 0; index <= bytes.Length - 4; index++)
                if (bytes[index] == '\r' && bytes[index + 1] == '\n' && bytes[index + 2] == '\r' && bytes[index + 3] == '\n')
                    return index;
            return -1;
        }

        private static IReadOnlyDictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                var key = Uri.UnescapeDataString((separator < 0 ? pair : pair[..separator]).Replace('+', ' '));
                var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
                result[key] = value;
            }
            return result;
        }
    }

    private static class PortableResponse
    {
        public static Task WriteStatusAsync(Stream stream, HttpStatusCode status, CancellationToken cancellationToken) =>
            WriteAsync(stream, status, null, ReadOnlyMemory<byte>.Empty, null, cancellationToken);

        public static async Task WriteAsync(Stream stream, HttpStatusCode status, string? contentType, ReadOnlyMemory<byte> body,
            IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            await WriteHeadersAsync(stream, status, contentType, body.Length, headers, cancellationToken).ConfigureAwait(false);
            if (!body.IsEmpty)
                await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task WriteHeadersAsync(Stream stream, HttpStatusCode status, string? contentType, long contentLength,
            IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            var builder = new StringBuilder()
                .Append("HTTP/1.1 ").Append((int)status).Append(' ').Append(Reason(status)).Append("\r\n")
                .Append("Content-Length: ").Append(contentLength.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
                .Append("Connection: close\r\n");
            if (contentType is not null)
                builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
            if (headers is not null)
                foreach (var (name, value) in headers)
                    builder.Append(name).Append(": ").Append(value).Append("\r\n");
            builder.Append("\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), cancellationToken).ConfigureAwait(false);
        }

        private static string Reason(HttpStatusCode status) => status switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.Unauthorized => "Unauthorized",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.RequestTimeout => "Request Timeout",
            HttpStatusCode.RequestEntityTooLarge => "Payload Too Large",
            HttpStatusCode.TooManyRequests => "Too Many Requests",
            _ => status.ToString(),
        };
    }

    private sealed class PrefixReadStream(byte[] prefix, Stream inner) : Stream
    {
        private int _offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = CopyPrefix(buffer.AsSpan(offset, count));
            return copied > 0 ? copied : inner.Read(buffer, offset, count);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var copied = CopyPrefix(buffer.Span);
            return copied > 0 ? copied : await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        private int CopyPrefix(Span<byte> target)
        {
            var count = Math.Min(target.Length, prefix.Length - _offset);
            if (count <= 0) return 0;
            prefix.AsSpan(_offset, count).CopyTo(target);
            _offset += count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class LimitedReadStream(Stream inner, long remaining) : Stream
    {
        private long _remaining = remaining;
        private readonly long _length = remaining;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining == 0) return 0;
            var read = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining == 0) return 0;
            var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken).ConfigureAwait(false);
            _remaining -= read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ChunkedReadStream(Stream inner) : Stream
    {
        private long _remaining;
        private bool _consumeTerminator;
        private bool _finished;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_finished || buffer.Length == 0)
                return 0;
            if (_remaining == 0)
            {
                if (_consumeTerminator)
                {
                    await ExpectCrlfAsync(cancellationToken).ConfigureAwait(false);
                    _consumeTerminator = false;
                }
                var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                var separator = line.IndexOf(';');
                var sizeText = separator < 0 ? line : line[..separator];
                if (!long.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _remaining) || _remaining < 0)
                    throw new InvalidDataException("Invalid HTTP chunk size.");
                if (_remaining == 0)
                {
                    while ((await ReadLineAsync(cancellationToken).ConfigureAwait(false)).Length != 0) { }
                    _finished = true;
                    return 0;
                }
            }

            var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("The HTTP chunk ended before its declared size.");
            _remaining -= read;
            if (_remaining == 0)
                _consumeTerminator = true;
            return read;
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            var bytes = new List<byte>(64);
            var previous = -1;
            var one = new byte[1];
            while (bytes.Count <= 8192)
            {
                if (await inner.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
                    throw new EndOfStreamException("Unexpected end of chunked HTTP body.");
                var current = one[0];
                if (previous == '\r' && current == '\n')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                    return Encoding.ASCII.GetString([.. bytes]);
                }
                bytes.Add(current);
                previous = current;
            }
            throw new InvalidDataException("HTTP chunk line is too long.");
        }

        private async Task ExpectCrlfAsync(CancellationToken cancellationToken)
        {
            var terminator = new byte[2];
            await inner.ReadExactlyAsync(terminator, cancellationToken).ConfigureAwait(false);
            if (terminator[0] != '\r' || terminator[1] != '\n')
                throw new InvalidDataException("HTTP chunk is missing its terminator.");
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
