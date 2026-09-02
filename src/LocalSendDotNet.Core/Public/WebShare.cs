namespace LocalSendDotNet;

/// <summary>A file offered through a browser share link.</summary>
/// <param name="Id">The identifier used in download URLs.</param>
/// <param name="FileName">The download filename.</param>
/// <param name="Size">The declared size in bytes.</param>
/// <param name="ContentType">The MIME type.</param>
public sealed record WebShareFile(string Id, string FileName, long Size, string ContentType);

/// <summary>A browser that asked to download the current web share.</summary>
/// <param name="SessionId">The browser session identifier.</param>
/// <param name="DeviceInfo">A short description parsed from the user agent.</param>
/// <param name="Ip">The browser's remote IPv4 address.</param>
/// <param name="Pending">Whether the host still needs to accept the request.</param>
public sealed record WebShareRequest(string SessionId, string DeviceInfo, string Ip, bool Pending);

/// <summary>Identifies whether the browser link sends files from or to this node.</summary>
public enum WebShareMode
{
    /// <summary>The browser downloads files offered by this node.</summary>
    Send,
    /// <summary>The browser uploads files to this node.</summary>
    Receive
}

/// <summary>A snapshot of the current web share session.</summary>
/// <param name="Active">Whether a share link is currently being served.</param>
/// <param name="Files">Files offered to browsers.</param>
/// <param name="Requests">Browsers that opened the share link.</param>
/// <param name="AutoAccept">Whether new browser requests are accepted without confirmation.</param>
/// <param name="Pin">The optional PIN browsers must supply, or <see langword="null"/> when none is required.</param>
public sealed record WebShareState(
    bool Active,
    IReadOnlyList<WebShareFile> Files,
    IReadOnlyList<WebShareRequest> Requests,
    bool AutoAccept,
    string? Pin)
{
    /// <summary>Gets the direction of the active browser link.</summary>
    public WebShareMode Mode { get; init; } = WebShareMode.Send;

    /// <summary>Gets an inactive share snapshot.</summary>
    public static readonly WebShareState Inactive = new(false, [], [], false, null);
}

/// <summary>Options for <see cref="LocalSendNode.StartWebShareAsync"/>.</summary>
public sealed class WebShareOptions
{
    /// <summary>Gets whether browser requests are accepted without confirmation.</summary>
    public bool AutoAccept { get; init; }
    /// <summary>Gets the optional PIN browsers must enter.</summary>
    public string? Pin { get; init; }
}
