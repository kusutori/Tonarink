using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using LocalSendDotNet.Protocol.V2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    private readonly ConcurrentDictionary<IPAddress, (int Count, DateTimeOffset LockedUntil)> _pinAttempts = new();
    private WebApplication? _application;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(V2Server).Assembly.FullName,
            EnvironmentName = Environments.Production
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestBodySize = null;
            server.ListenAnyIP(options.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
                if (options.EnableHttps)
                {
                    listen.UseHttps(https =>
                    {
                        https.ServerCertificate = identity.Certificate;
                        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                        https.ClientCertificateValidation = static (_, _, _) => true;
                    });
                }
            });
        });

        var app = builder.Build();
        app.MapPost(V2Constants.BasePath + "/register", RegisterAsync);
        app.MapGet(V2Constants.BasePath + "/info", InfoAsync);
        app.MapPost(V2Constants.BasePath + "/prepare-upload", PrepareUploadAsync);
        app.MapPost(V2Constants.BasePath + "/upload", UploadAsync);
        app.MapPost(V2Constants.BasePath + "/cancel", CancelAsync);
        app.MapGet("/", WebIndexAsync);
        app.MapPost(V2Constants.BasePath + "/prepare-download", PrepareDownloadAsync);
        app.MapGet(V2Constants.BasePath + "/download", DownloadAsync);
        app.MapPost(V2Constants.BasePath + "/prepare-web-upload", PrepareWebUploadAsync);
        _application = app;
        try { await app.StartAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (ContainsAddressInUse(exception))
        {
            throw new PortUnavailableException(options.Port, exception);
        }
    }

    private async Task RegisterAsync(HttpContext context)
    {
        if (!ConfigureRequestLimit(context, 64 * 1024))
            return;
        var payload = await ReadJsonAsync(context, V2JsonContext.Default.DeviceInfoDto).ConfigureAwait(false);
        if (payload is null)
            return;
        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted).ConfigureAwait(false);
        var certificateFingerprint = certificate is null ? null : DeviceIdentityStore.Fingerprint(certificate);
        if (options.EnableHttps && (certificate is null || !DeviceIdentityStore.ValidatePeerCertificate(certificate, payload.Fingerprint)))
        {
            logger.LogWarning("Ignoring spoofed register fingerprint from {Address}", context.Connection.RemoteIpAddress);
            await WriteErrorAsync(context, HttpStatusCode.Forbidden, "Client fingerprint mismatch").ConfigureAwait(false);
            return;
        }
        else
        {
            await onRegister(payload, RemoteAddress(context), certificateFingerprint).ConfigureAwait(false);
        }

        var info = localInfo();
        await context.Response.WriteAsJsonAsync(new RegisterResponseDto
        {
            Alias = info.Alias,
            Version = info.Version,
            DeviceModel = info.DeviceModel,
            DeviceType = info.DeviceType,
            Fingerprint = info.Fingerprint,
            Download = false
        }, V2JsonContext.Default.RegisterResponseDto, contentType: null, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task WebIndexAsync(HttpContext context)
    {
        var state = webShare.Snapshot();
        if (!state.Active)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var html = WebShareHtml.Render(localInfo().Alias, state.Pin is not null, state.Mode);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task PrepareDownloadAsync(HttpContext context)
    {
        try
        {
            var result = await webShare.PrepareAsync(
                RemoteAddress(context),
                context.Request.Headers.UserAgent.ToString(),
                context.Request.Query["pin"].ToString(),
                context.RequestAborted).ConfigureAwait(false);
            if (result is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await context.Response.WriteAsJsonAsync(result, V2JsonContext.Default.PrepareDownloadResponseDto, contentType: null, context.RequestAborted).ConfigureAwait(false);
        }
        catch (WebSharePinException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }
        catch (WebSharePinRateLimitedException)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        }
        catch (TimeoutException)
        {
            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
        }
    }

    private async Task DownloadAsync(HttpContext context)
    {
        var sessionId = context.Request.Query["sessionId"].ToString();
        var fileId = context.Request.Query["fileId"].ToString();
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(fileId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        try
        {
            var offered = await webShare.OpenFileAsync(
                sessionId,
                fileId,
                RemoteAddress(context),
                context.Request.Query["pin"].ToString(),
                context.RequestAborted).ConfigureAwait(false);
            if (offered is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var (file, item) = offered.Value;
            context.Response.ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;
            context.Response.ContentLength = file.Size;
            var encoded = Uri.EscapeDataString(file.FileName);
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{file.FileName.Replace("\"", "%22")}\"; filename*=UTF-8''{encoded}";
            await using var source = await item.OpenReadAsync(context.RequestAborted).ConfigureAwait(false);
            await source.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        catch (WebSharePinException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }
        catch (WebSharePinRateLimitedException)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        }
    }

    private async Task PrepareWebUploadAsync(HttpContext context)
    {
        var remote = RemoteAddress(context);
        if (!ConfigureRequestLimit(context, options.MaxPrepareRequestBytes))
            return;
        var payload = await ReadJsonAsync(context, V2JsonContext.Default.WebUploadPrepareRequestDto).ConfigureAwait(false);
        if (payload is null)
            return;
        if (payload.Files.Length == 0 || payload.Files.GroupBy(static file => file.Id, StringComparer.Ordinal).Any(static group => group.Count() != 1) ||
            payload.Files.Any(static file => file.Size < 0 || string.IsNullOrWhiteSpace(file.Id) || string.IsNullOrWhiteSpace(file.FileName) || string.IsNullOrWhiteSpace(file.FileType)))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid file metadata").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Length > options.MaxIncomingItemsPerTransfer || ExceedsTransferLimit(payload.Files, options.MaxIncomingTransferBytes))
        {
            await WriteErrorAsync(context, HttpStatusCode.RequestEntityTooLarge, "Incoming transfer exceeds configured limits").ConfigureAwait(false);
            return;
        }

        try
        {
            if (!webShare.TryAuthorizeReceive(remote, context.Request.Query["pin"].ToString(), out var autoAccept))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var userAgent = context.Request.Headers.UserAgent.ToString();
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{remote}|{userAgent}")));
            var request = new PrepareUploadRequestDto
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
            var outcome = await onPrepare(request, remote, fingerprint, autoAccept, context.RequestAborted).ConfigureAwait(false);
            context.Response.StatusCode = (int)outcome.StatusCode;
            if (outcome.Response is not null)
                await context.Response.WriteAsJsonAsync(outcome.Response, V2JsonContext.Default.PrepareUploadResponseDto, contentType: null, context.RequestAborted).ConfigureAwait(false);
            else if (outcome.Message is not null)
                await context.Response.WriteAsJsonAsync(new ErrorResponseDto { Message = outcome.Message }, V2JsonContext.Default.ErrorResponseDto, contentType: null, context.RequestAborted).ConfigureAwait(false);
        }
        catch (WebSharePinException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }
        catch (WebSharePinRateLimitedException)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        }
    }

    private Task InfoAsync(HttpContext context)
    {
        var info = localInfo();
        return context.Response.WriteAsJsonAsync(new RegisterResponseDto
        {
            Alias = info.Alias,
            Version = info.Version,
            DeviceModel = info.DeviceModel,
            DeviceType = info.DeviceType,
            Fingerprint = info.Fingerprint,
            Download = false
        }, V2JsonContext.Default.RegisterResponseDto, contentType: null, context.RequestAborted);
    }

    private async Task PrepareUploadAsync(HttpContext context)
    {
        var remote = RemoteAddress(context);
        if (!ConfigureRequestLimit(context, options.MaxPrepareRequestBytes))
            return;
        var payload = await ReadJsonAsync(context, V2JsonContext.Default.PrepareUploadRequestDto).ConfigureAwait(false);
        if (payload is null)
            return;
        if (payload.Files.Count == 0)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "No files provided").ConfigureAwait(false);
            return;
        }
        if (payload.Info.Port is < 1 or > ushort.MaxValue || string.IsNullOrWhiteSpace(payload.Info.Alias) ||
            !IsFingerprint(payload.Info.Fingerprint) ||
            (!StringComparer.OrdinalIgnoreCase.Equals(payload.Info.Protocol, "http") && !StringComparer.OrdinalIgnoreCase.Equals(payload.Info.Protocol, "https")))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid sender metadata").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Any(static pair => pair.Value.Size < 0 || string.IsNullOrWhiteSpace(pair.Value.FileName) ||
            string.IsNullOrWhiteSpace(pair.Value.FileType) || !StringComparer.Ordinal.Equals(pair.Key, pair.Value.Id)))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid file metadata").ConfigureAwait(false);
            return;
        }
        if (payload.Files.Count > options.MaxIncomingItemsPerTransfer || ExceedsTransferLimit(payload.Files.Values, options.MaxIncomingTransferBytes))
        {
            await WriteErrorAsync(context, HttpStatusCode.RequestEntityTooLarge, "Incoming transfer exceeds configured limits").ConfigureAwait(false);
            return;
        }

        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted).ConfigureAwait(false);
        var certFingerprint = certificate is null ? null : DeviceIdentityStore.Fingerprint(certificate);
        if (options.EnableHttps && (certificate is null || !DeviceIdentityStore.ValidatePeerCertificate(certificate, payload.Info.Fingerprint)))
        {
            await WriteErrorAsync(context, HttpStatusCode.Forbidden, "Client fingerprint mismatch").ConfigureAwait(false);
            return;
        }
        if (!CheckPin(context, remote))
            return;

        var outcome = await onPrepare(payload, remote, certFingerprint, false, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = (int)outcome.StatusCode;
        if (outcome.Response is not null)
            await context.Response.WriteAsJsonAsync(outcome.Response, V2JsonContext.Default.PrepareUploadResponseDto, contentType: null, context.RequestAborted).ConfigureAwait(false);
        else if (outcome.Message is not null)
            await context.Response.WriteAsJsonAsync(new ErrorResponseDto { Message = outcome.Message }, V2JsonContext.Default.ErrorResponseDto, contentType: null, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task UploadAsync(HttpContext context)
    {
        var query = context.Request.Query;
        if (!query.TryGetValue("sessionId", out var sessionId) || !query.TryGetValue("fileId", out var fileId) || !query.TryGetValue("token", out var token))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Missing upload query parameters").ConfigureAwait(false);
            return;
        }
        var status = await onUpload(sessionId.ToString(), fileId.ToString(), token.ToString(), RemoteAddress(context), context.Request.Body, context.Request.ContentLength, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = (int)status;
    }

    private async Task CancelAsync(HttpContext context)
    {
        if (!context.Request.Query.TryGetValue("sessionId", out var sessionId))
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Missing sessionId").ConfigureAwait(false);
            return;
        }
        context.Response.StatusCode = await onCancel(sessionId.ToString(), RemoteAddress(context), context.RequestAborted).ConfigureAwait(false)
            ? StatusCodes.Status200OK
            : StatusCodes.Status404NotFound;
    }

    private bool CheckPin(HttpContext context, IPAddress remote)
    {
        if (options.ReceivePin is null)
            return true;
        var now = DateTimeOffset.UtcNow;
        var attempts = _pinAttempts.GetOrAdd(remote, static _ => (0, DateTimeOffset.MinValue));
        if (attempts.Count >= 3 && attempts.LockedUntil > now)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return false;
        }
        if (attempts.Count >= 3)
            attempts = (0, DateTimeOffset.MinValue);
        var supplied = context.Request.Query["pin"].ToString();
        if (StringComparer.Ordinal.Equals(supplied, options.ReceivePin))
        {
            _pinAttempts.TryRemove(remote, out _);
            return true;
        }
        if (supplied.Length > 0)
        {
            var count = attempts.Count + 1;
            _pinAttempts[remote] = (count, count >= 3 ? now + options.PinLockoutDuration : DateTimeOffset.MinValue);
        }
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }

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

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context, JsonTypeInfo<T> jsonTypeInfo)
    {
        try
        {
            return await context.Request.ReadFromJsonAsync(jsonTypeInfo, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await WriteErrorAsync(context, HttpStatusCode.RequestEntityTooLarge, "JSON request exceeds the configured limit").ConfigureAwait(false);
            return default;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or Microsoft.AspNetCore.Http.BadHttpRequestException)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "Invalid JSON request").ConfigureAwait(false);
            return default;
        }
    }

    private static bool ConfigureRequestLimit(HttpContext context, long limit)
    {
        if (context.Request.ContentLength > limit)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return false;
        }
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = limit;
        return true;
    }

    private static Task WriteErrorAsync(HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.StatusCode = (int)status;
        return context.Response.WriteAsJsonAsync(new ErrorResponseDto { Message = message }, V2JsonContext.Default.ErrorResponseDto, contentType: null, context.RequestAborted);
    }

    private static IPAddress RemoteAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress ?? IPAddress.None;
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static bool ContainsAddressInUse(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AddressInUseException || current is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse })
                return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is null)
            return;
        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
        _application = null;
    }
}
