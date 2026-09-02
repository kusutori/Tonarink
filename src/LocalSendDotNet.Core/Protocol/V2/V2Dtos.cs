using System.Text.Json.Serialization;

namespace LocalSendDotNet.Protocol.V2;

internal static class V2Constants
{
    public const string Version = "2.2";
    public const string BasePath = "/api/localsend/v2";
}

internal sealed class DeviceInfoDto
{
    public required string Alias { get; init; }
    public required string Version { get; init; }
    public string? DeviceModel { get; init; }
    public string? DeviceType { get; init; }
    public required string Fingerprint { get; init; }
    public int Port { get; init; }
    public string Protocol { get; init; } = "https";
    public bool Download { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Announce { get; init; }
}

internal sealed class RegisterResponseDto
{
    public required string Alias { get; init; }
    public required string Version { get; init; }
    public string? DeviceModel { get; init; }
    public string? DeviceType { get; init; }
    public string Fingerprint { get; init; } = string.Empty;
    public bool Download { get; init; }
}

internal sealed class FileDto
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public long Size { get; init; }
    public required string FileType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Sha256 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Preview { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FileMetadataDto? Metadata { get; init; }
}

internal sealed class FileMetadataDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Modified { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Accessed { get; init; }
}

internal sealed class PrepareUploadRequestDto
{
    public required DeviceInfoDto Info { get; init; }
    public required Dictionary<string, FileDto> Files { get; init; }
}

internal sealed class WebUploadPrepareRequestDto
{
    public required FileDto[] Files { get; init; }
}

internal sealed class PrepareUploadResponseDto
{
    public required string SessionId { get; init; }
    public required Dictionary<string, string> Files { get; init; }
}

internal sealed class ErrorResponseDto
{
    public required string Message { get; init; }
}

internal sealed class PrepareDownloadResponseDto
{
    public required string SessionId { get; init; }
    public required PrepareDownloadFileDto[] Files { get; init; }
}

internal sealed class PrepareDownloadFileDto
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public long Size { get; init; }
}
