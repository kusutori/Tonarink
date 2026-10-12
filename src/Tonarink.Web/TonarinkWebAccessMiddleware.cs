using System.Net;
using System.Security.Cryptography;
using System.Text;
using Tonarink.Application;

namespace Tonarink.Web;

internal sealed class TonarinkWebAccessMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[]? _passwordHash;

    public TonarinkWebAccessMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var password = configuration["Tonarink:WebPassword"];
        if (string.IsNullOrWhiteSpace(password))
            password = Environment.GetEnvironmentVariable("TONARINK_WEB_PASSWORD");
        if (!string.IsNullOrWhiteSpace(password))
            _passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_passwordHash is null)
        {
            if (IsLoopback(context.Connection.RemoteIpAddress))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(AppText.Get("WebAccessRestricted"));
            return;
        }

        if (TryReadPassword(context.Request.Headers.Authorization, out var password) &&
            CryptographicOperations.FixedTimeEquals(_passwordHash, SHA256.HashData(Encoding.UTF8.GetBytes(password))))
        {
            await _next(context);
            return;
        }

        context.Response.Headers.WWWAuthenticate = "Basic realm=\"Tonarink\", charset=\"UTF-8\"";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static bool IsLoopback(IPAddress? address) => address is not null &&
        (IPAddress.IsLoopback(address) || address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4()));

    private static bool TryReadPassword(string? header, out string password)
    {
        password = string.Empty;
        if (header is null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            var separator = value.IndexOf(':');
            if (separator < 0)
                return false;
            password = value[(separator + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
