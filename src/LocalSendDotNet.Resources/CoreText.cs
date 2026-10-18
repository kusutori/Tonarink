using System.Globalization;
using System.Resources;

namespace LocalSendDotNet.Resources;

/// <summary>Shared, culture-aware text for the C# and F# Core implementations.</summary>
public static class CoreText
{
    private const string Prefix = "LocalSendDotNet.Resources.Messages";
    private static readonly ResourceManager English = new(Prefix, typeof(CoreText).Assembly);
    private static readonly IReadOnlyDictionary<string, ResourceManager> Translations =
        new[] { "zh", "ja", "ko", "fr", "es" }.ToDictionary(
            language => language,
            language => new ResourceManager($"{Prefix}.{language}", typeof(CoreText).Assembly),
            StringComparer.OrdinalIgnoreCase);

    // CurrentUICulture flows through async operations. Never change process-wide culture.
    public static string Get(CoreMessage key, string? cultureName = null)
    {
        var culture = ResolveCulture(cultureName);
        var manager = Translations.TryGetValue(culture.TwoLetterISOLanguageName, out var translated)
            ? translated : English;
        return manager.GetString(key.ToString(), CultureInfo.InvariantCulture)
            ?? English.GetString(key.ToString(), CultureInfo.InvariantCulture)
            ?? throw new MissingManifestResourceException($"Missing Core resource: {key}");
    }

    public static string Format(CoreMessage key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

    public static string FormatInCulture(CoreMessage key, string? cultureName, params object?[] arguments) =>
        string.Format(ResolveCulture(cultureName), Get(key, cultureName), arguments);

    private static CultureInfo ResolveCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return CultureInfo.CurrentUICulture;
        try { return CultureInfo.GetCultureInfo(name); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    public static WebShareStrings WebShare(string? cultureName = null)
    {
        var culture = ResolveCulture(cultureName);
        var language = culture.TwoLetterISOLanguageName;
        return new WebShareStrings
        {
            HtmlLang = language == "zh" ? "zh-CN" : Translations.ContainsKey(language) ? language : "en",
            PinPrompt = Get(CoreMessage.WebPinPrompt, culture.Name),
            Pick = Get(CoreMessage.WebPick, culture.Name),
            Waiting = Get(CoreMessage.WebWaiting, culture.Name),
            IncorrectPin = Get(CoreMessage.WebIncorrectPin, culture.Name),
            RateLimited = Get(CoreMessage.RateLimited, culture.Name),
            Busy = Get(CoreMessage.TransferBusy, culture.Name),
            Declined = Get(CoreMessage.WebDeclined, culture.Name),
            TimedOut = Get(CoreMessage.WebTimedOut, culture.Name),
            TooLarge = Get(CoreMessage.WebTooLarge, culture.Name),
            RequestFailed = Get(CoreMessage.WebRequestFailed, culture.Name),
            Uploading = Get(CoreMessage.WebUploading, culture.Name),
            SentFiles = Get(CoreMessage.WebSentFiles, culture.Name),
            SentFile = Get(CoreMessage.WebSentFile, culture.Name),
            NoneAccepted = Get(CoreMessage.WebNoneAccepted, culture.Name),
            UploadFailed = Get(CoreMessage.WebUploadFailed, culture.Name),
        };
    }

    public static string AtLeastOneItem => Get(CoreMessage.AtLeastOneItem);
    public static string PeerCertificateMissing => Get(CoreMessage.PeerCertificateMissing);
    public static string PeerIdentityInvalid => Get(CoreMessage.PeerIdentityInvalid);
    public static string PeerFingerprintMissing => Get(CoreMessage.PeerFingerprintMissing);
    public static string PeerFingerprintMismatch => Get(CoreMessage.PeerFingerprintMismatch);
    public static string PeerRegisterFingerprintMismatch => Get(CoreMessage.PeerRegisterFingerprintMismatch);
    public static string InfoResponseEmpty => Get(CoreMessage.InfoResponseEmpty);
    public static string RegisterResponseEmpty => Get(CoreMessage.RegisterResponseEmpty);
    public static string PrepareResponseEmpty => Get(CoreMessage.PrepareResponseEmpty);
    public static string SourceLengthChanged => Get(CoreMessage.SourceLengthChanged);
    public static string SourceLengthChangedNamed(object? argument0) => Format(CoreMessage.SourceLengthChangedNamed, argument0);
    public static string StreamFactoryNull => Get(CoreMessage.StreamFactoryNull);
    public static string StreamUnreadable => Get(CoreMessage.StreamUnreadable);
    public static string SourcePathRequired => Get(CoreMessage.SourcePathRequired);
    public static string FileNameRequired => Get(CoreMessage.FileNameRequired);
    public static string LengthNonnegative => Get(CoreMessage.LengthNonnegative);
    public static string InvalidSourceSize => Get(CoreMessage.InvalidSourceSize);
    public static string PeerEndpointMissing => Get(CoreMessage.PeerEndpointMissing);
    public static string NodeNotRunning => Get(CoreMessage.NodeNotRunning);
    public static string NodeCannotRestart => Get(CoreMessage.NodeCannotRestart);
    public static string IdentityNotLoaded => Get(CoreMessage.IdentityNotLoaded);
    public static string RequestMissing => Get(CoreMessage.RequestMissing);
    public static string SelectionUnknown => Get(CoreMessage.SelectionUnknown);
    public static string TargetNameUnknown => Get(CoreMessage.TargetNameUnknown);
    public static string DecisionAlreadyMade => Get(CoreMessage.DecisionAlreadyMade);
    public static string TransferBusy => Get(CoreMessage.TransferBusy);
    public static string TransferDeclined => Get(CoreMessage.TransferDeclined);
    public static string DecisionTimedOut => Get(CoreMessage.DecisionTimedOut);
    public static string TransferExpired => Get(CoreMessage.TransferExpired);
    public static string TransferCancelled => Get(CoreMessage.TransferCancelled);
    public static string BodyTooLong => Get(CoreMessage.BodyTooLong);
    public static string BodyTooShort => Get(CoreMessage.BodyTooShort);
    public static string LengthMismatch(object? argument0, object? argument1) => Format(CoreMessage.LengthMismatch, argument0, argument1);
    public static string ContentLengthMismatch => Get(CoreMessage.ContentLengthMismatch);
    public static string ChecksumMismatch => Get(CoreMessage.ChecksumMismatch);
    public static string InvalidFilename => Get(CoreMessage.InvalidFilename);
    public static string InvalidFilenameNamed(object? argument0) => Format(CoreMessage.InvalidFilenameNamed, argument0);
    public static string UnsafePath => Get(CoreMessage.UnsafePath);
    public static string UnsafePathNamed(object? argument0) => Format(CoreMessage.UnsafePathNamed, argument0);
    public static string PathEscapes => Get(CoreMessage.PathEscapes);
    public static string PathEscapesNamed(object? argument0) => Format(CoreMessage.PathEscapesNamed, argument0);
    public static string PathLinked => Get(CoreMessage.PathLinked);
    public static string PathLinkedNamed(object? argument0) => Format(CoreMessage.PathLinkedNamed, argument0);
    public static string ParentMissing => Get(CoreMessage.ParentMissing);
    public static string IdentityIncomplete(object? argument0) => Format(CoreMessage.IdentityIncomplete, argument0);
    public static string IdentityLoadFailed(object? argument0) => Format(CoreMessage.IdentityLoadFailed, argument0);
    public static string IdentityReadFailed => Get(CoreMessage.IdentityReadFailed);
    public static string IdentityAccessDenied => Get(CoreMessage.IdentityAccessDenied);
    public static string IdentityPrivateKeyMissing => Get(CoreMessage.IdentityPrivateKeyMissing);
    public static string PortUnavailable(object? argument0) => Format(CoreMessage.PortUnavailable, argument0);
    public static string DiscoveryStartFailed(object? argument0) => Format(CoreMessage.DiscoveryStartFailed, argument0);
    public static string DiscoveryInterfaceMissing => Get(CoreMessage.DiscoveryInterfaceMissing);
    public static string DiscoveryMembershipFailed => Get(CoreMessage.DiscoveryMembershipFailed);
    public static string PeerPinRequired => Get(CoreMessage.PeerPinRequired);
    public static string PeerPinInvalid => Get(CoreMessage.PeerPinInvalid);
    public static string RateLimited => Get(CoreMessage.RateLimited);
    public static string WebPinRequired => Get(CoreMessage.WebPinRequired);
    public static string WebPinRateLimited => Get(CoreMessage.WebPinRateLimited);
    public static string NoActiveShare => Get(CoreMessage.NoActiveShare);
    public static string WebBrowser => Get(CoreMessage.WebBrowser);
    public static string UnknownDevice => Get(CoreMessage.UnknownDevice);
    public static string Browser => Get(CoreMessage.Browser);
    public static string RemoteAddressMissing => Get(CoreMessage.RemoteAddressMissing);
    public static string RequestTooLarge => Get(CoreMessage.RequestTooLarge);
    public static string InvalidFileMetadata => Get(CoreMessage.InvalidFileMetadata);
    public static string InvalidSenderMetadata => Get(CoreMessage.InvalidSenderMetadata);
    public static string TransferLimitExceeded => Get(CoreMessage.TransferLimitExceeded);
    public static string ClientFingerprintMismatch => Get(CoreMessage.ClientFingerprintMismatch);
    public static string ClientCertificateRequired => Get(CoreMessage.ClientCertificateRequired);
    public static string UploadCredentialsMissing => Get(CoreMessage.UploadCredentialsMissing);
    public static string DownloadCredentialsMissing => Get(CoreMessage.DownloadCredentialsMissing);
    public static string SessionIdMissing => Get(CoreMessage.SessionIdMissing);
    public static string SessionExpired => Get(CoreMessage.SessionExpired);
    public static string UploadCredentialsInvalid => Get(CoreMessage.UploadCredentialsInvalid);
    public static string ExpectedJsonObject => Get(CoreMessage.ExpectedJsonObject);
    public static string FieldStringRequired(object? argument0) => Format(CoreMessage.FieldStringRequired, argument0);
    public static string FieldRequired(object? argument0) => Format(CoreMessage.FieldRequired, argument0);
    public static string FieldIntegerRequired(object? argument0) => Format(CoreMessage.FieldIntegerRequired, argument0);
    public static string FieldBooleanRequired(object? argument0) => Format(CoreMessage.FieldBooleanRequired, argument0);
    public static string PeerPortInvalid => Get(CoreMessage.PeerPortInvalid);
    public static string ProtocolUnsupported => Get(CoreMessage.ProtocolUnsupported);
    public static string TimestampInvalid(object? argument0) => Format(CoreMessage.TimestampInvalid, argument0);
    public static string FileIdMismatch => Get(CoreMessage.FileIdMismatch);
    public static string MissingInfo => Get(CoreMessage.MissingInfo);
    public static string MissingFiles => Get(CoreMessage.MissingFiles);
    public static string FilesObject => Get(CoreMessage.FilesObject);
    public static string FilesArray => Get(CoreMessage.FilesArray);
    public static string DuplicateFileId => Get(CoreMessage.DuplicateFileId);
    public static string DuplicateTokenId => Get(CoreMessage.DuplicateTokenId);
    public static string FileIdBlank => Get(CoreMessage.FileIdBlank);
    public static string SessionIdBlank => Get(CoreMessage.SessionIdBlank);
    public static string UploadTokenBlank => Get(CoreMessage.UploadTokenBlank);
    public static string FingerprintBlank => Get(CoreMessage.FingerprintBlank);
    public static string EndpointAddressMissing => Get(CoreMessage.EndpointAddressMissing);
    public static string PortRange => Get(CoreMessage.PortRange);
    public static string CannotBlank => Get(CoreMessage.CannotBlank);
    public static string MustPositive => Get(CoreMessage.MustPositive);
    public static string TimeoutPositive => Get(CoreMessage.TimeoutPositive);
    public static string ExpirationGreater => Get(CoreMessage.ExpirationGreater);
    public static string MulticastIPv4 => Get(CoreMessage.MulticastIPv4);
    public static string PinBlank => Get(CoreMessage.PinBlank);
    public static string ProbeNotVerified => Get(CoreMessage.ProbeNotVerified);
    public static string PeerNotTrusted => Get(CoreMessage.PeerNotTrusted);
    public static string WebRequestTimedOut => Get(CoreMessage.WebRequestTimedOut);
    public static string IdentityLoadGeneric => Get(CoreMessage.IdentityLoadGeneric);
    public static string InvalidJson => Get(CoreMessage.InvalidJson);
    public static string WebPinPrompt => Get(CoreMessage.WebPinPrompt);
    public static string WebPick => Get(CoreMessage.WebPick);
    public static string WebWaiting => Get(CoreMessage.WebWaiting);
    public static string WebIncorrectPin => Get(CoreMessage.WebIncorrectPin);
    public static string WebDeclined => Get(CoreMessage.WebDeclined);
    public static string WebTimedOut => Get(CoreMessage.WebTimedOut);
    public static string WebTooLarge => Get(CoreMessage.WebTooLarge);
    public static string WebRequestFailed => Get(CoreMessage.WebRequestFailed);
    public static string WebUploading => Get(CoreMessage.WebUploading);
    public static string WebSentFiles => Get(CoreMessage.WebSentFiles);
    public static string WebSentFile => Get(CoreMessage.WebSentFile);
    public static string WebNoneAccepted => Get(CoreMessage.WebNoneAccepted);
    public static string WebUploadFailed => Get(CoreMessage.WebUploadFailed);
    public static string HttpHeaderTooLarge => Get(CoreMessage.HttpHeaderTooLarge);
    public static string HttpRequestLineInvalid => Get(CoreMessage.HttpRequestLineInvalid);
    public static string HttpHeaderInvalid => Get(CoreMessage.HttpHeaderInvalid);
    public static string HttpContentLengthInvalid => Get(CoreMessage.HttpContentLengthInvalid);
    public static string HttpChunkSizeInvalid => Get(CoreMessage.HttpChunkSizeInvalid);
    public static string HttpChunkShort => Get(CoreMessage.HttpChunkShort);
    public static string HttpBodyTruncated => Get(CoreMessage.HttpBodyTruncated);
    public static string HttpChunkLineTooLong => Get(CoreMessage.HttpChunkLineTooLong);
    public static string HttpChunkTerminatorMissing => Get(CoreMessage.HttpChunkTerminatorMissing);
}
